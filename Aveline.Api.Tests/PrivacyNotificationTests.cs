using Aveline.Api.Authorization;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Notifications;
using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// Privacy plan §11 Phase 6 items 6.1 and 6.2.
///
/// <list type="bullet">
///   <item><b>6.1</b> - each privacy <see cref="NotificationType"/> resolves to the boutique's
///     owner(s) and manager(s), and a zero-recipient dispatch returns <c>null</c> without
///     throwing (the dispatcher's existing contract at <c>NotificationDispatcher.cs:52-57</c>).</item>
///   <item><b>6.2</b> - the three producers: a consent revocation, a completed erasure, and a
///     failed disclosure each write exactly one <see cref="NotificationRecord"/> plus one
///     <see cref="UserNotification"/> per recipient.</item>
/// </list>
///
/// Every assertion is written against the persisted rows rather than against a mock's recorder,
/// because the item under test is "the operator's inbox received the event", and only the rows
/// prove that. Payloads are asserted to carry identifiers only: no phone number, no message body.
/// </summary>
public class PrivacyNotificationTests : IAsyncLifetime
{
    private const string Phone = "+94771234567";

    private readonly string _databaseName = $"PrivacyNotifications_{Guid.NewGuid():N}";
    private ServiceProvider _provider = null!;

    /// <summary>Every channel send across the tests, so "the operator was actually told" is provable.</summary>
    private readonly List<(Guid UserId, NotificationType Type)> _deliveries = [];

    private Guid _orgId;
    private Guid _otherOrgId;
    private Guid _ownerId;
    private Guid _managerId;
    private Guid _staffId;
    private Guid _customerId;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        services.AddNotificationsModule(new ConfigurationBuilder().Build());

        // The recipient resolver and the notification repositories read the organisation and user
        // tables, which the real host registers through OrganizationsModule/SharedModule.
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerConsentRepository, CustomerConsentRepository>();

