using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.3 (strategy §5.1 S4, §7 checks 5 and 6) — the agent bridge. Every trigger that reaches
/// <c>/agents/query</c> must carry <c>org_context.attachments</c> and an absolute
/// <c>image_url</c> under <c>Media:PublicBaseUrl</c>, and the organisation it carries must be the
/// real tenant rather than the all-zeros default the graph path used to fall back to.
/// </summary>
/// <remarks>
/// The three assertions are deliberately per call site (<c>ConversationService.cs</c>'s
/// <c>SendStaffNoteAsync</c> call, <c>TriggerInboundDraftAsync</c>'s payload, and
/// <c>TriggerAgentAsync</c>'s payload builder), because the salon plan's R5 is exactly "wired at
/// one site and not the others" and only a per-site test detects it. The agent client is a
/// capturing double, so the assertion is on the bytes that crossed the boundary.
/// </remarks>
public class AgentContextAttachmentTests
{
    private const string PublicBaseUrl = "https://media.aveline.test";

    /// <summary>32 zero bytes, base64: a valid key, not a credential (mirrors the repo's convention).</summary>
    private const string SigningKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<ISignOffDecisionRepository> _signOffDecisions = new();
    private readonly Mock<IConversationReadStateRepository> _readStates = new();
    private readonly Mock<IMessageAttachmentRepository> _attachmentRows = new();
    private readonly Mock<IAttachmentStore> _attachmentStore = new();
    private readonly CapturingAgentClient _agent = new();

    private ConversationService Service()
    {
        var mediaOptions = new MediaOptions
        {
            Provider = MediaProvider.Database,
            PublicBaseUrl = PublicBaseUrl,
            SigningKey = SigningKey,
        };

        var mint = new MediaTokenMintService(
            new HmacMediaUrlSigner(
                Options.Create(mediaOptions), TimeProvider.System, new ClaimedNonceStore()),
            new MissingAssetLocator(),
            Options.Create(mediaOptions));

        return new ConversationService(
            _conversations.Object,
            _messages.Object,
            _signOffDecisions.Object,
            _readStates.Object,
            _attachmentRows.Object,
            _attachmentStore.Object,
            _agent,
            NullLogger<ConversationService>.Instance,
            mint);
    }

    // =======================================================================================
    // Site 1 — ConversationService.cs:271 (SendStaffNoteAsync → TriggerAgentAsync)
    // =======================================================================================

    [Fact]
    public async Task Site271_StaffNote_CarriesTheAttachmentAndAnAbsoluteImageUrl()
    {
        var conversation = Salon();
        var attachment = ImageAttachment(conversation.Id, _userId);
        ArrangeStaffNote(conversation, attachment);

        await Service().SendStaffNoteAsync(
            _orgId, _userId, conversation.Id, "look at this", null, [attachment.Id], CancellationToken.None);

        var context = OrgContext(_agent.LastBody!);
        AssertAttachment(context, attachment, MediaSource.Web);
        AssertAbsoluteImageUrl(context);
    }

    // =======================================================================================
    // Site 2 — ConversationService.cs:787 (TriggerInboundDraftAsync payload)
    // =======================================================================================

    [Fact]
    public async Task Site787_InboundDraft_CarriesTheAttachmentAndAnAbsoluteImageUrl()
    {
        var conversation = Salon();
        var attachment = ImageAttachment(conversation.Id, uploadedByUserId: null);
        ArrangeInbound(conversation, attachment);

        await Service().RecordInboundClientMessageAsync(
            _orgId, "+94771234567", "+94771234567", "Look at this", null, attachment.Id, CancellationToken.None);

        var context = OrgContext(_agent.LastBody!);
        AssertAttachment(context, attachment, MediaSource.WhatsApp);
        AssertAbsoluteImageUrl(context);
    }

    // =======================================================================================
    // Site 3 — ConversationService.cs:842 (the TriggerAgentAsync payload builder itself)
    // =======================================================================================

    [Fact]
    public async Task Site842_AgentPayload_CarriesTheAttachmentAndAnAbsoluteImageUrl()
    {
        var conversation = Salon();
        var attachment = ImageAttachment(conversation.Id, _userId);

        await Service().TriggerAgentAsync(
            conversation, "describe it", _userId, [attachment], CancellationToken.None);

        var context = OrgContext(_agent.LastBody!);
        AssertAttachment(context, attachment, MediaSource.Web);
        AssertAbsoluteImageUrl(context);
    }

