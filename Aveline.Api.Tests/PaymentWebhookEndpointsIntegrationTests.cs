using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.DTOs;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// The anonymous, signature-verified provider webhook of plan §6.4 steps 5-10 (P2-B2). The §7.5
/// rows that need a real delivered webhook live here: M7, M8, M9, M10 and M11.
/// </summary>
public class PaymentWebhookEndpointsIntegrationTests : IAsyncLifetime
{
    private const string WebhookSecret = "whsec_payment-webhook-test";
    private const string WebhookPath = "/api/v1/webhooks/payments/mock";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
            builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
            builder.UseSetting("Payments:Provider", "mock");
            builder.UseSetting("Payments:Mock:Enabled", "true");
            builder.UseSetting("Payments:Mock:AutoSettle", "false");
            builder.UseSetting("Payments:Mock:WebhookSigningSecret", WebhookSecret);
            builder.UseSetting("Payments:Currency", "LKR");
        });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId, string? userRole = null, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
        .Options);

    private static async Task<(Guid OrgId, string ClerkId)> SeedBoutiqueAsync(string suffix)
    {
        var clerkId = $"webhook_owner_{suffix}";
        await using var context = Context();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Webhook",
            LastName = "Owner",
            Username = clerkId,
            UserRole = Roles.BoutiqueOwner,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Webhook Org {suffix}",
            Slug = $"webhook-{suffix}",
            OwnerUserId = owner.Id,
        };
        context.Organizations.Add(org);
        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();

        return (org.Id, clerkId);
    }

    private static async Task SeedTopUpSkuAsync(string skuCode)
    {
        await using var context = Context();
        context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            SkuKind = BlossomSkuKind.TopUpPack,
            SkuCode = skuCode,
            BlossomQuantity = 500m,
            PriceLkr = 9000m,
            Status = BlossomRuleStatus.Active,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();
    }

    /// <summary>Creates a real checkout intent and returns the row the webhook must refer to.</summary>
    private async Task<(Guid OrgId, PaymentIntent Intent)> CreateIntentAsync(string suffix)
    {
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}");

        var checkout = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{orgId}/blossoms/top-ups/checkout",
            token,
            new { skuCode = $"pack_{suffix}" },
            $"webhook-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);

        await using var verify = Context();
        var intent = await verify.PaymentIntents.SingleAsync(row => row.OrganizationId == orgId);
        return (orgId, intent);
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method, string path, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static string BuildBody(
        string eventId,
        string type,
        string providerIntentId,
        long amountMinor,
        string currency,
        DateTimeOffset occurredAt) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            type,
            intentId = providerIntentId,
            refundId = (string?)null,
            amountMinor,
            currency,
            failureCode = (string?)null,
            occurredAt,
        });

    private static HttpRequestMessage SignedWebhook(
        string body, DateTimeOffset at, bool includeSignature = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var headers = MockPaymentProvider.SignedHeaders(WebhookSecret, at, body);
        request.Headers.Add(
            MockPaymentProvider.TimestampHeader, headers[MockPaymentProvider.TimestampHeader]);
        if (includeSignature)
        {
            request.Headers.Add(
                MockPaymentProvider.SignatureHeader, headers[MockPaymentProvider.SignatureHeader]);
        }

        return request;
    }

    private PaymentMetrics Metrics() => _factory.Services.GetRequiredService<PaymentMetrics>();

    // ── M7 ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// M7 — a delivered webhook settles exactly once; a second delivery of the same event id is a
    /// replay the inbox recognises, not a second grant.
    /// </summary>
    [Fact]
    public async Task AReplayedWebhook_IsAccepted_AndSettlesExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, intent) = await CreateIntentAsync(suffix);
        var eventId = $"evt_replay_{suffix}";
        var body = BuildBody(
            eventId, "intent.succeeded", intent.ProviderIntentId!, intent.AmountMinor, "LKR",
            DateTimeOffset.UtcNow);

        var first = await _client.SendAsync(SignedWebhook(body, DateTimeOffset.UtcNow));
        var second = await _client.SendAsync(SignedWebhook(body, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondBody.GetProperty("duplicate").GetBoolean());

        await using var verify = Context();
        var settled = await verify.PaymentIntents.SingleAsync(row => row.Id == intent.Id);
        Assert.Equal(PaymentProviderStatus.Succeeded, settled.Status);
        Assert.NotNull(settled.SettledAt);

        Assert.Single(await verify.BlossomLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        Assert.Single(await verify.IncomeLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());

        var inbox = await verify.PaymentProviderEvents
            .Where(row => row.ProviderEventId == eventId)
            .ToListAsync();
        Assert.Single(inbox);
        Assert.NotNull(inbox[0].ProcessedAt);
    }

    // ── M8 ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>M8 — the replay guard: a correctly signed but ten-minute-old event is refused.</summary>
    [Fact]
    public async Task AStaleTimestamp_Returns403_AndSettlesNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, intent) = await CreateIntentAsync(suffix);
        var body = BuildBody(
            $"evt_late_{suffix}", "intent.succeeded", intent.ProviderIntentId!, intent.AmountMinor,
            "LKR", DateTimeOffset.UtcNow.AddMinutes(-10));

        var response = await _client.SendAsync(
            SignedWebhook(body, DateTimeOffset.UtcNow.AddMinutes(-10)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

        await using var verify = Context();
        Assert.Empty(await verify.BlossomLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        Assert.Equal(
            PaymentProviderStatus.RequiresAction,
            (await verify.PaymentIntents.SingleAsync(row => row.Id == intent.Id)).Status);
    }

    // ── M9 ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>M9 — an unsigned event is refused, and the verification metric moves.</summary>
    [Fact]
    public async Task AnUnsignedWebhook_Returns403_AndIncrementsTheVerificationMetric()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, intent) = await CreateIntentAsync(suffix);
        var body = BuildBody(
            $"evt_unsigned_{suffix}", "intent.succeeded", intent.ProviderIntentId!,
            intent.AmountMinor, "LKR", DateTimeOffset.UtcNow);
        var before = Metrics().VerificationFailureCount("mock", "missing");

        var response = await _client.SendAsync(
            SignedWebhook(body, DateTimeOffset.UtcNow, includeSignature: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before + 1, Metrics().VerificationFailureCount("mock", "missing"));

        await using var verify = Context();
        Assert.Empty(await verify.BlossomLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
    }

    // ── M10 / M11 ───────────────────────────────────────────────────────────────────────────

    /// <summary>M10 — an amount that does not match the intent is a security event, not a receipt.</summary>
    [Fact]
    public async Task AnAmountMismatch_Returns409_AndStoresTheEventUnprocessed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, intent) = await CreateIntentAsync(suffix);
        var eventId = $"evt_amount_{suffix}";
        var body = BuildBody(
            eventId, "intent.succeeded", intent.ProviderIntentId!, intent.AmountMinor + 1,
            "LKR", DateTimeOffset.UtcNow);

        var response = await _client.SendAsync(SignedWebhook(body, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("payment-intent-mismatch", problem.GetProperty("code").GetString());

        await using var verify = Context();
        Assert.Empty(await verify.BlossomLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        Assert.Empty(await verify.IncomeLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        Assert.Equal(
            PaymentProviderStatus.RequiresAction,
            (await verify.PaymentIntents.SingleAsync(row => row.Id == intent.Id)).Status);

        var inbox = await verify.PaymentProviderEvents
            .SingleAsync(row => row.ProviderEventId == eventId);
        Assert.Null(inbox.ProcessedAt);
        Assert.NotNull(inbox.ProcessingError);
    }

    /// <summary>M11 — the currency guard, delivered exactly as M10 is.</summary>
    [Fact]
    public async Task ACurrencyMismatch_Returns409_AndStoresTheEventUnprocessed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, intent) = await CreateIntentAsync(suffix);
        var eventId = $"evt_currency_{suffix}";
        var body = BuildBody(
            eventId, "intent.succeeded", intent.ProviderIntentId!, intent.AmountMinor,
            "USD", DateTimeOffset.UtcNow);

        var response = await _client.SendAsync(SignedWebhook(body, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("payment-intent-mismatch", problem.GetProperty("code").GetString());

        await using var verify = Context();
        Assert.Empty(await verify.BlossomLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        var inbox = await verify.PaymentProviderEvents
            .SingleAsync(row => row.ProviderEventId == eventId);
        Assert.Null(inbox.ProcessedAt);
    }

    /// <summary>
    /// The payload is kept verbatim for forensics, and the row is the replay guard: both are part of
    /// the inbox's contract, not implementation detail.
    /// </summary>
    [Fact]
    public async Task AnAcceptedWebhook_PersistsTheRawPayload()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (_, intent) = await CreateIntentAsync(suffix);
        var eventId = $"evt_raw_{suffix}";
        var body = BuildBody(
            eventId, "intent.succeeded", intent.ProviderIntentId!, intent.AmountMinor,
            "LKR", DateTimeOffset.UtcNow);

        await _client.SendAsync(SignedWebhook(body, DateTimeOffset.UtcNow));

        await using var verify = Context();
        var inbox = await verify.PaymentProviderEvents
            .SingleAsync(row => row.ProviderEventId == eventId);
        Assert.Equal(body, inbox.RawPayload);
        Assert.Equal(intent.ProviderIntentId, inbox.ProviderIntentId);
        Assert.Equal(IncomeSourceKind.BlossomTopUp, (await verify.IncomeLedgerEntries
            .SingleAsync(row => row.OrganizationId == intent.OrganizationId)).SourceKind);
    }
}
