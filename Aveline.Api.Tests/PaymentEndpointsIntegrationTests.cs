using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.DTOs;
using Aveline.Api.Modules.Payments.Models;
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
/// The shipped checkout, poll and cancel routes of plan §9.2 (P2-B2), plus the Development-only
/// mock checkout endpoints. The §7.5 rows that need an HTTP host live here: M4's wire half, M13,
/// M17 and M18.
/// </summary>
public class PaymentEndpointsIntegrationTests : IAsyncLifetime
{
    private const string WebhookSecret = "whsec_payment-endpoints-test";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(ConfigurePayments);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private void ConfigurePayments(IWebHostBuilder builder)
    {
        builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
        builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
        ApplyPaymentsSettings(builder);
    }

    private static void ApplyPaymentsSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Payments:Provider", "mock");
        builder.UseSetting("Payments:Mock:Enabled", "true");
        builder.UseSetting("Payments:Mock:AutoSettle", "false");
        builder.UseSetting("Payments:Mock:WebhookSigningSecret", WebhookSecret);
        builder.UseSetting("Payments:Currency", "LKR");
    }

    /// <summary>The production host needs the startup guards' own settings (M-1, S-1, Q11).</summary>
    private void ApplyProductionSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
        builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
        builder.UseSetting("Telemetry:IpHashSalt", "test-production-ip-salt");
        builder.UseSetting("Metrics:ScrapeToken", "test-production-scrape-token");
        builder.UseSetting("Media:AllowDatabaseProviderInProduction", "true");
        // Required by the Production database guard: this host deliberately runs on the
        // in-memory provider.
        builder.UseSetting("Database:AllowInMemoryInProduction", "true");
        // Required by the agent client's startup guard (AddAgentServiceClient).
        builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:1");
        builder.UseSetting("AgentService:InternalToken", "test-internal-token");
        builder.UseSetting("Observability:AgentIsCritical", "false");
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

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
        .Options);

    /// <summary>Seeds a boutique whose owner holds the boutique-owner role BillingManage admits.</summary>
    private static async Task<(Guid OrgId, string ClerkId)> SeedBoutiqueAsync(string suffix)
    {
        var clerkId = $"payment_owner_{suffix}";
        await using var context = Context();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Payment",
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
            Name = $"Payment Org {suffix}",
            Slug = $"payment-{suffix}",
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

    private static async Task SeedTopUpSkuAsync(string skuCode, decimal blossomQuantity, decimal priceLkr)
    {
        await using var context = Context();
        context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            SkuKind = BlossomSkuKind.TopUpPack,
            SkuCode = skuCode,
            BlossomQuantity = blossomQuantity,
            PriceLkr = priceLkr,
            Status = BlossomRuleStatus.Active,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();
    }

    private static string CheckoutPath(Guid organizationId) =>
        $"/api/v1/orgs/{organizationId}/blossoms/top-ups/checkout";

    private static string IntentPath(Guid organizationId, Guid intentId) =>
        $"/api/v1/orgs/{organizationId}/payment-intents/{intentId}";

    // ── The shipped routes ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_CreatesAProviderIntent_AndReturnsTheHandoff()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            CheckoutPath(orgId),
            token,
            new { skuCode = $"pack_{suffix}" },
            $"checkout-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();
        Assert.NotNull(body);
        Assert.Equal("mock", body!.Provider);
        Assert.Equal("RequiresAction", body.Status);
        Assert.Equal(9000m, body.AmountLkr);
        Assert.Equal("LKR", body.Currency);
        Assert.Equal(500m, body.BlossomQuantity);
        Assert.False(string.IsNullOrWhiteSpace(body.CheckoutUrl));

        await using var verify = Context();
        var intent = await verify.PaymentIntents.SingleAsync(row => row.OrganizationId == orgId);
        Assert.Equal(body.PaymentIntentId, intent.Id);
        Assert.NotNull(intent.ProviderIntentId);
        // Deliverable 6: the period cap is resolved before payment and persisted on the intent.
        Assert.NotNull(intent.BillingPeriodEnd);
        Assert.Equal(
            new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            intent.BillingPeriodStart);
    }

    [Fact]
    public async Task Checkout_RequiresAnIdempotencyKey()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// M13 — the existing idempotency filter, asserted for the new route: the same key with a
    /// different body is a reuse, not a replay.
    /// </summary>
    [Fact]
    public async Task Checkout_WithAReusedIdempotencyKey_AndADifferentSku_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_a_{suffix}", 500m, 9000m);
        await SeedTopUpSkuAsync($"pack_b_{suffix}", 1000m, 17000m);
        var key = $"reuse-{suffix}";

        var first = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_a_{suffix}" }, key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_b_{suffix}" }, key));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency-key-reuse", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_ReturnsTheIntent_ForItsOwnOrganisation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var checkout = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" },
            $"get-{suffix}"));
        var created = await checkout.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, IntentPath(orgId, created!.PaymentIntentId), token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaymentIntentResponse>();
        Assert.Equal(created.PaymentIntentId, body!.PaymentIntentId);
        Assert.Equal("BlossomTopUp", body.Purpose);
        Assert.Equal("RequiresAction", body.Status);
    }

    /// <summary>M17 — tenant isolation fails closed: another organisation's intent is simply absent.</summary>
    [Fact]
    public async Task Get_WithATokenForAnotherOrganisation_Returns404()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var checkout = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" },
            $"iso-{suffix}"));
        var created = await checkout.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();

        var (otherOrgId, otherClerkId) = await SeedBoutiqueAsync($"other_{suffix}");
        var otherToken = CreateToken(otherClerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, IntentPath(otherOrgId, created!.PaymentIntentId), otherToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AnUnpaidIntent_MovesItToCancelled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var checkout = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" },
            $"cancel-{suffix}"));
        var created = await checkout.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"{IntentPath(orgId, created!.PaymentIntentId)}/cancel?reason=Customer%20abandoned%20the%20checkout.",
            token,
            idempotencyKey: $"cancel-key-{suffix}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaymentIntentResponse>();
        Assert.Equal("Cancelled", body!.Status);
    }

    // ── The refund route (plan §9.6) ────────────────────────────────────────────────────────

    private static string RefundPath(Guid organizationId, Guid intentId) =>
        $"{IntentPath(organizationId, intentId)}/refund";

    private static async Task<Guid> SettleATopUpAsync(
        HttpClient client, Guid organizationId, string token, string suffix)
    {
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var checkout = await client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(organizationId), token, new { skuCode = $"pack_{suffix}" },
            $"refund-checkout-{suffix}"));
        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
        var created = await checkout.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();

        var settle = await client.PostAsync(
            $"/api/v1/dev/mock-checkout/{created!.PaymentIntentId}/settle?token=tok_aveline_succeed",
            content: null);
        Assert.Equal(HttpStatusCode.OK, settle.StatusCode);

        return created.PaymentIntentId;
    }

    /// <summary>
    /// The shipped refund route: the provider is asked first (D8), and the response names both the
    /// provider's refund id and the ledger entry the receipt was written to (plan §9.6).
    /// </summary>
    [Fact]
    public async Task Refund_ASettledCharge_ReportsTheProviderRefundIdAndTheLedgerEntryId()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        var intentId = await SettleATopUpAsync(_client, orgId, token, suffix);

        // `revenue:refund` is the platform-money refund permission and is deliberately denied to
        // `admin`; only the platform owner holds it (plan §9.6).
        var ownerToken = CreateToken($"refund_owner_{suffix}", Roles.Owner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            RefundPath(orgId, intentId),
            ownerToken,
            new { amountLkr = (decimal?)null, reason = "Customer requested a full refund." },
            $"refund-{suffix}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PaymentRefundResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.ProviderRefundId));
        Assert.NotEqual(Guid.Empty, body.LedgerEntryId);
        Assert.Equal(9000m, body.AmountLkr);
        Assert.Equal("Refunded", body.Status);

        await using var verify = Context();
        var refund = await verify.IncomeLedgerEntries
            .SingleAsync(entry => entry.Id == body.LedgerEntryId);
        Assert.Equal(IncomeEntryKind.Refund, refund.Kind);
        Assert.Equal(IncomeChargeBasis.Verified, refund.ChargeBasis);
        Assert.Equal(body.ProviderRefundId, refund.SourceRef);

        var intent = await verify.PaymentIntents
            .SingleAsync(row => row.Id == intentId);
        Assert.NotNull(intent.RefundedAt);
    }

    /// <summary>
    /// M15 at the wire: a refund of an unsettled intent is a state error, not a silent no-op.
    /// </summary>
    [Fact]
    public async Task Refund_AnUnsettledIntent_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var checkout = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" },
            $"pending-{suffix}"));
        var created = await checkout.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            RefundPath(orgId, created!.PaymentIntentId),
            CreateToken($"pending_owner_{suffix}", Roles.Owner),
            new { amountLkr = (decimal?)null, reason = "Customer requested a full refund." },
            $"pending-refund-{suffix}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("payment-intent-state", problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// A boutique owner manages its billing but may not send platform money back: `revenue:refund`
    /// is the platform owner's, so this route answers `403` rather than moving money.
    /// </summary>
    [Fact]
    public async Task Refund_WithATokenThatOnlyHoldsBillingManage_Returns403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        var intentId = await SettleATopUpAsync(_client, orgId, token, suffix);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            RefundPath(orgId, intentId),
            token,
            new { amountLkr = (decimal?)null, reason = "Customer requested a full refund." },
            $"forbidden-refund-{suffix}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── The Development-only mock checkout endpoints ────────────────────────────────────────

    [Fact]
    public async Task MockCheckout_SettlesTheIntent_AndGrantsTheBlossoms()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        var checkout = await _client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" },
            $"settle-{suffix}"));
        var created = await checkout.Content.ReadFromJsonAsync<TopUpCheckoutResponse>();

        var page = await _client.GetAsync($"/api/v1/dev/mock-checkout/{created!.PaymentIntentId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var settle = await _client.PostAsync(
            $"/api/v1/dev/mock-checkout/{created.PaymentIntentId}/settle?token=tok_aveline_succeed",
            content: null);

        Assert.Equal(HttpStatusCode.OK, settle.StatusCode);

        await using var verify = Context();
        var intent = await verify.PaymentIntents.SingleAsync(row => row.Id == created.PaymentIntentId);
        Assert.Equal(PaymentProviderStatus.Succeeded, intent.Status);
        Assert.NotNull(intent.SettledAt);

        var grant = await verify.BlossomLedgerEntries.SingleAsync(row => row.OrganizationId == orgId);
        Assert.Equal(500m, grant.BlossomDelta);

        var income = await verify.IncomeLedgerEntries.SingleAsync(row => row.OrganizationId == orgId);
        Assert.Equal(IncomeChargeBasis.Verified, income.ChargeBasis);
    }

    /// <summary>
    /// M4 — a provider transport failure is a 502 and leaves **no** <c>PaymentIntents</c> row: an
    /// intent with no provider intent behind it is unactionable.
    /// </summary>
    [Fact]
    public async Task Checkout_WhenTheProviderTransportFails_Returns502_AndPersistsNoIntentRow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        await SeedTopUpSkuAsync($"pack_{suffix}", 500m, 9000m);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                ConfigurePayments(builder);
                // Replace the configured adapter with one whose create path cannot be reached.
                builder.ConfigureServices(services =>
                    services.AddKeyedSingleton<IPaymentProvider, UnreachablePaymentProvider>("mock"));
            });
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Authorized(
            HttpMethod.Post, CheckoutPath(orgId), token, new { skuCode = $"pack_{suffix}" },
            $"timeout-{suffix}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("payment-provider-error", body.GetProperty("code").GetString());

        await using var verify = Context();
        Assert.Empty(await verify.PaymentIntents
            .Where(row => row.OrganizationId == orgId)
            .ToListAsync());
    }

    /// <summary>
    /// M18 — a Production-configured host does not expose the mock checkout endpoints. The mapping
    /// itself is asserted from the route table, because the fallback authorization policy answers an
    /// unmatched anonymous request with 401 rather than 404, and "not mapped" is the actual contract.
    /// </summary>
    [Fact]
    public async Task ProductionHost_DoesNotExposeTheMockCheckoutRoutes()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                ApplyProductionSettings(builder);
            });
        using var client = factory.CreateClient();

        var patterns = factory.Services
            .GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            patterns, pattern => pattern.Contains("dev/mock-checkout", StringComparison.OrdinalIgnoreCase));

        var page = await client.GetAsync($"/api/v1/dev/mock-checkout/{Guid.CreateVersion7()}");
        var settle = await client.PostAsync(
            $"/api/v1/dev/mock-checkout/{Guid.CreateVersion7()}/settle?token=tok_aveline_succeed",
            content: null);

        Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, settle.StatusCode);
    }

    /// <summary>
    /// The other half of M18's guard: the Development host **does** map them, so the Production
    /// assertion above is not passing because the routes were never mapped anywhere.
    /// </summary>
    [Fact]
    public void DevelopmentHost_DoesMapTheMockCheckoutRoutes()
    {
        var patterns = _factory.Services
            .GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        Assert.Contains(
            patterns, pattern => pattern.Contains("dev/mock-checkout", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An adapter whose create path is unreachable, so M4 can assert the route's 502 and the
    /// absence of a committed row. Every other call is refused loudly rather than faked.
    /// </summary>
    private sealed class UnreachablePaymentProvider : IPaymentProvider
    {
        public string Key => "mock";

        public PaymentProviderCapabilities Capabilities { get; } = new(
            SupportsRecurringSubscriptions: false,
            SupportsProration: false,
            SupportsPartialRefunds: false,
            SupportsCancelAtPeriodEnd: false,
            SupportsHostedCheckout: true,
            SettlesAsynchronously: true);

        public Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
            CreateProviderIntentRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderTransportException(
                "The configured provider could not be reached while creating the charge.");

        public Task<ProviderPaymentIntent?> GetPaymentIntentAsync(
            string providerIntentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderPaymentIntent?>(null);

        public Task<ProviderPaymentIntent> CancelPaymentIntentAsync(
            string providerIntentId, string reason, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public Task<ProviderRefund> RefundAsync(
            ProviderRefundRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public PaymentWebhookEvent VerifyAndParseWebhook(PaymentWebhookRequest request) =>
            throw new PaymentWebhookVerificationException("signature");

        public Task<ProviderSubscription> CreateOrUpdateSubscriptionAsync(
            CreateProviderSubscriptionRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public Task<ProviderSubscription> CancelSubscriptionAsync(
            string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");
    }
}