    // =======================================================================================
    // Strategy §7 check 6 — the tenant travels with the reference-based request
    // =======================================================================================

    [Fact]
    public async Task EverySite_SendsTheRealOrganisation_NeverTheAllZerosDefault()
    {
        // The graph path used to analyse under 00000000-0000-0000-0000-000000000000 (C15), which
        // the tenant-bound token design refuses. The organisation the C# side puts in
        // `org_context` is the value the Python reference request must echo back, so it can
        // never be the empty default.
        foreach (var context in await AllThreeContextsAsync())
        {
            var organizationId = context.GetProperty("organization_id").GetGuid();
            Assert.Equal(_orgId, organizationId);
            Assert.NotEqual(Guid.Empty, organizationId);
        }
    }

    [Fact]
    public async Task TheMintedImageUrl_IsAbsoluteHttpsUnderPublicBaseUrl_AndInsideTheProviderUrlLimit()
    {
        var context = await StaffNoteContextAsync();
        var url = context.GetProperty("image_url").GetString()!;

        Assert.StartsWith("https://", url);
        Assert.StartsWith($"{PublicBaseUrl}/api/v1/media/", url);
        // The provider documents an 8192-character external-URL ceiling (strategy §3.5). The
        // payload grows with every claim added, so the rendered length is asserted, not assumed.
        Assert.True(url.Length < 8192, $"the token URL is {url.Length} characters");
    }

