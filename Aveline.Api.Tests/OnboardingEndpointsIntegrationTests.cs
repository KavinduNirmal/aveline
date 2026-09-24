using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

public class OnboardingEndpointsIntegrationTests : IAsyncLifetime
{
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
                builder.ConfigureServices(services =>
                {
                    // Replace IAgentServiceClient with mock
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentServiceClient));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddSingleton<IAgentServiceClient, FakeIntegrationAgentClient>();
                });
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? email = null)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("email", email ?? $"{userId}@aveline.lk"),
            new("first_name", "Test"),
            new("last_name", "User"),
            new("user_role", Roles.Staff),
        };

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

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object? payload = null)
    {
        var msg = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        if (payload != null)
        {
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }
        return msg;
    }

    [Fact]
    public async Task AnonymousAccess_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/onboarding/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Full6StepOnboardingFlow_SucceedsEndToEnd()
    {
        var token = CreateToken("user_owner_flow_test");

        // 1. Initial status -> Step 2
        var statusReq = AuthorizedJson(HttpMethod.Get, "/api/v1/onboarding/status", token);
        var statusRes = await _client.SendAsync(statusReq);
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
        var statusBody = await statusRes.Content.ReadFromJsonAsync<OnboardingStatusResponse>();
        Assert.NotNull(statusBody);
        Assert.False(statusBody.HasCompletedOnboarding);
        Assert.Equal(2, statusBody.CurrentStep);

        // 2. Step 3: Save Boutique Details
        var detailsPayload = new SaveBoutiqueDetailsRequest(
            Name: "The Grand Colombo Atelier",
            Address: "50 Park Street, Colombo 02",
            PhoneNumber: "+94 77 987 6543",
            Description: "Couture sarees and bespoke menswear",
            LogoUrl: "https://aveline.lk/logo.png");

        var ownerReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/owner", token, detailsPayload);
        var ownerRes = await _client.SendAsync(ownerReq);
        Assert.Equal(HttpStatusCode.OK, ownerRes.StatusCode);
        var ownerDto = await ownerRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(ownerDto);
        Assert.Equal("The Grand Colombo Atelier", ownerDto.Name);
        Assert.Equal("the-grand-colombo-atelier", ownerDto.Slug);
        Assert.Equal(3, ownerDto.OnboardingStep);

        // 3. Step 4: Choose Plan (Bloom)
        var planReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/plan", token, new SelectPlanRequest(PlanTier.Bloom));
        var planRes = await _client.SendAsync(planReq);
        Assert.Equal(HttpStatusCode.OK, planRes.StatusCode);
        var planDto = await planRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(planDto);
        Assert.Equal(PlanTier.Bloom, planDto.PlanTier);
        Assert.Equal(4, planDto.OnboardingStep);

        // 4. Step 5: Save AI Customization
        var customPayload = new SaveAiCustomizationRequest(
            BrandVoice: "Discreet and sophisticated",
            BusinessRules: "Max discount 12%, exchange within 7 days",
            PreferredColorsFabrics: "Raw silk and French lace");

        var customReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/customize", token, customPayload);
        var customRes = await _client.SendAsync(customReq);
        Assert.Equal(HttpStatusCode.OK, customRes.StatusCode);
        var customDto = await customRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(customDto);
        Assert.Equal("Discreet and sophisticated", customDto.BrandVoice);
        Assert.Equal(5, customDto.OnboardingStep);

        // 5. Step 6: Complete Onboarding
        var completeReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/complete", token);
        var completeRes = await _client.SendAsync(completeReq);
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);
        var completeBody = await completeRes.Content.ReadFromJsonAsync<CompleteOnboardingResponse>();
        Assert.NotNull(completeBody);
        Assert.True(completeBody.Organization.HasCompletedOnboarding);
        Assert.Equal(6, completeBody.Organization.OnboardingStep);
        Assert.Equal("owner", completeBody.UserRole);
        Assert.Equal(Roles.BoutiqueOwner, completeBody.OrganizationRole);
        Assert.Equal(AccountState.Active.ToString(), completeBody.AccountState);
        Assert.Equal(750m, completeBody.BlossomAllocation);
        Assert.True(completeBody.AgentWarmedUp);

        // Verify status endpoint now reports completed
        var finalStatusReq = AuthorizedJson(HttpMethod.Get, "/api/v1/onboarding/status", token);
        var finalStatusRes = await _client.SendAsync(finalStatusReq);
        var finalStatus = await finalStatusRes.Content.ReadFromJsonAsync<OnboardingStatusResponse>();
        Assert.NotNull(finalStatus);
        Assert.True(finalStatus.HasCompletedOnboarding);
        Assert.Equal(6, finalStatus.CurrentStep);
    }

    /// <summary>
    /// Plan §9.1 F1 acceptance (3), priced half: a book that carries a Bloom price makes
    /// <c>POST /api/v1/onboarding/plan</c> return that price on the widened DTO and persist a
    /// <c>Trialing</c> subscription. Defer mode means no checkout and no intent.
    /// </summary>
    [Fact]
    public async Task SelectPlanEndpoint_WithAPricedBloomBook_ReturnsThePriceAndTrials()
    {
        var clerkId = $"user_owner_priced_{Guid.NewGuid():N}";
        var token = CreateToken(clerkId);
        Guid orgId;

        var ownerReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/owner", token,
            new SaveBoutiqueDetailsRequest(
                Name: "The Priced Pavilion",
                Address: "50 Park Street, Colombo 02",
                PhoneNumber: "+94 77 987 6543"));
        var ownerRes = await _client.SendAsync(ownerReq);
        Assert.Equal(HttpStatusCode.OK, ownerRes.StatusCode);
        var ownerDto = await ownerRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(ownerDto);
        orgId = ownerDto.Id;

        await SeedPlanAllowanceAsync(PlanTier.Bloom, 3500m, orgId);

        var planReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/plan", token,
            new SelectPlanRequest(PlanTier.Bloom));
        var planRes = await _client.SendAsync(planReq);

        Assert.Equal(HttpStatusCode.OK, planRes.StatusCode);
        var planDto = await planRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(planDto);
        Assert.Equal(PlanTier.Bloom, planDto.PlanTier);
        Assert.Equal(3500m, planDto.PriceLkr);
        Assert.Equal("LKR", planDto.Currency);
        Assert.Equal("Trialing", planDto.SubscriptionStatus);
        Assert.Null(planDto.PaymentIntentId);
        Assert.Null(planDto.CheckoutUrl);

        // Defer mode never creates a payment intent (plan §9.1 / §14 Q1).
        await using var context = Context();
        var subscription = await context.OrganizationSubscriptions
            .SingleAsync(s => s.OrganizationId == orgId);
        Assert.Equal(3500m, subscription.PriceLkr);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(0, await context.PaymentIntents.CountAsync(i => i.OrganizationId == orgId));
    }

    /// <summary>
    /// Plan §9.1 F1 acceptance (3), unpriced half: a book with no row for the chosen tier must not
    /// be coerced to zero. The tier is still recorded and the subscription still trials.
    /// </summary>
    [Fact]
    public async Task SelectPlanEndpoint_WithAnUnpricedTier_ReturnsANullPriceAndStillTrials()
    {
        var clerkId = $"user_owner_unpriced_{Guid.NewGuid():N}";
        var token = CreateToken(clerkId);
        Guid orgId;

        var ownerReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/owner", token,
            new SaveBoutiqueDetailsRequest(
                Name: "The Unpriced Pavilion",
                Address: "51 Park Street, Colombo 02",
                PhoneNumber: "+94 77 987 6544"));
        var ownerRes = await _client.SendAsync(ownerReq);
        Assert.Equal(HttpStatusCode.OK, ownerRes.StatusCode);
        var ownerDto = await ownerRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(ownerDto);
        orgId = ownerDto.Id;

        // The book is not empty, so this is specifically "no price for this tier", not "no book".
        await SeedPlanAllowanceAsync(PlanTier.Rose, 20000m, Guid.CreateVersion7());

        var planReq = AuthorizedJson(HttpMethod.Post, "/api/v1/onboarding/plan", token,
            new SelectPlanRequest(PlanTier.Bloom));
        var planRes = await _client.SendAsync(planReq);

        Assert.Equal(HttpStatusCode.OK, planRes.StatusCode);
        var planDto = await planRes.Content.ReadFromJsonAsync<OnboardingOrganizationDto>();
        Assert.NotNull(planDto);
        Assert.Null(planDto.PriceLkr);
        Assert.Equal("Trialing", planDto.SubscriptionStatus);
        Assert.Null(planDto.CheckoutUrl);

        await using var context = Context();
        var subscription = await context.OrganizationSubscriptions
            .SingleAsync(s => s.OrganizationId == orgId);
        Assert.Equal(0m, subscription.PriceLkr);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
        .Options);

    /// <summary>
    /// An organisation-scoped <c>PlanAllowance</c> row, which the resolver prefers over the tier
    /// list price, so a test's book cannot leak into another test's organisation.
    /// </summary>
    private static async Task SeedPlanAllowanceAsync(PlanTier tier, decimal priceLkr, Guid organizationId)
    {
        await using var context = Context();
        context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            PlanTier = tier,
            OrganizationId = organizationId,
            SkuKind = BlossomSkuKind.PlanAllowance,
            BlossomQuantity = 750m,
            PriceLkr = priceLkr,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Seeded by the onboarding plan-selection endpoint tests.",
            CreatedByUserId = Guid.CreateVersion7(),
        });
        await context.SaveChangesAsync();
    }

    private class FakeIntegrationAgentClient : IAgentServiceClient
    {
        public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