        // Producers under test, wired exactly as PrivacyModule does in the host.
        services.AddScoped<IPhoneSubjectLocator, PhoneSubjectLocator>();
        services.AddScoped<IConsentRevoker, ConsentRevoker>();
        services.AddScoped<IPrivacyNotificationService, PrivacyNotificationService>();
        services.AddScoped<IErasureService, ErasureService>();
        services.AddSingleton<IAuditService>(NullAuditService.Instance);
        services.AddScoped<ICustomerCacheInvalidator, NoopCustomerCacheInvalidator>();
        services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());
        services.AddSingleton(TimeProvider.System);

        // The real channels are SignalR/FCM/email transports. The dispatcher treats them as a switch
        // over the three interfaces, so the recording doubles below are what makes the send side of
        // "one record plus one inbox row per recipient" observable. Replacing them keeps the test
        // about the producer and the resolver rather than about a transport.
        services.RemoveAll<IRealtimeChannel>();
        services.RemoveAll<IPushChannel>();
        services.RemoveAll<IEmailChannel>();
        services.AddSingleton<IRealtimeChannel>(new RecordingRealtimeChannel(_deliveries));
        services.AddSingleton<IPushChannel>(new RecordingPushChannel(_deliveries));
        services.AddSingleton<IEmailChannel>(new RecordingEmailChannel(_deliveries));

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedAsync(db);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private async Task SeedAsync(AppDbContext db)
    {
        var owner = NewUser("privacy-owner", "owner@aveline.lk", Roles.Owner);
        var manager = NewUser("privacy-manager", "manager@aveline.lk", Roles.BoutiqueManager);
        var staff = NewUser("privacy-staff", "staff@aveline.lk", Roles.BoutiqueStaff);
        db.Users.AddRange(owner, manager, staff);

        var org = new Organization { Name = "Emerald Boutique", Slug = $"privacy-{Guid.NewGuid():N}", OwnerUserId = owner.Id };
        var otherOrg = new Organization { Name = "Other Boutique", Slug = $"other-{Guid.NewGuid():N}", OwnerUserId = owner.Id };
        db.Organizations.AddRange(org, otherOrg);
        await db.SaveChangesAsync();

        db.OrganizationMemberships.AddRange(
            Membership(org.Id, owner.Id, Roles.BoutiqueOwner),
            Membership(org.Id, manager.Id, Roles.BoutiqueManager),
            Membership(org.Id, staff.Id, Roles.BoutiqueStaff));
        await db.SaveChangesAsync();

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = Phone,
            FullName = "Sarah Perera",
            Status = "new",
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        _orgId = org.Id;
        _otherOrgId = otherOrg.Id;
        _ownerId = owner.Id;
        _managerId = manager.Id;
        _staffId = staff.Id;
        _customerId = customer.Id;
    }

    private static User NewUser(string clerkId, string email, string boutiqueRole) => new()
    {
        Id = Guid.CreateVersion7(),
        ClerkId = clerkId,
        Email = email,
        FirstName = "Privacy",
        LastName = "Member",
        Username = clerkId,
        UserRole = Roles.Staff,
        OrganizationRole = boutiqueRole,
        HasCompletedOnboarding = true,
    };

    private static OrganizationMembership Membership(Guid orgId, Guid userId, string role) => new()
    {
        OrganizationId = orgId,
        UserId = userId,
        BoutiqueRole = role,
        Status = MembershipStatus.Active,
    };

    private static Notification NotificationFor(NotificationType type, Guid orgId, NotificationTarget target) => new(
        type,
        "Title",
        "Body",
        target,
        new Dictionary<string, string?>(),
        NotificationChannel.Realtime);

    // ----- 6.1 recipient rules ----------------------------------------------------------------

    [Theory]
    [InlineData(NotificationType.ConsentRevoked)]
    [InlineData(NotificationType.PrivacyDeliveryFailed)]
    public async Task EveryPrivacyTypeResolvesToTheOwnerAndTheManager(NotificationType type)
    {
        await using var scope = _provider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRecipientResolver>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // The producer names no roles: the rule must come from the catalog, or a future producer
        // that forgets the target roles would silently broadcast to every member of staff.
        var notification = NotificationFor(type, _orgId, new NotificationTarget(_orgId));

        var record = await notifications.DispatchAsync(notification);

        Assert.NotNull(record);
        Assert.Equal(type, record!.Type);

        var inbox = await InboxRowsAsync();
        Assert.Equal(2, inbox.Count);
        Assert.Contains(_ownerId, inbox);
        Assert.Contains(_managerId, inbox);
        Assert.DoesNotContain(_staffId, inbox);

        // The resolver is the unit under test; resolving directly pins the roles it read.
        var recipients = await resolver.ResolveAsync(notification);
        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, r => r.UserId == _ownerId);
        Assert.Contains(recipients, r => r.UserId == _managerId);
    }

    [Fact]
    public async Task TheDeletionEventResolvesToTheOwnerOnly()
    {
        await using var scope = _provider.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // Plan §8.5.4: an erasure "is a legal event", so it is addressed to the owner and no one
        // else. The manager does not receive it even though they may act on a consent objection.
        var record = await notifications.DispatchAsync(
            NotificationFor(NotificationType.DataDeleted, _orgId, new NotificationTarget(_orgId)));

        Assert.NotNull(record);
        var inbox = await InboxRowsAsync();
        var only = Assert.Single(inbox);
        Assert.Equal(_ownerId, only);
        Assert.DoesNotContain(_managerId, inbox);
        Assert.DoesNotContain(_staffId, inbox);
    }

    [Fact]
    public async Task AZeroRecipientPrivacyDispatchReturnsNullWithoutThrowing()
    {
        await using var scope = _provider.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // An organisation with no active memberships is the zero-recipient case.
        var result = await notifications.DispatchAsync(
            NotificationFor(NotificationType.PrivacyDeliveryFailed, _otherOrgId, new NotificationTarget(_otherOrgId)));

        Assert.Null(result);
        Assert.False(await db.NotificationRecords.AnyAsync());
        Assert.False(await db.UserNotifications.AnyAsync());
    }

    // ----- 6.2 producers ----------------------------------------------------------------------

    [Fact]
    public async Task AConsentRevocationWritesOneRecordAndOneInboxRowPerOwnerAndManager()
    {
        await using var scope = _provider.CreateAsyncScope();
        await SeedConsentAsync(scope.ServiceProvider, ConsentStatuses.Granted);

        var revoker = scope.ServiceProvider.GetRequiredService<IConsentRevoker>();
        await revoker.RevokeAsync(new ConsentRevocationRequest(
            _orgId,
            CustomerId: _customerId,
            PhoneE164: Phone,
            Scope: ConsentRevocationScope.Org,
            Actor: new ConsentActor(
                ConsentActorKinds.Customer,
                ConsentSources.OtpLink,
                ActorRef: PhoneFingerprint.Of(Phone)),
            Reason: "customer_otp_opt_out"));

        var record = await SingleRecordAsync(NotificationType.ConsentRevoked);
        Assert.Equal(_orgId, record.OrganizationId);

        var inbox = await InboxRowsAsync();
        Assert.Equal(2, inbox.Count);
        Assert.Contains(_ownerId, inbox);
        Assert.Contains(_managerId, inbox);

        // Identifiers only - never the number the customer opted out with.
        Assert.DoesNotContain(Phone, record.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStaffRevocationThroughTheConsentServiceAlsoNotifies()
    {
        await using var scope = _provider.CreateAsyncScope();
        await SeedConsentAsync(scope.ServiceProvider, ConsentStatuses.Granted);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new CustomerConsentService(
            new CustomerConsentRepository(db),
            db,
            NullAuditService.Instance,
            TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<INotificationDispatcher>());

        await service.UpdateAsync(_orgId, _customerId, ConsentStatuses.Revoked);

        var record = await SingleRecordAsync(NotificationType.ConsentRevoked);
        Assert.Equal(_orgId, record.OrganizationId);
        Assert.Equal(2, (await InboxRowsAsync()).Count);
    }

    [Fact]
    public async Task ACompletedDeletionWritesOneRecordAndOneInboxRowForTheOwner()
    {
        await using var scope = _provider.CreateAsyncScope();
        await SeedConsentAsync(scope.ServiceProvider, ConsentStatuses.Granted);

        var erasure = scope.ServiceProvider.GetRequiredService<IErasureService>();
        var result = await erasure.EraseAsync(new ErasureRequest(
            _orgId,
            Phone,
            ErasureScope.Org,
            IdempotencyKey: $"privacy-notification-{Guid.NewGuid():N}",
            Actor: new ConsentActor(
                ConsentActorKinds.Customer,
                ConsentSources.OtpLink,
                ActorRef: PhoneFingerprint.Of(Phone))));

        Assert.False(result.Replayed);

        var record = await SingleRecordAsync(NotificationType.DataDeleted);
        Assert.Equal(_orgId, record.OrganizationId);

        // Owner-only (plan §8.5.4).
        var inbox = await InboxRowsAsync();
        Assert.Equal([_ownerId], inbox);

        // Counts only, never the erased content.
        Assert.Contains("customers", record.DataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, record.DataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Sarah Perera", record.DataJson ?? string.Empty, StringComparison.Ordinal);
    }

    // ----- helpers ----------------------------------------------------------------------------

    private async Task SeedConsentAsync(IServiceProvider scope, string status)
    {
        var db = scope.GetRequiredService<AppDbContext>();
        db.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = _customerId,
            ConsentStatus = status,
            ConsentGrantedAt = status == ConsentStatuses.Granted ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<NotificationRecord> SingleRecordAsync(NotificationType type)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.NotificationRecords.SingleAsync();
        Assert.Equal(type, record.Type);
        return record;
    }

    private async Task<List<Guid>> InboxRowsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserNotifications.Select(n => n.UserId).ToListAsync();
    }

    private sealed class NoopCustomerCacheInvalidator : ICustomerCacheInvalidator
    {        public Task<int> InvalidateAsync(
            Guid organizationId,
            Guid customerId,
            string? phoneE164 = null,
            string? fullName = null,
            string? email = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    /// <summary>
    /// The three channel interfaces are separate types on purpose: one recorder implementing all
    /// three would always match the dispatcher's first switch arm and the push and email arms would
    /// never be exercised (the reason <c>NotificationDispatcherTests</c> splits them too).
    /// </summary>
    private sealed class RecordingRealtimeChannel(List<(Guid UserId, NotificationType Type)> deliveries) : IRealtimeChannel
    {
        public Task SendAsync(
            ResolvedRecipient recipient, Notification notification, Guid inboxItemId, int unreadCount,
            CancellationToken cancellationToken = default)
        {
            deliveries.Add((recipient.UserId, notification.Type));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPushChannel(List<(Guid UserId, NotificationType Type)> deliveries) : IPushChannel
    {
        public Task SendAsync(
            ResolvedRecipient recipient, Notification notification, Guid inboxItemId,
            CancellationToken cancellationToken = default)
        {
            deliveries.Add((recipient.UserId, notification.Type));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailChannel(List<(Guid UserId, NotificationType Type)> deliveries) : IEmailChannel
    {
        public Task SendAsync(
            ResolvedRecipient recipient, Notification notification,
            CancellationToken cancellationToken = default)
        {
            deliveries.Add((recipient.UserId, notification.Type));
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Item 6.2, delivery half: a disclosure the provider refused must raise the compliance-visible
/// notification, and the notification must carry identifiers only.
/// </summary>
public class PrivacyDeliveryFailureNotificationTests
{
    private const string To = "+94771234567";
    private static readonly string SigningKey = Convert.ToBase64String(new byte[32]);

    private readonly AppDbContext _context;
    private readonly RecordingPrivacyNotifier _notifier = new();
    private readonly FakeConsentRepository _consent = new();
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    public PrivacyDeliveryFailureNotificationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PrivacyDeliveryFailure_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _consent.Rows[(_orgId, _customerId)] = new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = _customerId,
            ConsentStatus = ConsentStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private DisclosureDispatchService CreateService(OutboundMessageResult outcome)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://app.aveline.lk",
                [PrivacyLinkSigner.ConfigKey] = SigningKey,
            })
            .Build();

        var organizations = new Mock<IOrganizationRepository>();
        organizations
            .Setup(repo => repo.GetByIdAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _orgId, Name = "Emerald Boutique", Slug = "emerald-boutique" });

        return new DisclosureDispatchService(
            _consent,
            organizations.Object,
            _context,
            new FakeOutboundMessaging(outcome),
            new DisclosureBodyBuilder(),
            new PrivacyLinkSigner(configuration),
            new Aveline.Api.Modules.Privacy.Metrics.DisclosureMetrics(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DisclosureDispatchService>.Instance,
            _notifier);
    }

    [Fact]
    public async Task AFailedDisclosureRaisesPrivacyDeliveryFailedWithIdentifiersOnly()
    {
        var service = CreateService(OutboundMessageResult.Failed("Meta refused the message"));

        var result = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.Failed, result.Outcome);

        var failure = Assert.Single(_notifier.DeliveryFailures);
        Assert.Equal(_orgId, failure.OrganizationId);
        Assert.Equal(_customerId, failure.CustomerId);
        Assert.Equal("disclosure", failure.Kind);
        Assert.Equal("provider", failure.Reason);

        // No phone number, no message body, no signing secret and no provider error text. The
        // notification payload is identifiers and a bounded reason only.
        var payload = string.Join("|", failure.Data.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain(To, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Meta refused", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Aveline, an AI assistant", payload, StringComparison.Ordinal);
        Assert.Contains("channel=whatsapp", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChannelThatIsNotConfiguredIsRecordedButNotReportedAsADeliveryFailure()
    {
        // "No credentials" is a boutique that never finished onboarding, not an outage: it is
        // already visible on the integration screen and must not page the owner.
        var service = CreateService(OutboundMessageResult.NotConfigured("no credentials"));

        var result = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.NotConfigured, result.Outcome);
        Assert.Empty(_notifier.DeliveryFailures);
    }

    private sealed class RecordingPrivacyNotifier : IPrivacyNotificationService
    {
        public List<(Guid OrganizationId, Guid? CustomerId, string Kind, string Reason, IReadOnlyDictionary<string, string?> Data)> DeliveryFailures { get; } = [];

        public List<(Guid OrganizationId, Guid CustomerId, string Scope, string Source)> Revocations { get; } = [];

        public List<(Guid OrganizationId, Guid RequestId, string Scope, IReadOnlyDictionary<string, int> Counts)> Deletions { get; } = [];

        public Task ConsentRevokedAsync(
            Guid organizationId,
            Guid customerId,
            string scope,
            string source,
            CancellationToken cancellationToken = default)
        {
            Revocations.Add((organizationId, customerId, scope, source));
            return Task.CompletedTask;
        }

        public Task DataDeletedAsync(
            Guid organizationId,
            Guid requestId,
            string scope,
            IReadOnlyDictionary<string, int> counts,
            CancellationToken cancellationToken = default)
        {
            Deletions.Add((organizationId, requestId, scope, counts));
            return Task.CompletedTask;
        }

        public Task PrivacyDeliveryFailedAsync(
            Guid organizationId,
            Guid? customerId,
            string kind,
            string reason,
            IReadOnlyDictionary<string, string?> data,
            CancellationToken cancellationToken = default)
        {
            DeliveryFailures.Add((organizationId, customerId, kind, reason, data));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboundMessaging(OutboundMessageResult outcome) : Aveline.Api.Modules.Integrations.Services.IOutboundMessagingService
    {
        public Task<OutboundMessageResult> SendWhatsAppTextAsync(
            Guid organizationId, string toE164, string text, string idempotencyKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult(outcome);

        public Task<OutboundMessageResult> SendWhatsAppTemplateAsync(
            Guid organizationId, string toE164, string templateName, string languageCode,
            IReadOnlyList<object>? components, string idempotencyKey,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> IsChannelConfiguredAsync(
            Guid organizationId, string channelKey, CancellationToken cancellationToken = default)
            => Task.FromResult(!outcome.Skipped);
    }

    private sealed class FakeConsentRepository : ICustomerConsentRepository
    {
        public Dictionary<(Guid OrganizationId, Guid CustomerId), CustomerConsent> Rows { get; } = [];

        public Task<CustomerConsent?> GetForCustomerAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.GetValueOrDefault((orgId, customerId)));

        public Task<CustomerConsent> AddAsync(CustomerConsent consent, CancellationToken cancellationToken = default)
        {
            Rows[(consent.OrganizationId, consent.CustomerId)] = consent;
            return Task.FromResult(consent);
        }

        public Task SaveAsync(CustomerConsent consent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryClaimDisclosureAsync(
            Guid orgId, Guid customerId, string disclosureVersion, DateTime shownAt,
            CancellationToken cancellationToken = default)
        {
            var row = Rows.GetValueOrDefault((orgId, customerId));
            if (row is null || row.DisclosureShownAt is not null)
            {
                return Task.FromResult(false);
            }

            row.DisclosureShownAt = shownAt;
            row.DisclosureVersion = disclosureVersion;
            return Task.FromResult(true);
        }

        public Task ReleaseDisclosureAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
        {
            var row = Rows.GetValueOrDefault((orgId, customerId));
            if (row is not null)
            {
                row.DisclosureShownAt = null;
                row.DisclosureVersion = null;
            }

            return Task.CompletedTask;
        }
    }
}
