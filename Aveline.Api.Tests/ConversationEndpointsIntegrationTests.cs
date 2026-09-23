using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
                // The credential store is a required deployment setting (it encrypts tenant
                // integration secrets). Delivery reads it to find a channel a boutique has
                // connected, so a host without it can only answer 500 — which is the honest
                // answer to a server that cannot read its own credentials, but not what these
                // tests are about.
                builder.UseSetting(
                    "Credentials:EncryptionKey",
                    // Exactly 32 bytes: the store derives a key from it and refuses anything else.
                    Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes("aveline-test-credentials-key-012")));
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

    /// <summary>
    /// Adds a second user to an existing organization with a chosen boutique role, so the
    /// role-narrowed policy can be exercised with a real membership row.
    /// </summary>
    private async Task<User> SeedMemberAsync(string clerkId, Guid organizationId, string boutiqueRole)
    {
        await using var context = CreateContext();
        var member = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Member",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(member);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = member.Id,
            BoutiqueRole = boutiqueRole,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return member;
    }

    /// <summary>
    /// Persists a SignOff through the real write path, so its `AwaitingSignOff` status and
    /// content hash are produced by the service rather than faked.
    /// </summary>
    private async Task<(Guid messageId, string contentHash)> SeedSignOffAsync(Guid conversationId, string threadId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var stored = await service.ApplyAgentMessageAsync(new AgentMessageEvent(
            conversationId,
            threadId,
            "lina",
            MessageKind.SignOff,
            System.Text.Json.JsonSerializer.SerializeToElement(new[] { new { type = "sign_off", amount = 48000 } }),
            null,
            null));
        return (stored.Id, stored.ContentHash!);
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
    public async Task InvisibleConversation_ListsMessages_With404_Not500()
    {
        // The route advertises a 404 and `SendMessageAsync` already catches; the read used to
        // throw straight into the global handler and answer 500.
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_404", "conversations-404");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations/{Guid.NewGuid()}/messages", token));

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains("Conversation not found.", body);
    }

    [Fact]
    public async Task Send_IsIdempotent_OnClientMessageId()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_idem", "conversations-idem");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        var key = Guid.NewGuid();

        var first = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "On my way.", clientMessageId = key }));
        var second = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token,
            new { text = "On my way.", clientMessageId = key }));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstMessage = await first.Content.ReadFromJsonAsync<MessageDto>();
        var secondMessage = await second.Content.ReadFromJsonAsync<MessageDto>();
        Assert.Equal(firstMessage!.Id, secondMessage!.Id);
        // The key is echoed so a client can reconcile the realtime broadcast of its own send.
        Assert.Equal(key, firstMessage.ClientMessageId);

        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token));
        var page = await list.Content.ReadFromJsonAsync<MessagePage>();
        // Aveline's greeting + exactly one staff note.
        Assert.Equal(2, page!.Total);
    }

    [Fact]
    public async Task Send_WithAReusedKeyAndDifferentText_Is409()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_conflict", "conversations-conflict");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        var key = Guid.NewGuid();

        await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "On my way.", clientMessageId = key }));

        var conflict = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token,
            new { text = "Something else.", clientMessageId = key }));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("message-idempotency-conflict", body);

        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token));
        var page = await list.Content.ReadFromJsonAsync<MessagePage>();
        Assert.Equal(2, page!.Total);
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

    [Fact]
    public async Task Owner_MarksAConversationRead()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_read", "conversations-read");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        var send = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "Hello" }));
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();

        var patch = await _client.SendAsync(AuthorizedJson(HttpMethod.Patch,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/read", token,
            new { lastReadMessageId = message!.Id }));

        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        await using var context = CreateContext();
        var state = Assert.Single(await context.ConversationReadStates
            .Where(s => s.OrganizationId == org.Id)
            .ToListAsync());
        Assert.Equal(owner.Id, state.UserId);
        Assert.Equal(message.Id, state.LastReadMessageId);
    }

    [Fact]
    public async Task MarkRead_RejectsAMessageFromAnotherConversation()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_read_bad", "conversations-read-bad");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var first = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await first.Content.ReadFromJsonAsync<ConversationDto>();
        // A different customer, because the general Salon is get-or-create per user: asking
        // for it twice returns the same thread.
        var second = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)Guid.NewGuid() }));
        var other = await second.Content.ReadFromJsonAsync<ConversationDto>();
        var send = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{other!.Id}/messages", token,
            new { text = "Elsewhere" }));
        var foreign = await send.Content.ReadFromJsonAsync<MessageDto>();

        var patch = await _client.SendAsync(AuthorizedJson(HttpMethod.Patch,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/read", token,
            new { lastReadMessageId = foreign!.Id }));

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task MarkRead_ForAnInvisibleConversation_Is404()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_read_404", "conversations-read-404");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var patch = await _client.SendAsync(AuthorizedJson(HttpMethod.Patch,
            $"/api/v1/orgs/{org.Id}/conversations/{Guid.NewGuid()}/read", token,
            new { lastReadMessageId = Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task SignOffDecide_IsGatedByApprovalsApprove()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_signoff_auth", "conversations-signoff-auth");
        // A membership whose role holds `conversations:view` but not `approvals:approve` is the
        // clean probe for the release gate. (It used to be `org:boutique_staff`; Q8 grants them
        // `approvals:approve` so they can approve a customer order, which moves them inside the
        // grant and makes them the wrong probe.)
        var nonApprover = await SeedMemberAsync("conv_nonapprover_signoff_auth", org.Id, Roles.CustomerRelations);
        var staff = await SeedMemberAsync("conv_staff_signoff_auth", org.Id, Roles.BoutiqueStaff);
        var ownerToken = CreateToken(owner.ClerkId, owner.Email);
        var nonApproverToken = CreateToken(nonApprover.ClerkId, nonApprover.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", ownerToken, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        var (messageId, contentHash) = await SeedSignOffAsync(conversation!.Id, conversation.ThreadId);

        Assert.False(Permissions.IsGranted(Roles.CustomerRelations, Permissions.ApprovalsApprove));
        Assert.True(Permissions.IsGranted(Roles.BoutiqueStaff, Permissions.ApprovalsApprove));

        // Without `approvals:approve` the release gate refuses the caller at the policy, before
        // the conversation is ever looked up.
        var asNonApprover = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off",
            nonApproverToken, new { approved = true, contentHash }));
        Assert.Equal(HttpStatusCode.Forbidden, asNonApprover.StatusCode);

        // A staff member now passes the policy. They still cannot reach a conversation the Salon
        // does not show them, which is why the visibility-aware lookup answers 404 rather than
        // 200: the policy gate and the visibility gate are separate on purpose.
        var staffToken = CreateToken(staff.ClerkId, staff.Email);
        var asStaff = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off",
            staffToken, new { approved = true, contentHash }));
        Assert.Equal(HttpStatusCode.NotFound, asStaff.StatusCode);

        var asOwner = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off",
            ownerToken, new { approved = true, contentHash }));
        Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
    }

    [Fact]
    public async Task SignOffRevoke_IsGatedByApprovalsApprove_AndRelightsTheMarker()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_revoke", "conversations-revoke");
        var nonApprover = await SeedMemberAsync("conv_nonapprover_revoke", org.Id, Roles.CustomerRelations);
        var ownerToken = CreateToken(owner.ClerkId, owner.Email);
        var nonApproverToken = CreateToken(nonApprover.ClerkId, nonApprover.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", ownerToken, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        var (messageId, contentHash) = await SeedSignOffAsync(conversation!.Id, conversation.ThreadId);
        await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off",
            ownerToken, new { approved = true, contentHash }));

        // The revocation route carries the same `approvals:approve` gate as the decision.
        var asNonApprover = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off/revoke",
            nonApproverToken, new { reason = "no" }));
        Assert.Equal(HttpStatusCode.Forbidden, asNonApprover.StatusCode);

        var asOwner = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off/revoke",
            ownerToken, new { reason = "customer changed their mind" }));
        Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
        var revoked = await asOwner.Content.ReadFromJsonAsync<MessageDto>();
        Assert.Equal("AwaitingSignOff", revoked!.Status);

        // The revocation returns the SignOff to the associate's queue, which is exactly what
        // the inbox's derived `approval` marker reads.
        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations", ownerToken));
        var page = await list.Content.ReadFromJsonAsync<ConversationPage>();
        var tile = page!.Items.Single(item => item.Id == conversation.Id);
        Assert.Contains("approval", tile.Markers ?? []);
    }

    [Fact]
    public async Task Revoke_RefusesASignOffThatIsNotApproved()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_revoke_bad", "conversations-revoke-bad");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        var (messageId, _) = await SeedSignOffAsync(conversation!.Id, conversation.ThreadId);

        var revoke = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{messageId}/sign-off/revoke",
            token, new { reason = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, revoke.StatusCode);
    }

    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
    ];

    [Fact]
    public async Task AttachmentUpload_ThenFetch_IsAuthenticatedAndNosniff()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_att", "conversations-att");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var upload = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/attachments", token,
            new { imageData = Convert.ToBase64String(TinyPng), fileName = "photo.png" }));

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(attachment);
        Assert.Equal("image/png", attachment!.ContentType);
        Assert.Equal(TinyPng.LongLength, attachment.SizeBytes);

        // The URL is not anonymous: the catalog's AllowAnonymous image GET is deliberately not
        // copied for a customer's file.
        var anonymous = await _client.GetAsync(attachment.Url);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var fetch = await _client.SendAsync(Authorized(HttpMethod.Get, attachment.Url, token));
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        Assert.Equal("nosniff", fetch.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(TinyPng, await fetch.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AttachmentUpload_RejectsAnOversizeFileAndADisallowedType()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_att_bad", "conversations-att-bad");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var oversize = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/attachments", token,
            new { imageData = Convert.ToBase64String(new byte[(5 * 1024 * 1024) + 1]), fileName = "big.png" }));
        Assert.Equal(HttpStatusCode.BadRequest, oversize.StatusCode);

        var disallowed = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/attachments", token,
            new { imageData = Convert.ToBase64String(TinyPng), fileName = "notes.txt" }));
        Assert.Equal(HttpStatusCode.BadRequest, disallowed.StatusCode);
    }

    [Fact]
    public async Task Send_WithAttachmentIds_BindsThemAndCarriesTheirBlocks()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_att_send", "conversations-att-send");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var upload = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/attachments", token,
            new { imageData = Convert.ToBase64String(TinyPng), fileName = "photo.png" }));
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();

        var send = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token,
            new { text = "Here it is.", attachmentIds = new[] { attachment!.AttachmentId } }));

        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();
        var blocks = message!.ContentBlocks.EnumerateArray().ToList();
        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        Assert.Equal("attachment", blocks[1].GetProperty("type").GetString());
        Assert.Equal(attachment.AttachmentId, blocks[1].GetProperty("attachmentId").GetGuid());

        // A re-list carries the same block, because it is stored with the message.
        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token));
        var page = await list.Content.ReadFromJsonAsync<MessagePage>();
        var reloaded = page!.Items.Single(m => m.Id == message.Id);
        Assert.Contains(reloaded.ContentBlocks.EnumerateArray(),
            b => b.GetProperty("type").GetString() == "attachment");
    }

    [Fact]
    public async Task Send_WithAForeignAttachmentId_Is400()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_att_foreign", "conversations-att-foreign");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var send = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "Here it is.", attachmentIds = new[] { Guid.NewGuid() } }));

        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
    }

    /// <summary>Seeds a client on file, so a thread can be bound to somebody reachable.</summary>
    private async Task<Guid> SeedCustomerAsync(Organization org, string phoneNumber)
    {
        await using var context = CreateContext();
        var customer = new Aveline.Api.Modules.CustomerConcierge.Models.Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = phoneNumber,
            FullName = "Nadia Perera",
            Status = "returning",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task Deliver_WithoutAConnectedChannel_Is409AndSaysSo()
    {
        // Delivery is the outbound channel path, and this deployment has no channel connected, so
        // the honest answer is a refusal that says exactly that — not a note written into the
        // Salon, which would reach nobody while reading as though it had.
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_deliver", "conversations-deliver");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var customerId = await SeedCustomerAsync(org, "94771234567");

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var deliver = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/deliver", token,
            new { text = "Silk Slip Dress · Size M · LKR 24,000" }));

        Assert.Equal(HttpStatusCode.Conflict, deliver.StatusCode);
        var result = await deliver.Content.ReadFromJsonAsync<DeliveryResultDto>();
        Assert.NotNull(result);
        Assert.False(result!.Delivered);
        Assert.Equal("channel_not_connected", result.Refusal);
        Assert.Contains("WhatsApp", result.Detail);

        // Nothing was recorded: a row saying `Sent` is a claim that a customer was messaged.
        var list = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages", token));
        var page = await list.Content.ReadFromJsonAsync<MessagePage>();
        Assert.DoesNotContain(page!.Items, m => m.Status == "Sent");
    }

    [Fact]
    public async Task Deliver_OnAThreadWithNoClient_Is409WithItsOwnRefusal()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_deliver_noclient", "conversations-deliver-noclient");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var deliver = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/deliver", token,
            new { text = "hello" }));

        Assert.Equal(HttpStatusCode.Conflict, deliver.StatusCode);
        var result = await deliver.Content.ReadFromJsonAsync<DeliveryResultDto>();
        Assert.Equal("no_customer", result!.Refusal);
    }

    [Fact]
    public async Task Deliver_OnAnUnknownConversation_Is404()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_deliver_404", "conversations-deliver-404");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var deliver = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{Guid.NewGuid()}/deliver", token,
            new { text = "hello" }));

        Assert.Equal(HttpStatusCode.NotFound, deliver.StatusCode);
    }

    [Fact]
    public async Task Deliver_WithNoWords_Is400()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_deliver_blank", "conversations-deliver-blank");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var deliver = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/deliver", token,
            new { text = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, deliver.StatusCode);
    }

    [Fact]
    public async Task Regenerate_ReRunsTheTurnAndAnswers202()
    {
        // Regeneration re-runs the question, not the answer, so the agent must be asked again and
        // the fresh blocks arrive over the hub rather than in this response.
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_regen", "conversations-regen");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var send = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages", token,
            new { text = "Do we have the slip dress in a medium?" }));
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();
        var runsAfterSend = _agentServer.QueryCount;

        var regenerate = await _client.SendAsync(Authorized(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation.Id}/messages/{message!.Id}/regenerate", token));

        Assert.Equal(HttpStatusCode.Accepted, regenerate.StatusCode);
        // The question is re-run: the agent is asked a second time, with the turn's own words.
        Assert.Equal(runsAfterSend + 1, _agentServer.QueryCount);
        Assert.NotNull(_agentServer.ReceivedBody);
        Assert.Contains("slip dress in a medium", _agentServer.ReceivedBody);
    }

    [Fact]
    public async Task Regenerate_ForAMessageOutsideTheThread_Is404()
    {
        var (owner, org) = await SeedActiveOwnerAsync("conv_owner_regen_404", "conversations-regen-404");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();

        var regenerate = await _client.SendAsync(Authorized(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation!.Id}/messages/{Guid.NewGuid()}/regenerate", token));

        Assert.Equal(HttpStatusCode.NotFound, regenerate.StatusCode);
    }

    private sealed record DeliveryResultDto(bool Delivered, string? Channel, string? ProviderMessageId, MessageDto? Message, string? Refusal, string? Detail);
    private sealed record ConversationDto(Guid Id, string Kind, Guid? CustomerId, string ThreadId, string Status, DateTime? LastMessageAt, System.Collections.Generic.IReadOnlyList<string>? Markers = null);
    private sealed record ConversationPage(System.Collections.Generic.IReadOnlyList<ConversationDto> Items, int Total, int Page, int PageSize);
    private sealed record AttachmentResponse(Guid AttachmentId, string Url, string ContentType, string FileName, long SizeBytes, int? Width, int? Height);
    private sealed record MessageDto(Guid Id, Guid ConversationId, string AuthorKind, string? AgentKey, Guid? AuthorUserId, string Kind, System.Text.Json.JsonElement ContentBlocks, Guid? ReplyToMessageId, string Status, DateTime CreatedAt, Guid? ClientMessageId, Guid? WorkflowRunId);
    private sealed record MessagePage(System.Collections.Generic.IReadOnlyList<MessageDto> Items, int Total, int Page, int PageSize);
}
