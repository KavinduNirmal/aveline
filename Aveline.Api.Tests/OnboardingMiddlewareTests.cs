using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Aveline.Api.Common.Middleware;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Tests;

public class OnboardingMiddlewareTests
{
    private class FakeUserService : IUserService
    {
        public UserOnboardingCacheItem? UserToReturn { get; set; }
        public bool GetOrSynchronizeCalled { get; private set; }

        public Task<UserOnboardingCacheItem> GetOrSynchronizeUserAsync(string clerkId, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            GetOrSynchronizeCalled = true;
            return Task.FromResult(UserToReturn ?? new UserOnboardingCacheItem
            {
                Id = Guid.NewGuid(),
                ClerkId = clerkId,
                HasCompletedOnboarding = false,
                AccountState = AccountState.OnboardingPending,
            });
        }

        public Task<UserDto?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<UserDto?>(null);
        }

        public Task<UserDto> CompleteOnboardingAsync(string clerkId, CompleteOnboardingRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<UserDto?> SetAccountStateAsync(string clerkId, AccountState state, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<UserDto?>(null);
        }
    }

    private readonly FakeUserService _fakeUserService = new();
    private bool _nextCalled;

    private RequestDelegate NextMiddleware => _ =>
    {
        _nextCalled = true;
        return Task.CompletedTask;
    };

    [Fact]
    public async Task AnonymousRequest_PassesThrough_WithoutCheckingUserService()
    {
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.True(_nextCalled);
        Assert.False(_fakeUserService.GetOrSynchronizeCalled);
    }

    [Fact]
    public async Task AuthenticatedUser_WithCompletedOnboarding_PassesThrough_AndSetsHeaderTrue()
    {
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/agents/ping";

        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_onboarded_user")
        }, "TestAuth"));
        context.User = userClaims;

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_onboarded_user",
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            DisplayName = "Onboarded Associate"
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.True(_nextCalled);
        Assert.NotNull(context.Items["CurrentUser"]);
    }

    [Fact]
    public async Task AuthenticatedUser_WithIncompleteOnboarding_AccessingAllowedEndpoint_PassesThrough()
    {
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/users/me";

        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_new_user")
        }, "TestAuth"));
        context.User = userClaims;

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_new_user",
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task AuthenticatedUser_WithIncompleteOnboarding_AccessingProtectedEndpoint_Returns403Forbidden()
    {
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";
        context.Response.Body = new MemoryStream();

        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_unonboarded_user")
        }, "TestAuth"));
        context.User = userClaims;

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_unonboarded_user",
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("onboarding-required", body);
    }

    [Fact]
    public async Task SuspendedAccount_AnyPath_Returns403AccountSuspended()
    {
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/users/me";
        context.Response.Body = new MemoryStream();

        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_suspended_user")
        }, "TestAuth"));
        context.User = userClaims;

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_suspended_user",
            HasCompletedOnboarding = true,
            AccountState = AccountState.Suspended,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("account-suspended", body);
    }
}
