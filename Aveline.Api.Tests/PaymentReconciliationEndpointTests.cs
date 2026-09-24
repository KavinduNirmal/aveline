using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// P7's reconciliation read over HTTP: <c>GET /api/v1/admin/statistics/payments/reconciliation</c>
/// (plan §18 Appendix C). The route is gated <c>stats:system</c>, matching the rest of the
/// <c>/admin/statistics</c> family — a moderator reads revenue, not system statistics.
/// </summary>
public class PaymentReconciliationEndpointTests : IAsyncLifetime
{
    private const string Path = "/api/v1/admin/statistics/payments/reconciliation";
    private const string WebhookSecret = "whsec_payment-reconciliation-endpoint-test";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Payments:Provider", "mock");
                builder.UseSetting("Payments:Mock:Enabled", "true");
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

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    private string CreateToken(string clerkId, string userRole) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Audience = "aveline-api",
            Subject = new ClaimsIdentity([new Claim("sub", clerkId), new Claim("user_role", userRole)]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        });

    private HttpRequestMessage Authorized(string token, Guid? organizationId = null)
    {
        var path = organizationId is null ? Path : $"{Path}?organizationId={organizationId}";
        return new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
    }

    private static async Task<string> SeedTeamUserAsync(string suffix, string role)
    {
        var clerkId = $"precon_{role}_{suffix}";
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(), ClerkId = clerkId, Email = $"{clerkId}@aveline.lk",
            FirstName = "Payment", LastName = "Recon", Username = clerkId, UserRole = role,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
        return clerkId;
    }

    /// <summary>
    /// Seeds an intent whose provider intent the mock adapter never issued (so `Get` returns null)
    /// plus one unprocessed inbox row: the two classes of divergence this read exists to surface.
    /// </summary>
    private static async Task<Guid> SeedUnreconciledAsync(string suffix)
    {
        var ownerId = Guid.CreateVersion7();
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"precon_owner_{suffix}", Email = $"precon_{suffix}@aveline.lk",
            FirstName = "Payment", LastName = "Owner", Username = $"precon_owner_{suffix}",
            UserRole = Roles.BoutiqueOwner, OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true, AccountState = AccountState.Active,
        });
        var org = new Organization
        {
            Name = $"Payment Recon {suffix}", Slug = $"payment-recon-{suffix}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.PaymentIntents.Add(new PaymentIntent
        {
            OrganizationId = org.Id,
            Provider = "mock",
            ProviderIntentId = $"mock_orphaned_{suffix}",
            Purpose = PaymentPurpose.BlossomTopUp,
            Status = PaymentProviderStatus.RequiresAction,
            AmountMinor = 3500,
            Currency = "LKR",
            PriceLkr = 35m,
            SkuCode = "pack_500",
            BlossomQuantity = 500m,
            Description = "Reconciliation endpoint test intent.",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        });

        context.PaymentProviderEvents.Add(new PaymentProviderEvent
        {
            Provider = "mock",
            ProviderEventId = $"evt_unprocessed_{suffix}",
            EventType = PaymentWebhookEventType.Unknown,
            ProviderIntentId = $"mock_orphaned_{suffix}",
            OccurredAt = DateTime.UtcNow.AddHours(-1),
            ReceivedAt = DateTime.UtcNow.AddHours(-1),
            RawPayload = "{}",
            ProcessedAt = null,
            ProcessingError = "Provider event type 'Unknown' has no settlement effect in this phase.",
        });

        await context.SaveChangesAsync();
        return org.Id;
    }

    [Fact]
    public async Task WithoutAToken_Is401()
    {
        var response = await _client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>`stats:system`, matching the rest of the admin statistics family.</summary>
    [Fact]
    public async Task AModerator_Is403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Moderator);
        var token = CreateToken(clerkId, Roles.Moderator);

        var response = await _client.SendAsync(Authorized(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnOwner_ReadsTheUnreconciledIntentsAndTheUnprocessedBacklog()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var token = CreateToken(clerkId, Roles.Owner);
        var orgId = await SeedUnreconciledAsync(suffix);

        var response = await _client.SendAsync(Authorized(token, orgId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(orgId, body.RootElement.GetProperty("organizationId").GetGuid());
        var intents = body.RootElement.GetProperty("unreconciledIntents").EnumerateArray().ToList();
        var row = Assert.Single(intents);
        Assert.Equal(orgId, row.GetProperty("organizationId").GetGuid());
        Assert.Equal(ReconciliationReasons.ProviderUnknownIntent, row.GetProperty("reason").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("unreconciledCount").GetInt32());
        Assert.True(body.RootElement.GetProperty("unreconciledByProvider").TryGetProperty("mock", out _));

        // The P6 backlog is surfaced by the same read (deliverable 4), not only the intents.
        var backlog = body.RootElement.GetProperty("unprocessedWebhookBacklog").EnumerateArray().ToList();
        Assert.Contains(backlog, item => item.GetProperty("provider").GetString() == "mock");
        Assert.True(body.RootElement.GetProperty("unprocessedBacklogTotal").GetInt64() >= 1);
    }

    /// <summary>
    /// The endpoint and the alert call one derivation: the service the gauge is published from
    /// returns exactly the figure the route reports for the same query.
    /// </summary>
    [Fact]
    public async Task TheEndpoint_ReportsWhatTheSharedDerivationReturns()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var token = CreateToken(clerkId, Roles.Owner);
        var orgId = await SeedUnreconciledAsync(suffix);

        var response = await _client.SendAsync(Authorized(token, orgId));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var endpointCount = body.RootElement.GetProperty("unreconciledCount").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();
        var report = await service.ReconcileAsync(new PaymentReconciliationQuery(
            OrganizationId: orgId, Provider: "mock", From: DateTime.UtcNow.AddDays(-1), To: DateTime.UtcNow));

        Assert.Equal(report.UnreconciledCount, endpointCount);
        Assert.Equal(1, endpointCount);
    }
}
