using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aveline.Api.Tests;

/// <summary>
/// Privacy plan §11 Phase 6 item 6.2, the OTP half: an opt-out code the provider refused must raise
/// <see cref="Aveline.Api.Modules.Notifications.Models.NotificationType.PrivacyDeliveryFailed"/>,
/// because the customer cannot complete an opt-out - a rights-accessibility failure, not a cosmetic
/// one. The wire response must stay the constant <c>202</c>, so anti-enumeration is unaffected.
/// </summary>
public class PrivacyOtpDeliveryFailureNotificationTests : IAsyncLifetime
{
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string Phone = "+94771234567";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly RecordingNotifier _notifier = new();
    private readonly FailingOtpDelivery _delivery = new();
    private readonly string _databaseName = $"OtpDeliveryNotification_{Guid.NewGuid():N}";
    private Guid _orgId;

    private sealed class RecordingNotifier : IPrivacyNotificationService
    {
        public List<(Guid OrganizationId, Guid? CustomerId, string Kind, string Reason, IReadOnlyDictionary<string, string?> Data)> DeliveryFailures { get; } = [];

        public Task ConsentRevokedAsync(Guid o, Guid c, string scope, string source, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DataDeletedAsync(Guid o, Guid r, string scope, IReadOnlyDictionary<string, int> counts, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PrivacyDeliveryFailedAsync(
            Guid organizationId, Guid? customerId, string kind, string reason,
            IReadOnlyDictionary<string, string?> data, CancellationToken cancellationToken = default)
        {
            DeliveryFailures.Add((organizationId, customerId, kind, reason, data));
            return Task.CompletedTask;
        }
    }

    /// <summary>Mints a code, then reports a provider refusal on the send.</summary>
    private sealed class FailingOtpDelivery : IOtpDeliveryService
    {
        public Task<OutboundMessageResult> SendAsync(
            Guid organizationId, string phoneE164, string handle, string code,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OutboundMessageResult.Failed("provider refused"));

        public Task<bool> IsConfiguredAsync(Guid organizationId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class StubOtpService : IOtpService
    {
        public Task<OtpIssueResult> IssueAsync(Guid organizationId, string phoneE164, CancellationToken cancellationToken = default)
            => Task.FromResult(new OtpIssueResult("123456", "aGFuZGxl", DateTimeOffset.UtcNow.AddMinutes(5)));

        public Task<bool?> TryStartAsync(string ipAddress, string phoneE164, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(true);

        public Task<OtpVerifyResult> VerifyAsync(string handle, string otp, CancellationToken cancellationToken = default)
            => Task.FromResult(new OtpVerifyResult(false, OtpVerifyFailure.ExpiredOrUnknown, null));
    }

    public async Task InitializeAsync()
    {
        await using (var seed = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseInMemoryDatabase(_databaseName)
                         .Options))
        {
            var ownerId = Guid.CreateVersion7();
            seed.Users.Add(new User
            {
                Id = ownerId,
                ClerkId = "otp-notify-owner",
                Email = "owner@aveline.lk",
                FirstName = "Otp",
                LastName = "Owner",
                Username = "otp-notify-owner",
                UserRole = "owner",
                OrganizationRole = "org:boutique_owner",
            });
            var org = new Organization { Name = "Otp Boutique", Slug = $"otp-notify-{Guid.NewGuid():N}", OwnerUserId = ownerId };
            seed.Organizations.Add(org);
            await seed.SaveChangesAsync();

            seed.Customers.Add(new Customer
            {
                OrganizationId = org.Id,
                PhoneNumber = Phone,
                FullName = "Sarah Perera",
                Status = "new",
            });
            await seed.SaveChangesAsync();
            _orgId = org.Id;
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Privacy:LinkSigningKey", PrivacyKey);
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Observability:AgentIsCritical", "false");
                builder.ConfigureTestServices(services => Override(services, _databaseName, _delivery));
            });

        _client = _factory.CreateClient();
    }

    /// <summary>The DI overrides every case in this class shares.</summary>
    private void Override(IServiceCollection services, string databaseName, IOtpDeliveryService delivery)
    {
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.RemoveAll<AppDbContext>();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));

        services.RemoveAll<IOtpService>();
        services.AddSingleton<IOtpService>(new StubOtpService());
        services.RemoveAll<IOtpDeliveryService>();
        services.AddSingleton(delivery);
        services.RemoveAll<IPrivacyNotificationService>();
        services.AddSingleton<IPrivacyNotificationService>(_notifier);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task AProviderRefusalRaisesTheFailureAndLeavesTheWireResponseUnchanged()
    {
        var signer = _factory.Services.GetRequiredService<IPrivacyLinkSigner>();

        var response = await _client.PostAsJsonAsync("/api/v1/privacy/opt-out/start", new
        {
            organizationId = _orgId,
            phoneNumber = Phone,
            scope = "org",
            version = "1",
            signature = signer.Sign(_orgId, "1"),
        });

        // Anti-enumeration is untouched: a known number with a failing provider is still the
        // constant 202 with the same field set as a successful send (or an unknown number). The
        // handle is opaque and random, so it is the shape - not byte-identity - that is asserted.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var fields = body.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "status", "handle", "expiresInSeconds" }, fields);
            Assert.Equal("accepted", body.RootElement.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("handle").GetString()));
        }

        var failure = Assert.Single(_notifier.DeliveryFailures);
        Assert.Equal(_orgId, failure.OrganizationId);
        Assert.Equal(PrivacyDeliveryKinds.Otp, failure.Kind);
        Assert.Equal(PrivacyDeliveryFailureReasons.Provider, failure.Reason);

        // Identifiers only: the OTP and the number never appear in the payload.
        var payload = string.Join("|", failure.Data.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain("123456", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A boutique that never connected WhatsApp is not an outage: the metric records it, but the
    /// owner is not paged - that state is already visible on the integration screen. The flow reads
    /// as an identical 202 either way.
    /// </summary>
    [Fact]
    public async Task AChannelThatIsNotConfiguredRaisesNoNotification()
    {
        using var host = NewFactory(new NotConfiguredOtpDelivery());
        using var client = host.CreateClient();
        var signer = host.Services.GetRequiredService<IPrivacyLinkSigner>();

        var response = await client.PostAsJsonAsync("/api/v1/privacy/opt-out/start", new
        {
            organizationId = _orgId,
            phoneNumber = Phone,
            scope = "org",
            version = "1",
            signature = signer.Sign(_orgId, "1"),
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(_notifier.DeliveryFailures);
    }

    private WebApplicationFactory<Program> NewFactory(IOtpDeliveryService delivery)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Privacy:LinkSigningKey", PrivacyKey);
                builder.ConfigureTestServices(services => Override(services, _databaseName, delivery));
            });

    private sealed class NotConfiguredOtpDelivery : IOtpDeliveryService
    {
        public Task<OutboundMessageResult> SendAsync(
            Guid organizationId, string phoneE164, string handle, string code,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OutboundMessageResult.NotConfigured("no credentials"));

        public Task<bool> IsConfiguredAsync(Guid organizationId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