    [Fact]
    public async Task AnAttachmentTheVisionProviderCannotRead_IsListedButNotTokenised()
    {
        // Strategy §3.6: a stored type outside VisionContentTypes is stored, served and tagged
        // normally, and never handed to the provider. The identity still travels.
        var conversation = Salon();
        var pdf = ImageAttachment(conversation.Id, _userId, "application/pdf", "lookbook.pdf");
        ArrangeStaffNote(conversation, pdf);

        await Service().SendStaffNoteAsync(
            _orgId, _userId, conversation.Id, "the lookbook", null, [pdf.Id], CancellationToken.None);

        var context = OrgContext(_agent.LastBody!);
        Assert.Single(context.GetProperty("attachments").EnumerateArray());
        Assert.False(context.TryGetProperty("image_url", out var url) && url.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task TheBridgePublishesTheReferenceAndOrganisationTheAnalyzeContractReads()
    {
        // The handover to the Python side (lane L5): the identity the C# bridge publishes must be
        // exactly what U2.2's reference arm reads off `POST /internal/visual/analyze-image`, and
        // the organisation must be the one the C# side sent rather than the all-zeros default
        // (strategy §4 C15). Pinned here so a rename on either side fails loudly.
        var context = await StaffNoteContextAsync();
        var reference = context.GetProperty("attachments")[0].GetProperty("reference");

        var request = JsonSerializer.Serialize(new
        {
            organizationId = context.GetProperty("organization_id").GetGuid(),
            imageRefKind = reference.GetProperty("kind").GetString(),
            imageRefId = reference.GetProperty("id").GetGuid(),
        });

        var dto = JsonSerializer.Deserialize<AnalyzeImageDto>(
            request, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(_orgId, dto.OrganizationId);
        Assert.NotEqual(Guid.Empty, dto.OrganizationId);
        Assert.Equal(MediaReferenceKinds.Attachment, dto.ImageRefKind);
        Assert.Equal(reference.GetProperty("id").GetGuid(), dto.ImageRefId);
    }

    // =======================================================================================
    // ADR-026 — the tenant-account audience flag, declared at every site
    // =======================================================================================

    [Fact]
    public async Task EveryStaffSite_DeclaresTheTenantAccountAudience()
    {
        // Aveline reads the boutique's own Blossom balance and seat counts only when the caller
        // says so *explicitly*, and these are the paths a staff member's question travels:
        // SendStaffNoteAsync, and the TriggerAgentAsync payload builder its other callers use.
        // A site that dropped the flag would silently lose the answer rather than leak it, which is
        // still a bug — hence a per-site assertion.
        var staffNote = await StaffNoteContextAsync();
        var agentPayload = await AgentPayloadContextAsync();

        foreach (var context in new[] { staffNote, agentPayload })
        {
            Assert.True(
                context.TryGetProperty("staff_query", out var flag) && flag.ValueKind == JsonValueKind.True,
                "a staff trigger must declare staff_query: true");
        }
    }

    [Fact]
    public async Task TheInboundCustomerSite_DeniesTheTenantAccountAudience()
    {
        // The same flag from the customer path. It is an explicit false rather than an absent key,
        // so a future change to a missing-flag default cannot quietly open the tenant lane to an
        // inbound WhatsApp message.
        var context = await InboundContextAsync();

        Assert.True(context.TryGetProperty("staff_query", out var flag));
        Assert.Equal(JsonValueKind.False, flag.ValueKind);
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private async Task<IReadOnlyList<JsonElement>> AllThreeContextsAsync() =>
    [
        await StaffNoteContextAsync(),
        await InboundContextAsync(),
        await AgentPayloadContextAsync(),
    ];

    /// <summary>The <c>TriggerAgentAsync</c> payload builder, called the way its own callers call it.</summary>
    private async Task<JsonElement> AgentPayloadContextAsync()
    {
        var conversation = Salon();
        var attachment = ImageAttachment(conversation.Id, _userId);

        await Service().TriggerAgentAsync(
            conversation, "describe it", _userId, [attachment], CancellationToken.None);

        return OrgContext(_agent.LastBody!);
    }

    private async Task<JsonElement> StaffNoteContextAsync()
    {
        var conversation = Salon();
        var attachment = ImageAttachment(conversation.Id, _userId);
        ArrangeStaffNote(conversation, attachment);

        await Service().SendStaffNoteAsync(
            _orgId, _userId, conversation.Id, "look", null, [attachment.Id], CancellationToken.None);

        return OrgContext(_agent.LastBody!);
    }

    private async Task<JsonElement> InboundContextAsync()
    {
        var conversation = Salon();
        var attachment = ImageAttachment(conversation.Id, uploadedByUserId: null);
        ArrangeInbound(conversation, attachment);

        await Service().RecordInboundClientMessageAsync(
            _orgId, "+94770000000", "+94770000000", "hi", null, attachment.Id, CancellationToken.None);

        return OrgContext(_agent.LastBody!);
    }

    /// <summary>The <c>TriggerAgentAsync</c> payload builder, called the way its own callers call it.</summary>
    private void ArrangeStaffNote(Conversation conversation, MessageAttachment attachment)
    {
        _conversations
            .Setup(r => r.GetVisibleToUserAsync(_orgId, conversation.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _attachmentRows
            .Setup(r => r.GetBindableAsync(
                _orgId, conversation.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([attachment]);
        _messages.Setup(r => r.SaveAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _attachmentRows.Setup(r => r.SaveAsync(It.IsAny<MessageAttachment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _conversations.Setup(r => r.SaveAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void ArrangeInbound(Conversation conversation, MessageAttachment attachment)
    {
        _conversations
            .Setup(r => r.GetOrCreateSalonByExternalRefAsync(
                _orgId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _attachmentRows
            .Setup(r => r.GetAsync(_orgId, conversation.Id, attachment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attachment);
        _messages.Setup(r => r.SaveAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _attachmentRows.Setup(r => r.SaveAsync(It.IsAny<MessageAttachment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _conversations.Setup(r => r.SaveAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private Conversation Salon() => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = _orgId,
        Kind = ConversationKind.Salon,
        OwnerUserId = _userId,
        ThreadId = "thread-agent-context",
        Status = ConversationStatus.Active,
    };

    private MessageAttachment ImageAttachment(
        Guid conversationId,
        Guid? uploadedByUserId,
        string contentType = "image/png",
        string fileName = "photo.png") => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = _orgId,
        ConversationId = conversationId,
        UploadedByUserId = uploadedByUserId,
        StorageProvider = "database",
        StorageKey = null,
        ContentType = contentType,
        FileName = fileName,
        SizeBytes = 12,
        Url = $"/api/v1/orgs/{_orgId}/conversations/{conversationId}/attachments/pending",
    };

    private static JsonElement OrgContext(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("org_context").Clone();
    }

    private static void AssertAttachment(JsonElement context, MessageAttachment attachment, MediaSource source)
    {
        var entries = context.GetProperty("attachments").EnumerateArray().ToList();
        var entry = Assert.Single(entries);

        Assert.Equal(attachment.Id, entry.GetProperty("attachmentId").GetGuid());
        Assert.Equal(attachment.ContentType, entry.GetProperty("contentType").GetString());
        Assert.Equal(attachment.FileName, entry.GetProperty("fileName").GetString());
        Assert.Equal(source.Value, entry.GetProperty("source").GetString());

        var reference = entry.GetProperty("reference");
        Assert.Equal(MediaReferenceKinds.Attachment, reference.GetProperty("kind").GetString());
        Assert.Equal(attachment.Id, reference.GetProperty("id").GetGuid());
    }

    private static void AssertAbsoluteImageUrl(JsonElement context)
    {
        var url = context.GetProperty("image_url").GetString();
        Assert.False(string.IsNullOrWhiteSpace(url));
        Assert.StartsWith("https://", url);
        Assert.StartsWith($"{PublicBaseUrl}/api/v1/media/", url);
    }

    private sealed class CapturingAgentClient : IAgentServiceClient
    {
        public int PostCount { get; private set; }

        public string? LastPath { get; private set; }

        public string? LastBody { get; private set; }

        public async Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            PostCount++;
            LastPath = path;
            LastBody = await content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class ClaimedNonceStore : IMediaTokenNonceStore
    {
        public Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default)
            => Task.FromResult(MediaNonceClaim.Claimed);
    }

    private sealed class MissingAssetLocator : IMediaAssetLocator
    {
        public Task<MediaAccessAsset?> FindByAssetKeyAsync(
            Guid organizationId, string assetKey, CancellationToken ct = default)
            => Task.FromResult<MediaAccessAsset?>(null);

        public Task<MediaAccessAsset?> FindByReferenceAsync(
            Guid organizationId, string imageRefKind, Guid imageRefId, CancellationToken ct = default)
            => Task.FromResult<MediaAccessAsset?>(null);
    }
}

/// <summary>
/// The disclosure half of the bridge: the minted URL is a bearer credential, so it may only ever
/// travel in the service-to-service <c>org_context</c>. Exercised through the real HTTP endpoint
/// so "no token in any response body or header" is asserted on an actual response.
/// </summary>
public class AgentContextTokenDisclosureTests : IAsyncLifetime
{
    private const string SigningKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private StubAgentServer _agentServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
    ];

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
                builder.UseSetting("Media:PublicBaseUrl", "https://media.aveline.test");
                builder.UseSetting("Media:SigningKey", SigningKey);
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

    [Fact]
    public async Task AStaffNoteWithAnAttachment_NeverLeaksTheMintedTokenToTheCaller()
    {
        var (owner, org) = await SeedActiveOwnerAsync("token_owner", "token-disclosure");
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

        // The agent side received the bridge: the identity plus one absolute, fetchable URL.
        Assert.NotNull(_agentServer.ReceivedBody);
        using var captured = JsonDocument.Parse(_agentServer.ReceivedBody!);
        var context = captured.RootElement.GetProperty("org_context");
        Assert.Equal(1, context.GetProperty("attachments").GetArrayLength());
        var imageUrl = context.GetProperty("image_url").GetString()!;
        Assert.StartsWith("https://media.aveline.test/api/v1/media/", imageUrl);
        var mintedToken = imageUrl["https://media.aveline.test/api/v1/media/".Length..];

        // ... and the caller's own HTTP response carries none of it.
        var body = await send.Content.ReadAsStringAsync();
        Assert.DoesNotContain(mintedToken, body);
        Assert.DoesNotContain("/api/v1/media/", body);
        foreach (var header in send.Headers.Concat(send.Content.Headers))
        {
            foreach (var value in header.Value)
            {
                Assert.DoesNotContain(mintedToken, value);
            }
        }
    }

    private string CreateToken(string userId, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null)
        {
            claims.Add(new Claim("email", email));
        }

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

    private static HttpRequestMessage AuthorizedJson(
        HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };

    private sealed record ConversationDto(Guid Id, string Kind, Guid? CustomerId, string ThreadId, string Status);

    private sealed record AttachmentResponse(
        Guid AttachmentId, string Url, string ContentType, string FileName, long SizeBytes, int? Width, int? Height);
}
