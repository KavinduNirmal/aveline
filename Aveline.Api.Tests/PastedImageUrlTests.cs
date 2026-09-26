using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Media;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// The caller side of the pasted-image-URL path (unit U4.2's CR-2; strategy §5.1 S6; salon plan
/// §7.5 items 10, 11 and 14).
/// </summary>
/// <remarks>
/// <para>
/// The fetcher's own fourteen behaviours are <see cref="ImageUrlFetcherTests"/>'s. What this class
/// asserts is what the endpoint does with a fetch that succeeds and with each refusal: a
/// successful fetch stores the <em>sniffed</em> type and the byte hash with
/// <see cref="MediaSource.Url"/>, and a refused fetch leaves the message's own words untouched and
/// adds no attachment — the send still succeeds, which is the whole point of the fail-closed
/// design (item 14). The one refusal that is not "send without the image" is the kill switch: with
/// the feature disabled the caller is told so explicitly on the response (<c>400</c>, "upload the
/// file instead"), because there is no image to silently drop and the client can act on the answer
/// (strategy §4 C7, §7 check 9).
/// </para>
/// <para>
/// Two seams are replaced through DI and nothing else: <see cref="IImageUrlFetcher"/> (the
/// network) and the <see cref="IAttachmentStore"/> registration (a capturing decorator over the
/// real store). The endpoint still runs its real pipeline — authentication, conversation
/// visibility, storage, binding and broadcast — and the stored row is read back with a fresh
/// <see cref="AppDbContext"/>. The expected hash is recomputed here with <see cref="SHA256"/>
/// rather than by calling the helper under test.
/// </para>
/// </remarks>
public sealed class PastedImageUrlTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private StubAgentServer _agentServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private CapturingLoggerProvider _logs = null!;
    private StubImageUrlFetcher _fetcher = null!;
    private CapturingAttachmentStore _store = null!;


    /// <summary>The sniffed type is <c>image/png</c>; the bytes are a minimal PNG header.</summary>
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _agentServer = new StubAgentServer();
        await _agentServer.StartAsync();

        _fetcher = new StubImageUrlFetcher();
        _logs = new CapturingLoggerProvider();
        _store = new CapturingAttachmentStore();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl);
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IImageUrlFetcher>();
                    services.AddSingleton<IImageUrlFetcher>(_fetcher);
                    services.AddSingleton<ILoggerProvider>(_logs);
                    // The decorator is the one seam that observes what the endpoint handed the
                    // row store, and it forwards to whichever adapter the provider selected.
                    services.RemoveAll<IAttachmentStore>();
                    services.AddScoped<DatabaseAttachmentStore>();
                    services.AddScoped<IAttachmentStore>(provider =>
                        _store.Forward(provider.GetRequiredService<DatabaseAttachmentStore>()));
                });
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

    // =======================================================================================
    // The successful path
    // =======================================================================================

    [Fact]
    public async Task APastedUrl_StoresTheSniffedType_TheByteHashAndTheUrlProvenance_AndBindsIt()
    {
        var (owner, org, conversationId) = await SeedSalonAsync("past_ok", "pasted-ok");

        var send = await SendAsync(org.Id, conversationId, owner, new
        {
            text = "Found it online!",
            imageUrl = "https://cdn.example.com/photo.jpg",
        });

        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();
        Assert.NotNull(message);
        Assert.Equal(["https://cdn.example.com/photo.jpg"], _fetcher.RequestedUrls);

        // The message carries the fetched attachment as a block, so the thread shows it.
        var attachmentBlock = message!.ContentBlocks.EnumerateArray()
            .Single(b => b.GetProperty("type").GetString() == "attachment");

        var stored = await FindAttachmentAsync(conversationId);
        Assert.NotNull(stored);
        Assert.Equal(attachmentBlock.GetProperty("attachmentId").GetGuid(), stored!.Id);

        // The fetcher declared `image/jpeg` over PNG bytes; the bytes decided (strategy §3.6).
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal(TinyPng, stored.ImageData);
        Assert.Equal(TinyPng.LongLength, stored.SizeBytes);
        Assert.Equal(ExpectedHash(TinyPng), stored.ContentHash);
        Assert.Equal(message.Id, stored.MessageId);
        Assert.NotNull(stored.BoundAtUtc);
        Assert.Equal(conversationId, stored.ConversationId);
        Assert.Equal(org.Id, stored.OrganizationId);
        Assert.Equal(owner.Id, stored.UploadedByUserId);

        // The provenance the tagger turns into `source:url` / `s=url`.
        var request = _store.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(MediaSource.Url, request!.Source);
        Assert.Equal(ExpectedHash(TinyPng), request.ContentHash);
    }

    [Fact]
    public async Task APastedUrl_IsFetchedExactlyOnce_WithTheRequestedUrl()
    {
        var (owner, org, conversationId) = await SeedSalonAsync("past_once", "pasted-once");

        await SendAsync(org.Id, conversationId, owner, new
        {
            text = "See this",
            imageUrl = "https://cdn.example.com/one.png",
        });

        Assert.Equal(["https://cdn.example.com/one.png"], _fetcher.RequestedUrls);
        Assert.Equal(1, _store.StoreCalls);
    }

    [Fact]
    public async Task APastedUrl_AndAnUploadedAttachmentId_AreBothBound()
    {
        var (owner, org, conversationId) = await SeedSalonAsync("past_with_upload", "pasted-with-upload");

        var upload = await UploadAsync(org.Id, conversationId, owner, TinyPng, "photo.png");
        var uploaded = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(uploaded);

        var send = await SendAsync(org.Id, conversationId, owner, new
        {
            text = "Both of these.",
            imageUrl = "https://cdn.example.com/photo.jpg",
            attachmentIds = new[] { uploaded!.AttachmentId },
        });

        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();
        var blockIds = message!.ContentBlocks.EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "attachment")
            .Select(b => b.GetProperty("attachmentId").GetGuid())
            .ToList();

        Assert.Equal(2, blockIds.Count);
        Assert.Contains(uploaded.AttachmentId, blockIds);
        Assert.Equal(2, (await AttachmentsAsync(conversationId)).Count);
    }

    // =======================================================================================
    // The kill switch: the one refusal the client is told about
    // =======================================================================================

    [Fact]
    public async Task KillSwitchDisabled_RefusesThePastedUrlWithAnExplicit400_AndStoresNothing()
    {
        // `Media:ImageUrlUploadEnabled` is false by default in the shipped configuration, and
        // strategy §4 C7 made that the safe value everywhere. Disabled means the feature is not
        // offered, not that the image silently vanished: the client is told to upload the file.
        _fetcher.ThrowDisabled = true;

        var (owner, org, conversationId) = await SeedSalonAsync("past_disabled", "pasted-disabled");

        var send = await SendAsync(org.Id, conversationId, owner, new
        {
            text = "Here is a link",
            imageUrl = "https://cdn.example.com/photo.jpg",
        });

        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
        var body = await send.Content.ReadAsStringAsync();
        Assert.Contains("upload", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ImageUrlFetchReasons.Disabled, body);

        // The refusal came from the fetcher's own kill switch, not a caller-side pre-check.
        Assert.Equal(["https://cdn.example.com/photo.jpg"], _fetcher.RequestedUrls);
        Assert.Empty(await AttachmentsAsync(conversationId));
        Assert.Empty(await MessagesAsync(conversationId));
        Assert.Equal(0, _store.StoreCalls);
    }

    // =======================================================================================
    // Every other refusal: the send stands, the words stand, no attachment is added
    // =======================================================================================

    [Theory]
    [InlineData(ImageUrlFetchReasons.NonPublicAddress)]
    [InlineData(ImageUrlFetchReasons.TooLarge)]
    [InlineData(ImageUrlFetchReasons.ContentTypeNotAllowed)]
    [InlineData(ImageUrlFetchReasons.ContentSignatureMismatch)]
    [InlineData(ImageUrlFetchReasons.SchemeNotAllowed)]
    [InlineData(ImageUrlFetchReasons.Timeout)]
    [InlineData(ImageUrlFetchReasons.FetchFailed)]
    [InlineData(ImageUrlFetchReasons.TooManyRedirects)]
    [InlineData(ImageUrlFetchReasons.Unparseable)]
    public async Task ARefusedPastedUrl_LeavesTheTextIntact_AddsNoAttachment_LogsAtWarning_AndStillSends(
        string reason)
    {
        _fetcher.RefusalReason = reason;

        var (owner, org, conversationId) = await SeedSalonAsync($"past_{reason}", $"pasted-{reason}");
        const string words = "The customer asked about this fabric.";

        var send = await SendAsync(org.Id, conversationId, owner, new
        {
            text = words,
            imageUrl = "https://not-public.example.com/photo.jpg?token=SECRETVALUE",
        });

        // The send still succeeds: a fetch that failed must not fail the message (item 14).
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var message = await send.Content.ReadFromJsonAsync<MessageDto>();
        Assert.NotNull(message);

        // The text is the client's own words, untouched, and no attachment block was appended.
        var blocks = message!.ContentBlocks.EnumerateArray().ToList();
        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        Assert.Equal(words, blocks[0].GetProperty("text").GetString());
        Assert.DoesNotContain(blocks, b => b.GetProperty("type").GetString() == "attachment");

        Assert.Empty(await AttachmentsAsync(conversationId));
        Assert.Equal(0, _store.StoreCalls);

        // The caller logs its own Warning, carrying the reason and never the URL.
        var warnings = _logs.Records
            .Where(record => record.Level == LogLevel.Warning)
            .ToList();
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, warning => warning.Message.Contains(reason));
        Assert.DoesNotContain("SECRETVALUE", string.Join('\n', warnings.Select(w => w.Message)));

        // And the agent was still briefed with the message the staff member actually wrote.
        Assert.NotNull(_agentServer.ReceivedBody);
        Assert.Contains(words, _agentServer.ReceivedBody);
    }

    [Fact]
    public async Task ARefusedPastedUrl_LogsTheWarningFromTheCaller_NotOnlyFromTheFetcher()
    {
        _fetcher.RefusalReason = ImageUrlFetchReasons.ContentSignatureMismatch;

        var (owner, org, conversationId) = await SeedSalonAsync("past_log", "pasted-log");

        await SendAsync(org.Id, conversationId, owner, new
        {
            text = "Look",
            imageUrl = "https://cdn.example.com/smuggled.jpg",
        });

        // The stub fetcher logs nothing, so any Warning at all is the endpoint's own.
        var callerWarning = _logs.Records.SingleOrDefault(record =>
            record.Level == LogLevel.Warning
            && record.Category.Contains(nameof(Aveline.Api.Endpoints.ConversationEndpoints)));

        Assert.NotNull(callerWarning);
        Assert.Contains(ImageUrlFetchReasons.ContentSignatureMismatch, callerWarning!.Message);
        Assert.DoesNotContain("cdn.example.com/smuggled", callerWarning.Message);
    }

    [Fact]
    public async Task ASendWithNoImageUrl_DoesNotCallTheFetcherAtAll()
    {
        var (owner, org, conversationId) = await SeedSalonAsync("past_none", "pasted-none");

        var send = await SendAsync(org.Id, conversationId, owner, new { text = "Just words." });

        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        Assert.Empty(_fetcher.RequestedUrls);
        Assert.Empty(await AttachmentsAsync(conversationId));
    }

    [Fact]
    public async Task ABlankImageUrl_IsTreatedAsAbsent_NotAsAFetch()
    {
        var (owner, org, conversationId) = await SeedSalonAsync("past_blank", "pasted-blank");

        var send = await SendAsync(org.Id, conversationId, owner, new
        {
            text = "No link, then.",
            imageUrl = "   ",
        });

        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        Assert.Empty(_fetcher.RequestedUrls);
        Assert.Empty(await AttachmentsAsync(conversationId));
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private static string ExpectedHash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private async Task<(User owner, Organization org, Guid conversationId)> SeedSalonAsync(
        string clerkId, string slug)
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
            Id = Guid.CreateVersion7(),
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

        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = org.Id,
            OwnerUserId = owner.Id,
            Kind = ConversationKind.Salon,
            ThreadId = $"{slug}-thread",
            Status = ConversationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();

        return (owner, org, conversation.Id);
    }

    private Task<HttpResponseMessage> SendAsync(Guid orgId, Guid conversationId, User owner, object payload)
    {
        var token = CreateToken(owner.ClerkId, owner.Email);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orgs/{orgId}/conversations/{conversationId}/messages")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> UploadAsync(
        Guid orgId, Guid conversationId, User owner, byte[] bytes, string fileName)
    {
        var token = CreateToken(owner.ClerkId, owner.Email);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orgs/{orgId}/conversations/{conversationId}/attachments")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new
            {
                imageData = Convert.ToBase64String(bytes),
                fileName,
            }),
        };
        return _client.SendAsync(request);
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
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<MessageAttachment?> FindAttachmentAsync(Guid conversationId)
    {
        await using var context = CreateContext();
        return await context.MessageAttachments.FirstOrDefaultAsync(a => a.ConversationId == conversationId);
    }

    private static async Task<IReadOnlyList<MessageAttachment>> AttachmentsAsync(Guid conversationId)
    {
        await using var context = CreateContext();
        return await context.MessageAttachments
            .Where(a => a.ConversationId == conversationId)
            .ToListAsync();
    }

    private static async Task<IReadOnlyList<Message>> MessagesAsync(Guid conversationId)
    {
        await using var context = CreateContext();
        return await context.Messages
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync();
    }

    /// <summary>
    /// The fetcher seam. It never touches the network: it returns a sniffed fetch or throws the
    /// typed refusal the production fetcher raises, so what is tested is the endpoint's mapping.
    /// </summary>
    private sealed class StubImageUrlFetcher : IImageUrlFetcher
    {
        public List<string> RequestedUrls { get; } = [];

        public string? RefusalReason { get; set; }

        public bool ThrowDisabled { get; set; }

        public Task<FetchedImage> FetchAsync(string? imageUrl, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(imageUrl ?? string.Empty);

            if (ThrowDisabled)
            {
                throw new ImageUrlFetchException(
                    ImageUrlFetchReasons.Disabled,
                    "Pasted image URLs are not accepted by this deployment; upload the image file instead.");
            }

            if (RefusalReason is not null)
            {
                throw new ImageUrlFetchException(RefusalReason, "The image URL could not be used.");
            }

            // The declared header would have been `image/jpeg`; the sniffed truth is `image/png`.
            // The fetcher returns the sniffed one and the caller must store it (strategy §3.6).
            return Task.FromResult(new FetchedImage(TinyPng, "image/png"));
        }
    }

    /// <summary>
    /// A singleton over the scoped store registration. The adapter the endpoint receives is a
    /// per-scope forwarder to this instance, so the request the endpoint built is observable and
    /// the real adapter still writes the row.
    /// </summary>
    private sealed class CapturingAttachmentStore
    {
        public AttachmentStoreRequest? LastRequest { get; private set; }

        public int StoreCalls { get; private set; }

        public IAttachmentStore Forward(IAttachmentStore inner) => new Forwarder(this, inner);

        private sealed class Forwarder(CapturingAttachmentStore owner, IAttachmentStore inner) : IAttachmentStore
        {
            public string Provider => inner.Provider;

            public Task<MessageAttachment> StoreAsync(AttachmentStoreRequest request, CancellationToken ct)
            {
                owner.LastRequest = request;
                owner.StoreCalls++;
                return inner.StoreAsync(request, ct);
            }

            public Task<Stream?> OpenReadAsync(MessageAttachment attachment, CancellationToken ct)
                => inner.OpenReadAsync(attachment, ct);

            public Task DeleteAsync(MessageAttachment attachment, CancellationToken ct)
                => inner.DeleteAsync(attachment, ct);
        }
    }

    /// <summary>Captures every structured log record so the caller's Warning can be asserted.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogRecord> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Records);

        public void Dispose() { }

        private sealed class CapturingLogger(string categoryName, List<LogRecord> records) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => records.Add(new LogRecord(logLevel, categoryName, formatter(state, exception)));
        }
    }

    private sealed record LogRecord(LogLevel Level, string Category, string Message);

    private sealed record AttachmentResponse(
        Guid AttachmentId,
        string Url,
        string ContentType,
        string FileName,
        long SizeBytes,
        int? Width,
        int? Height);

    private sealed record MessageDto(
        Guid Id,
        Guid ConversationId,
        string AuthorKind,
        string? AgentKey,
        Guid? AuthorUserId,
        string Kind,
        System.Text.Json.JsonElement ContentBlocks,
        Guid? ReplyToMessageId,
        string Status,
        DateTime CreatedAt,
        Guid? ClientMessageId,
        Guid? WorkflowRunId);
}
