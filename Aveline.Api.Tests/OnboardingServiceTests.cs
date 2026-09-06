using System.Net;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

public class OnboardingServiceTests
{
    private readonly AppDbContext _context;
    private readonly OnboardingService _sut;
    private readonly IUserCacheService _cacheService;
    private readonly FakeAgentServiceClient _agentClient;

    public OnboardingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OnboardingServiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        var memCacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        var distCache = new MemoryDistributedCache(memCacheOptions);
        _cacheService = new UserCacheService(distCache, NullLogger<UserCacheService>.Instance);
        _agentClient = new FakeAgentServiceClient();

        _sut = new OnboardingService(
            new OrganizationRepository(_context),
            new UserRepository(_context),
            new UsageRepository(_context),
            _cacheService,
            _agentClient,
            NullLogger<OnboardingService>.Instance);
    }

    private async Task<User> CreateUserAsync(string clerkId = "user_clerk_1")
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@example.com",
            FirstName = "Aveline",
            LastName = "Owner",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetStatusAsync_WhenNoOrg_ReturnsStep2()
    {
        var user = await CreateUserAsync();

        var status = await _sut.GetStatusAsync(user.Id);

        Assert.False(status.HasCompletedOnboarding);
        Assert.Equal(2, status.CurrentStep);
        Assert.Null(status.Organization);
    }

    [Fact]
    public async Task SaveBoutiqueDetailsAsync_CreatesDraftOrgWithStep3()
    {
        var user = await CreateUserAsync();

        var request = new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567",
            Description: "Fine silks and atelier bridal",
            LogoUrl: "https://example.com/logo.png");

        var result = await _sut.SaveBoutiqueDetailsAsync(user.Id, request);

        Assert.Equal("The Silk Pavilion", result.Name);
        Assert.Equal("the-silk-pavilion", result.Slug);
        Assert.Equal(3, result.OnboardingStep);
        Assert.False(result.HasCompletedOnboarding);
    }

    [Fact]
    public async Task SelectPlanAsync_UpdatesPlanTierAndStep4()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));

        var result = await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));

        Assert.Equal(PlanTier.Bloom, result.PlanTier);
        Assert.Equal(4, result.OnboardingStep);
    }

    [Fact]
    public async Task SaveAiCustomizationAsync_WhenSeedTierAttemptsCustomContext_ThrowsArgumentException()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Seed));

        var req = new SaveAiCustomizationRequest(
            BrandVoice: "Poised and discreet",
            BusinessRules: "Max discount 10%");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SaveAiCustomizationAsync(user.Id, req));

        Assert.Contains("not available on the Seed plan", ex.Message);
    }

    [Fact]
    public async Task SaveAiCustomizationAsync_WhenOrchidTier_SavesFullContextAndStep5()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Orchid));

        var req = new SaveAiCustomizationRequest(
            BrandVoice: "Poised and discreet",
            BusinessRules: "Max discount 15%, 14-day exchange",
            PreferredColorsFabrics: "Raw silk, Pashmina, Hand-woven linen",
            CustomerPreferences: "Greet with Ceylon tea, note sizing preferences");

        var result = await _sut.SaveAiCustomizationAsync(user.Id, req);

        Assert.Equal("Poised and discreet", result.BrandVoice);
        Assert.Equal("Raw silk, Pashmina, Hand-woven linen", result.PreferredColorsFabrics);
        Assert.Equal(5, result.OnboardingStep);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_ActivatesOrgUserAndProvisionsBlossoms()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "Atelier Luxe",
            Address: "Colombo 07",
            PhoneNumber: "+94 11 234 5678"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));
        await _sut.SaveAiCustomizationAsync(user.Id, new SaveAiCustomizationRequest(
            BrandVoice: "Warm and inviting"));

        var response = await _sut.CompleteOnboardingAsync(user.Id);

        Assert.True(response.Organization.HasCompletedOnboarding);
        Assert.Equal(6, response.Organization.OnboardingStep);
        Assert.Equal("owner", response.UserRole);
        Assert.Equal("org:principal", response.OrganizationRole);
        Assert.Equal(AccountState.Active.ToString(), response.AccountState);
        Assert.Equal(750m, response.BlossomAllocation);
        Assert.True(response.AgentWarmedUp);

        // Verify database state
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.True(updatedUser.HasCompletedOnboarding);
        Assert.Equal(AccountState.Active, updatedUser.AccountState);
        Assert.Equal("owner", updatedUser.UserRole);
    }

    private class FakeAgentServiceClient : IAgentServiceClient
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
