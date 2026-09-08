using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Modules.Notifications.Hubs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Tests;

public class NotificationHubTests
{
    private sealed class FakeHubCallerContext : HubCallerContext
    {
        public FakeHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-1")
        {
            User = user;
            ConnectionId = connectionId;
        }

        public override string ConnectionId { get; }
        public override string? UserIdentifier => User?.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<string> AddedGroups { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddedGroups.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestableHub : NotificationHub
    {
        public TestableHub(IUserRepository users, IOrganizationRepository organizations)
            : base(users, organizations)
        {
        }

        public void SetContext(HubCallerContext context) => Context = context;
        public void SetGroups(IGroupManager groups) => Groups = groups;
    }

    private sealed class FakeUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default)
            => Task.FromResult(user);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(user);
        public Task<User> CreateAsync(User user, CancellationToken cancellationToken = default) => Task.FromResult(user);
        public Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default) => Task.FromResult(user);
        public Task<bool> ExistsByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FakeOrganizationRepository(IReadOnlyList<OrganizationMembership> memberships) : IOrganizationRepository
    {
        public Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(memberships);

        public Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Organization?>(null);
        public Task<Organization?> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default) => Task.FromResult<Organization?>(null);
        public Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult<Organization?>(null);
        public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default) => Task.FromResult(organization);
        public Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default) => Task.FromResult(organization);
        public Task<OrganizationMembership?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationMembership?>(null);
        public Task<OrganizationMembership> AddMembershipAsync(OrganizationMembership membership, CancellationToken cancellationToken = default) => Task.FromResult(membership);
        public Task<OrganizationMembership> UpdateMembershipAsync(OrganizationMembership membership, CancellationToken cancellationToken = default) => Task.FromResult(membership);
        public Task RemoveMembershipAsync(OrganizationMembership membership, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UserHasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(memberships);
        public Task<IReadOnlyList<OrganizationMembership>> GetActiveMembersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(memberships);
    }

    private static User MakeUser(Guid id, string clerkId) => new()
    {
        Id = id,
        ClerkId = clerkId,
        Email = $"{clerkId}@aveline.lk",
        FirstName = "SignalR",
        LastName = "User",
        Username = clerkId,
        UserRole = Roles.Staff,
        OrganizationRole = string.Empty,
        HasCompletedOnboarding = true,
        AccountState = AccountState.Active,
    };

    [Fact]
    public async Task OnConnectedAsync_NoUserClaim_Throws()
    {
        var hub = new TestableHub(new FakeUserRepository(null), new FakeOrganizationRepository([]));
        hub.SetContext(new FakeHubCallerContext(user: null));
        hub.SetGroups(new RecordingGroupManager());

        await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());
    }

    [Fact]
    public async Task OnConnectedAsync_AuthenticatedUser_AddsUserAndActiveOrgGroups()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var clerkId = "hub_unit_user";
        var user = MakeUser(userId, clerkId);
        var memberships = new List<OrganizationMembership>
        {
            new() { OrganizationId = orgId, UserId = userId, Status = MembershipStatus.Active },
            new() { OrganizationId = Guid.NewGuid(), UserId = userId, Status = MembershipStatus.Suspended },
        };

        var hub = new TestableHub(new FakeUserRepository(user), new FakeOrganizationRepository(memberships));
        hub.SetContext(new FakeHubCallerContext(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", clerkId) }))));
        var groups = new RecordingGroupManager();
        hub.SetGroups(groups);

        await hub.OnConnectedAsync();

        Assert.Contains($"user:{userId}", groups.AddedGroups);
        Assert.Contains($"org:{orgId}", groups.AddedGroups);
        // Suspended membership must not be joined.
        Assert.Single(groups.AddedGroups, g => g.StartsWith("org:"));
    }

    [Fact]
    public async Task OnConnectedAsync_AuthenticatedUserNotInAveline_Throws()
    {
        var hub = new TestableHub(new FakeUserRepository(null), new FakeOrganizationRepository([]));
        hub.SetContext(new FakeHubCallerContext(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "unknown_clerk") }))));
        hub.SetGroups(new RecordingGroupManager());

        await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());
    }
}
