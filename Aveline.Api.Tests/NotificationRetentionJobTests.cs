using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Jobs;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// The notification retention job (S6): dismissed rows after 30 days, read rows
/// after 180, the audit trail untouched, idempotent, and never running while the
/// distributed lock is held elsewhere.
/// </summary>
public class NotificationRetentionJobTests
{
    [Fact]
    public async Task PurgesDismissedRowsPastTheWindowAndKeepsTheAuditTrail()
    {
        var harness = Build();
        var old = await SeedInboxRowAsync(harness, dismissedAt: DateTime.UtcNow.AddDays(-31), readAt: null);
        var recent = await SeedInboxRowAsync(harness, dismissedAt: DateTime.UtcNow.AddDays(-5), readAt: null);

        var processed = await harness.Job.RunAsync();

        Assert.Equal(1, processed);

        await using var context = harness.Context();
        Assert.False(await context.UserNotifications.AnyAsync(row => row.Id == old.InboxId));
        Assert.True(await context.UserNotifications.AnyAsync(row => row.Id == recent.InboxId));

        // The record and its delivery attempts are the audit trail: kept.
        Assert.Equal(2, await context.NotificationRecords.CountAsync());
        Assert.Equal(2, await context.NotificationDeliveries.CountAsync());

        // Absent from every read the inbox serves.
        var repository = new UserNotificationRepository(context);
        Assert.Null(await repository.GetByIdAsync(old.InboxId, old.UserId));
        var (items, total) = await repository.ListAsync(old.UserId, page: 1, pageSize: 20, unreadOnly: false);
        Assert.Empty(items);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task PurgesReadRowsPastTheWindowAndKeepsTheWindowEdge()
    {
        var harness = Build();
        var old = await SeedInboxRowAsync(harness, dismissedAt: null, readAt: DateTime.UtcNow.AddDays(-181));
        var edge = await SeedInboxRowAsync(harness, dismissedAt: null, readAt: DateTime.UtcNow.AddDays(-179));

        var processed = await harness.Job.RunAsync();

        Assert.Equal(1, processed);

        await using var context = harness.Context();
        Assert.False(await context.UserNotifications.AnyAsync(row => row.Id == old.InboxId));
        Assert.True(await context.UserNotifications.AnyAsync(row => row.Id == edge.InboxId));
    }

    [Fact]
    public async Task UnreadAndUndismissedRowsAreNeverPurged()
    {
        var harness = Build();
        var fresh = await SeedInboxRowAsync(harness, dismissedAt: null, readAt: null);
        // An old row that was neither read nor dismissed is still open work.
        var untouched = await SeedInboxRowAsync(harness, dismissedAt: null, readAt: null, createdAt: DateTime.UtcNow.AddDays(-400));

        var processed = await harness.Job.RunAsync();

        Assert.Equal(0, processed);
        await using var context = harness.Context();
        Assert.True(await context.UserNotifications.AnyAsync(row => row.Id == fresh.InboxId));
        Assert.True(await context.UserNotifications.AnyAsync(row => row.Id == untouched.InboxId));
    }

    [Fact]
    public async Task SecondPassDeletesNothing()
    {
        var harness = Build();
        await SeedInboxRowAsync(harness, dismissedAt: DateTime.UtcNow.AddDays(-31), readAt: null);

        Assert.Equal(1, await harness.Job.RunAsync());
        Assert.Equal(0, await harness.Job.RunAsync());
    }

    [Fact]
    public async Task DoesNotRunWhileTheLockIsHeldElsewhere()
    {
        var harness = Build();
        await SeedInboxRowAsync(harness, dismissedAt: DateTime.UtcNow.AddDays(-31), readAt: null);

        await using (var held = await harness.JobLock.TryAcquireAsync(NotificationRetentionJob.JobName))
        {
            Assert.NotNull(held);
            Assert.Equal(0, await harness.Job.RunLockedAsync());
        }

        // Once released, the same pass runs.
        Assert.Equal(1, await harness.Job.RunLockedAsync());
    }

    [Fact]
    public async Task ReadsItsWindowsFromConfiguration()
    {
        // A shorter dismissed window purges what the defaults would keep.
        var harness = Build(dismissedDays: 1, readDays: 1);
        await SeedInboxRowAsync(harness, dismissedAt: DateTime.UtcNow.AddDays(-2), readAt: null);

        Assert.Equal(1, await harness.Job.RunAsync());
    }

    private static Harness Build(int dismissedDays = 30, int readDays = 180)
    {
        var databaseName = $"NotificationRetention_{Guid.NewGuid()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:DismissedRetentionDays"] = dismissedDays.ToString(),
                ["Notifications:ReadRetentionDays"] = readDays.ToString(),
            })
            .Build();

        var jobLock = new InMemoryDistributedJobLock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IDistributedJobLock>(jobLock);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));

        var provider = services.BuildServiceProvider();
        var job = new NotificationRetentionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            jobLock,
            NullLogger<NotificationRetentionJob>.Instance);

        return new Harness(provider, databaseName, job, jobLock);
    }

    private static async Task<SeededRow> SeedInboxRowAsync(
        Harness harness,
        DateTime? dismissedAt,
        DateTime? readAt,
        DateTime? createdAt = null)
    {
        await using var context = harness.Context();
        var record = new NotificationRecord
        {
            OrganizationId = Guid.CreateVersion7(),
            Type = NotificationType.SystemAlert,
            Title = "Alert",
            Body = "Body",
        };
        context.NotificationRecords.Add(record);
        await context.SaveChangesAsync();

        var userId = Guid.CreateVersion7();
        var inbox = new UserNotification
        {
            UserId = userId,
            NotificationRecordId = record.Id,
            DismissedAt = dismissedAt,
            ReadAt = readAt,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-200),
        };
        context.UserNotifications.Add(inbox);
        context.NotificationDeliveries.Add(new NotificationDelivery
        {
            NotificationRecordId = record.Id,
            UserId = userId,
            Channel = NotificationChannel.Realtime,
            Status = DeliveryStatus.Delivered,
        });
        await context.SaveChangesAsync();

        return new SeededRow(inbox.Id, userId, record.Id);
    }

    private sealed record SeededRow(Guid InboxId, Guid UserId, Guid RecordId);

    private sealed record Harness(
        ServiceProvider Provider,
        string DatabaseName,
        NotificationRetentionJob Job,
        IDistributedJobLock JobLock)
    {
        public AppDbContext Context() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DatabaseName).Options);
    }
}
