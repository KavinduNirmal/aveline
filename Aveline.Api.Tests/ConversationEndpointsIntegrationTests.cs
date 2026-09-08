using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for the org-scoped conversation ("The Salon") endpoints
/// (<c>/api/v1/orgs/&#123;organizationId&#125;/conversations</c>).
/// </summary>
public class ConversationEndpointsIntegrationTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private StubAgentServer _agentServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _agentServer = new StubAgentServer();
        await _agentServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl);
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _agentServer.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null) claims.Add(new Claim("email", email));

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

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(User owner, Organization org)> SeedActiveOwnerAsync(string clerkId, string slug)
    {
        await using var context = CreateContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Owner",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = slug.Replace('-', ' '),
            Slug = slug,
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return (owner, org);
    }

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    [Fact]
    public async Task Owner_CreatesAndListsConversations()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_a", "conversations-a");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ConversationDto>();
        Assert.NotNull(created);
        Assert.Equal("Salon", created.Kind);
        Assert.False(string.IsNullOrWhiteSpace(created.ThreadId));

        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await list.Content.ReadFromJsonAsync<ConversationPage>();
        Assert.NotNull(page);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Owner_SendsStaffNote_TriggersAgent()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_b", "conversations-b");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var send = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "Does anything match Michael's request?" }));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();
        Assert.NotNull(message);
        Assert.Equal("Note", message.Kind);
        Assert.Equal("User", message.AuthorKind);
        Assert.Equal(owner.Id, message.AuthorUserId);

        // The agent server should have received the query.
        Assert.NotNull(_agentServer.ReceivedBody);
        Assert.Contains("Michael", _agentServer.ReceivedBody);
    }

    [Fact]
    public async Task Owner_ListsMessages()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_c", "conversations-c");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "Hello" }));

        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await list.Content.ReadFromJsonAsync<MessagePage>();
        Assert.NotNull(page);
        // Aveline's greeting (seeded on salon creation) + the staff note.
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/orgs/00000000-0000-0000-0000-000000000000/conversations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_IsDenied()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_d", "conversations-d");
        // A different user with no membership in the org.
        var outsider = await SeedActiveOwnerAsync("conv_outsider", "conversations-outsider");
        var token = CreateToken(outsider.owner.ClerkId, outsider.owner.Email);

        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations", token));
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    private sealed record ConversationDto(Guid Id, string Kind, Guid? CustomerId, string ThreadId, string Status, DateTime? LastMessageAt);
    private sealed record ConversationPage(System.Collections.Generic.IReadOnlyList<ConversationDto> Items, int Total, int Page, int PageSize);
    private sealed record MessageDto(Guid Id, Guid ConversationId, string AuthorKind, string? AgentKey, Guid? AuthorUserId, string Kind, System.Text.Json.JsonElement ContentBlocks, Guid? ReplyToMessageId, string Status, DateTime CreatedAt);
    private sealed record MessagePage(System.Collections.Generic.IReadOnlyList<MessageDto> Items, int Total, int Page, int PageSize);
}
