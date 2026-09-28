using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Infrastructure.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// The repair for clients that predate eager Salon creation.
///
/// Two clients in the demo boutique had no Salon at all, so they existed in the client book and
/// were invisible in the concierge list - the list is conversations, not clients. Creating the
/// Salon on the create path fixes new clients; this is what fixes the ones already stored, and it
/// is idempotent because it runs on every boot.
/// </summary>
public class CustomerSalonBackfillTests : IAsyncLifetime
{
    private readonly string _databaseName = $"salon_backfill_{Guid.NewGuid():N}";
    private ServiceProvider _provider = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ISignOffDecisionRepository, SignOffDecisionRepository>();
        services.AddScoped<IConversationReadStateRepository, ConversationReadStateRepository>();
        services.AddScoped<IMessageAttachmentRepository, MessageAttachmentRepository>();
        services.AddScoped<IAttachmentStore, DatabaseAttachmentStore>();
        // The repair writes the greeting and never calls the agent; the client is required by the
        // service's constructor and is stubbed rather than reached.
        services.AddSingleton(new Mock<IAgentServiceClient>().Object);
        services.AddLogging();
        services.AddScoped<IConversationService, ConversationService>();
        _provider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static AppDbContext Context(IServiceProvider provider) =>
        provider.GetRequiredService<AppDbContext>();

    private async Task<(Guid OrgId, Guid WithSalon, Guid WithoutSalon, Guid Deleted)> SeedAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = Context(scope.ServiceProvider);

        var orgId = Guid.CreateVersion7();
        var withSalon = Guid.CreateVersion7();
        var withoutSalon = Guid.CreateVersion7();
        var deleted = Guid.CreateVersion7();

        context.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = "Salon Backfill Boutique",
            Slug = $"salon-backfill-{orgId:N}"[..40],
        });

        foreach (var (id, name, deletedAt) in new[]
                 {
                     (withSalon, "Has Salon", (DateTime?)null),
                     (withoutSalon, "No Salon", null),
                     (deleted, "Deleted Client", DateTime.UtcNow),
                 })
        {
            context.Customers.Add(new Customer
            {
                Id = id,
                OrganizationId = orgId,
                FullName = name,
                PhoneNumber = string.Empty,
                Status = "new",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeletedAt = deletedAt,
            });
        }

        // Only the first client already has its thread.
        context.Conversations.Add(new Conversation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = orgId,
            Kind = ConversationKind.Salon,
            CustomerId = withSalon,
            OwnerUserId = null,
            ThreadId = Guid.NewGuid().ToString("N"),
            Status = ConversationStatus.Active,
        });

        await context.SaveChangesAsync();
        return (orgId, withSalon, withoutSalon, deleted);
    }

    [Fact]
    public async Task Run_CreatesTheMissingClientSalon_AndLeavesExistingOnesAlone()
    {
        var (orgId, withSalon, withoutSalon, _) = await SeedAsync();

        await using (var scope = _provider.CreateAsyncScope())
        {
            var created = await CustomerSalonBackfill.RunAsync(
                Context(scope.ServiceProvider),
                scope.ServiceProvider.GetRequiredService<IConversationService>(),
                maxCustomers: 100,
                NullLogger.Instance);

            Assert.Equal(1, created);
        }

        await using var verifyScope = _provider.CreateAsyncScope();
        var verify = Context(verifyScope.ServiceProvider);

        var salons = await verify.Conversations
            .Where(c => c.OrganizationId == orgId && c.Kind == ConversationKind.Salon)
            .ToListAsync();

        Assert.Equal(2, salons.Count);
        Assert.Single(salons, c => c.CustomerId == withSalon);
        var createdSalon = Assert.Single(salons, c => c.CustomerId == withoutSalon);
        // Client-bound Salons are organization-shared (ADR-021): the repair must not invent an owner.
        Assert.Null(createdSalon.OwnerUserId);

        // The repaired Salon is greeted exactly like one created through the API.
        var messages = await verify.Messages
            .Where(m => m.ConversationId == createdSalon.Id)
            .ToListAsync();
        Assert.Single(messages);
    }

    [Fact]
    public async Task Run_IsIdempotent_SoAEveryBootIsSafe()
    {
        await SeedAsync();

        await using (var first = _provider.CreateAsyncScope())
        {
            var created = await CustomerSalonBackfill.RunAsync(
                Context(first.ServiceProvider),
                first.ServiceProvider.GetRequiredService<IConversationService>(),
                maxCustomers: 100,
                NullLogger.Instance);
            Assert.Equal(1, created);
        }

        await using (var second = _provider.CreateAsyncScope())
        {
            var created = await CustomerSalonBackfill.RunAsync(
                Context(second.ServiceProvider),
                second.ServiceProvider.GetRequiredService<IConversationService>(),
                maxCustomers: 100,
                NullLogger.Instance);
            Assert.Equal(0, created);
        }
    }

    [Fact]
    public async Task Run_DoesNotRepairASoftDeletedClient()
    {
        await SeedAsync();

        await using var scope = _provider.CreateAsyncScope();
        var created = await CustomerSalonBackfill.RunAsync(
            Context(scope.ServiceProvider),
            scope.ServiceProvider.GetRequiredService<IConversationService>(),
            maxCustomers: 100,
            NullLogger.Instance);

        // One client was missing a Salon; the deleted one is not resurrected as a thread.
        Assert.Equal(1, created);
    }
}
