using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Aveline.Api.Authorization;
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

        public Task<UserDto?> UpdateProfileAsync(string clerkId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserDto?>(null);

        public Task<UserDto?> DeleteAccountAsync(string clerkId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserDto?>(null);

        public Task<UserDto> ChangeAccountStateAsync(Guid userId, AccountState state, Guid actorUserId, string? reason = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PagedUsers> SearchUsersAsync(string? search, AccountState? state, Guid? organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedUsers([], page, pageSize, 0));
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

    private (OnboardingMiddleware Middleware, DefaultHttpContext Context) CreatePendingRequest(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_pending_path_user")
        }, "TestAuth"));

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_pending_path_user",
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };

        return (new OnboardingMiddleware(NextMiddleware), context);
    }

    [Theory]
    [InlineData("/api/v1/users/me")]
    [InlineData("/api/v1/users/me/sessions")]
    [InlineData("/api/v1/users/onboarding")]
    [InlineData("/api/v1/onboarding/status")]
    [InlineData("/api/v1/onboarding/owner")]
    [InlineData("/api/v1/invitations/abc")]
    [InlineData("/api/v1/auth/claims")]
    [InlineData("/api/v1/admin/requests")]
    public async Task PendingAccount_OnAllowedOnboardingPath_PassesThrough(string path)
    {
        var (middleware, context) = CreatePendingRequest(path);

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.True(_nextCalled);
    }

    [Theory]
    [InlineData("/api/v1/admin")]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/admin/orgs")]
    [InlineData("/api/v1/admin/audit")]
    [InlineData("/api/v1/admin/requests/00000000-0000-0000-0000-000000000000/approve")]
    public async Task PendingAccount_OnGatedAdminPath_Returns403(string path)
    {
        var (middleware, context) = CreatePendingRequest(path);

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task PendingAccount_WithConsoleRole_OnGatedAdminPath_PassesThrough()
    {
        // Aveline console roles are not boutique tenants and never onboard, so the
        // Pending gate must not apply to them once the role is granted.
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/admin/users";

        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_console_member"),
            new Claim(ClaimTypes.Role, Roles.Owner),
        }, "TestAuth"));

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_console_member",
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task PendingAccount_WithStaffRole_OnGatedAdminPath_Returns403()
    {
        // staff / customer_relations are Aveline team roles but do not operate the
        // console, so they remain subject to the onboarding gate.
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/admin/users";
        context.Response.Body = new MemoryStream();

        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_team_staff"),
            new Claim(ClaimTypes.Role, Roles.Staff),
        }, "TestAuth"));

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_team_staff",
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task PendingAccount_WithOnlyBoutiqueRole_OnGatedAdminPath_Returns403()
    {
        // Only Aveline team roles bypass onboarding; a boutique org role must not.
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/admin/users";
        context.Response.Body = new MemoryStream();

        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_boutique_owner"),
            new Claim(ClaimTypes.Role, Roles.BoutiqueOwner),
        }, "TestAuth"));

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_boutique_owner",
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task SuspendedAccount_WithTeamRole_StillReturns403AccountSuspended()
    {
        // The team-role bypass must never resurrect a suspended account.
        var middleware = new OnboardingMiddleware(NextMiddleware);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/admin/users";
        context.Response.Body = new MemoryStream();

        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "clerk_suspended_owner"),
            new Claim(ClaimTypes.Role, Roles.Owner),
        }, "TestAuth"));

        _fakeUserService.UserToReturn = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_suspended_owner",
            HasCompletedOnboarding = true,
            AccountState = AccountState.Suspended,
        };

        await middleware.InvokeAsync(context, _fakeUserService);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
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
