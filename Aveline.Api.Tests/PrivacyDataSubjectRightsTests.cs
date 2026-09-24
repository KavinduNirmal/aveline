using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Models;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 5 items 5.1, 5.2, 5.4, 5.5 and 5.6 (plan §7.2, §7.4, §7.5, §11 Phase 5): the two anonymous,
/// OTP-gated data-subject-rights routes.
///
/// <list type="bullet">
///   <item>Export returns the document <b>inline</b> (DR-4) with <c>Cache-Control: no-store</c> and a
///     download filename built from the organisation slug;</item>
///   <item>Export also ships a ZIP of CSVs plus a <c>MANIFEST.json</c> (never one flattened file);</item>
///   <item>Delete requires <c>confirm: "DELETE"</c> and a fresh OTP, executes the §7.3 scope, returns
///     per-table counts, and is idempotent per key;</item>
///   <item>the profile cache is explicitly invalidated (item 5.6).</item>
/// </list>
/// </summary>
public class PrivacyDataSubjectRightsTests : IAsyncLifetime
{
    private const string EncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";
    private const string Phone = "+94771234567";
    private const string OtherPhone = "+94779876543";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly StubWhatsAppService _whatsApp = new();
    private readonly RecordingCache _cache = new();

    private sealed class StubWhatsAppService : IWhatsAppService
    {
        public List<(string To, string Text)> Sent { get; } = [];

        public Task<WhatsAppTestResult> TestConnectionAsync(
            string accessToken, string phoneNumberId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppTestResult(IsValid: true));

        public Task<WhatsAppMediaResult> GetMediaAsync(
            string accessToken, string mediaId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppMediaResult(IsSuccess: false, Error: "not used"));

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((to, text));
            return Task.FromResult(new WhatsAppSendResult(
                IsSuccess: true, MessageId: $"wamid.DSR{Sent.Count}", HttpStatus: 200));
        }

        public Task<WhatsAppSendResult> SendTemplateAsync(
            string accessToken, string phoneNumberId, string to, string templateName,
            string languageCode, IReadOnlyList<object>? components,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopAgentClient : IAgentServiceClient
    {
        public Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public Task<HttpResponseMessage> GetAsync(
            string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class NoopBroadcaster : IMessageBroadcaster
    {
        public Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BroadcastConversationChangedAsync(ConversationTile tile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class AllowAllRateLimiter : IRateLimiter
    {
        public Task<bool> TryAllowAsync(
            string scopeKey, int limit, TimeSpan window, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    /// <summary>
    /// An <see cref="IDistributedCache"/> that actually stores (so the OTP flow works) and exposes a
    /// presence probe, which is how the cache-invalidation assertion reads.
    /// </summary>
    private sealed class RecordingCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new();

        public IReadOnlyCollection<string> Keys => _store.Keys.ToList();

        public byte[]? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _store[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:59999");
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Observability:AgentIsCritical", "false");
                builder.UseSetting("Credentials:EncryptionKey", EncryptionKey);
                builder.UseSetting("Privacy:LinkSigningKey", PrivacyKey);
                builder.UseSetting("App:BaseUrl", "https://app.aveline.lk");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IWhatsAppService>();
                    services.AddSingleton<IWhatsAppService>(_whatsApp);
                    services.RemoveAll<IAgentServiceClient>();
                    services.AddSingleton<IAgentServiceClient>(new NoopAgentClient());
                    services.RemoveAll<IMessageBroadcaster>();
                    services.AddSingleton<IMessageBroadcaster>(new NoopBroadcaster());
                    services.RemoveAll<IRateLimiter>();
                    services.AddSingleton<IRateLimiter>(new AllowAllRateLimiter());
                    services.RemoveAll<IDistributedCache>();
                    services.AddSingleton<IDistributedCache>(_cache);
                });
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    /// <summary>Reads a <c>counts</c> object into a comparable dictionary (key order is not a contract).</summary>
    private static Dictionary<string, int> Counts(JsonElement body)
        => body.GetProperty("counts")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetInt32());

    private static async Task<Guid> SeedOrganizationAsync(string suffix, string slug)
    {
        await using var context = CreateContext();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"dsr_owner_{suffix}",
            Email = $"dsr_owner_{suffix}@aveline.lk",
            FirstName = "Dsr",
            LastName = "Owner",
            Username = $"dsr_owner_{suffix}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = $"Emerald {suffix}",
            Slug = slug,
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = EncryptionKey,
            })
            .Build();
        var encryption = new CredentialEncryptionService(config);
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["accessToken"] = "wa-token",
            ["phoneNumberId"] = "111",
            ["appSecret"] = AppSecret,
            ["webhookVerifyToken"] = VerifyToken,
        });
        context.IntegrationCredentials.Add(new IntegrationCredential
        {
            OrganizationId = org.Id,
            IntegrationType = IntegrationType.WhatsApp,
            EncryptedValue = encryption.Encrypt(json, $"{org.Id}:{IntegrationType.WhatsApp}"),
            Status = IntegrationStatus.Connected,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return org.Id;
    }

    private static async Task<Guid> SeedCustomerAsync(
        Guid orgId, string phone, string? name = "Sarah Perera", DateTime? deletedAt = null)
    {
        await using var context = CreateContext();
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phone,
            FullName = name,
            Status = "new",
            DeletedAt = deletedAt,
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            ConsentStatus = ConsentStatuses.Granted,
            ConsentGrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        context.CustomerMemories.Add(new CustomerMemory
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            Content = $"Memory for {name}",
            Category = "fact",
        });
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private object SignedBody(Guid orgId, object extra)
    {
        var signer = _factory.Services.GetRequiredService<IPrivacyLinkSigner>();
        var properties = new Dictionary<string, object?>
        {
            ["organizationId"] = orgId,
            ["version"] = "1",
            ["signature"] = signer.Sign(orgId, "1"),
        };

        foreach (var property in extra.GetType().GetProperties())
        {
            properties[property.Name] = property.GetValue(extra);
        }

        return properties;
    }

    private async Task<(string Handle, string Code)> StartAndReadOtpAsync(Guid orgId, string phone)
    {
        var before = _whatsApp.Sent.Count;
        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/start", SignedBody(orgId, new { phoneNumber = phone, scope = "org" }));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.True(_whatsApp.Sent.Count > before, "no OTP message was sent");
        var text = _whatsApp.Sent[^1].Text;
        var match = Regex.Match(text, @"\b(\d{6})\b");
        Assert.True(match.Success, $"no six-digit code in the OTP body: {text}");

        await using var context = CreateContext();
        var handle = await context.InboundMessageLogs
            .Where(l => l.OrganizationId == orgId && l.Direction == "outbound")
            .OrderByDescending(l => l.ReceivedAt)
            .Select(l => l.ExternalId!)
            .FirstAsync();
        return (handle.Split(':')[^1], match.Groups[1].Value);
    }

    private object ExportBody(Guid orgId, string phone, string handle, string otp, string format = "json")
        => new { organizationId = orgId, phoneNumber = phone, handle, otp, format };

    private object DeleteBody(
        Guid orgId, string phone, string handle, string otp, string? confirm = "DELETE",
        string scope = "org", string? idempotencyKey = "dsr-key")
        => new
        {
            organizationId = orgId,
            phoneNumber = phone,
            handle,
            otp,
            confirm,
            scope,
            idempotencyKey,
        };

    // ----- 5.2 export -------------------------------------------------------------------------

    [Fact]
    public async Task AnExportWithAWrongCodeIsRejectedAndReturnsNoDocument()
    {
        var orgId = await SeedOrganizationAsync("exp-wrong", $"exp-wrong-{Guid.NewGuid():N}");
        await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/export", ExportBody(orgId, Phone, handle, wrong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("otp-invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExportWithACorrectCodeReturnsTheDocumentInlineWithNoStoreAndADownloadFilename()
    {
        var slug = $"emerald-export-{Guid.NewGuid():N}";
        var orgId = await SeedOrganizationAsync("exp-ok", slug);
        await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/export", ExportBody(orgId, Phone, handle, code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.Matches(
            $@"^aveline-data-{Regex.Escape(slug)}-\d{{8}}\.json$",
            disposition.FileName!.Trim('"'));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("generatedAtUtc", out _));
        Assert.Equal(orgId, body.GetProperty("subject").GetProperty("organizationId").GetGuid());
        Assert.True(body.GetProperty("memories").GetArrayLength() >= 1);
        Assert.True(body.GetProperty("counts").GetProperty("memories").GetInt32() >= 1);
    }

    [Fact]
    public async Task AnExportAsCsvReturnsAZipOfCsvFilesWithAManifest()
    {
        var orgId = await SeedOrganizationAsync("exp-csv", $"exp-csv-{Guid.NewGuid():N}");
        await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/export", ExportBody(orgId, Phone, handle, code, "csv"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".zip", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("MANIFEST.json", names);
        Assert.Contains("memories.csv", names);
        Assert.Contains("customer.csv", names);
    }

    // ----- 5.5 delete -------------------------------------------------------------------------

    [Fact]
    public async Task ADeleteWithoutTheConfirmationWordIsRejectedAndTouchesNothing()
    {
        var orgId = await SeedOrganizationAsync("del-confirm", $"del-confirm-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/delete", DeleteBody(orgId, Phone, handle, code, confirm: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("confirm-required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = CreateContext();
        Assert.True(await context.Customers.AnyAsync(c => c.Id == customerId));
    }

    [Fact]
    public async Task ADeleteWithAWrongCodeIsRejectedAndTouchesNothing()
    {
        var orgId = await SeedOrganizationAsync("del-wrong", $"del-wrong-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/delete", DeleteBody(orgId, Phone, handle, wrong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("otp-invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = CreateContext();
        Assert.True(await context.Customers.AnyAsync(c => c.Id == customerId));
    }

    [Fact]
    public async Task ADeleteWithAValidCodeRemovesTheCustomerAndReturnsPerTableCounts()
    {
        var orgId = await SeedOrganizationAsync("del-ok", $"del-ok-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/delete", DeleteBody(orgId, Phone, handle, code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("requestId").GetGuid());

        var counts = body.GetProperty("counts");
        // The §7.2 key names are part of the contract.
        foreach (var name in new[] { "memories", "preferences", "events", "interactions", "tags", "messages", "attachments", "consents" })
        {
            Assert.True(counts.TryGetProperty(name, out _), $"counts.{name} is missing");
        }

        Assert.True(counts.GetProperty("memories").GetInt32() >= 1);

        await using var context = CreateContext();
        Assert.False(await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customerId));
        Assert.False(await context.CustomerMemories.IgnoreQueryFilters().AnyAsync(m => m.CustomerId == customerId));

        // The audit row records the completion with counts only.
        Assert.True(await context.DataSubjectRequests.AnyAsync(
            r => r.OrganizationId == orgId
                 && r.Kind == DataSubjectRequestKinds.Delete
                 && r.Status == DataSubjectRequestStatuses.Completed
                 && r.ResultJson != null));
    }

    [Fact]
    public async Task TheSameIdempotencyKeyTwiceDeletesOnceAndReturnsTheStoredResult()
    {
        var orgId = await SeedOrganizationAsync("del-idem", $"del-idem-{Guid.NewGuid():N}");
        await SeedCustomerAsync(orgId, Phone);

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var body = DeleteBody(orgId, Phone, handle, code, idempotencyKey: "repeatable-key");

        var first = await _client.PostAsJsonAsync("/api/v1/privacy/data/delete", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(firstBody.GetProperty("replayed").GetBoolean());

        // The retry re-sends the *same* request: the code is now spent, so only the stored result
        // can answer it. That is the §7.5 idempotency contract working for a network retry.
        var second = await _client.PostAsJsonAsync("/api/v1/privacy/data/delete", body);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondBody.GetProperty("replayed").GetBoolean());

        Assert.Equal(firstBody.GetProperty("requestId").GetGuid(), secondBody.GetProperty("requestId").GetGuid());
        Assert.Equal(
            Counts(firstBody).OrderBy(pair => pair.Key),
            Counts(secondBody).OrderBy(pair => pair.Key));

        await using var context = CreateContext();
        Assert.Equal(
            1,
            await context.DataSubjectRequests.CountAsync(
                r => r.OrganizationId == orgId
                     && r.Kind == DataSubjectRequestKinds.Delete
                     && r.IdempotencyKey == "repeatable-key"));
    }

    [Fact]
    public async Task TheProfileCacheIsEmptyForTheDeletedCustomerAfterTheCall()
    {
        var orgId = await SeedOrganizationAsync("del-cache", $"del-cache-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, Phone);
        var profileKey = $"customer_profile:{customerId:D}";
        await _cache.SetStringAsync(profileKey, "{\"fullName\":\"Sarah Perera\"}");

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/delete", DeleteBody(orgId, Phone, handle, code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Null(await _cache.GetStringAsync(profileKey));
        Assert.DoesNotContain(_cache.Keys, key => key.Contains(customerId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ADataRequestForOrgALeavesOrgBsDataAloneEvenWhenThePhoneMatches()
    {
        var orgA = await SeedOrganizationAsync("iso-a", $"iso-a-{Guid.NewGuid():N}");
        var orgB = await SeedOrganizationAsync("iso-b", $"iso-b-{Guid.NewGuid():N}");
        var customerA = await SeedCustomerAsync(orgA, Phone, name: "Org A Sarah");
        var customerB = await SeedCustomerAsync(orgB, Phone, name: "Org B Sarah");

        var (handle, code) = await StartAndReadOtpAsync(orgA, Phone);

        var export = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/export", ExportBody(orgA, Phone, handle, code));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var document = await export.Content.ReadFromJsonAsync<JsonElement>();
        var text = document.GetRawText();
        Assert.DoesNotContain("Org B Sarah", text, StringComparison.Ordinal);
        Assert.Equal(orgA, document.GetProperty("subject").GetProperty("organizationId").GetGuid());

        var (handle2, code2) = await StartAndReadOtpAsync(orgA, Phone);
        var delete = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/delete", DeleteBody(orgA, Phone, handle2, code2));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        await using var context = CreateContext();
        Assert.False(await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customerA));
        Assert.True(await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customerB));
        Assert.True(await context.CustomerMemories.AnyAsync(m => m.CustomerId == customerB));
    }

    // ----- Conversation content redaction (Q-3) ------------------------------------------------

    [Fact]
    public async Task ADeleteRedactsTheCustomersOwnWordsButKeepsTheThreadSkeleton()
    {
        var orgId = await SeedOrganizationAsync("del-msg", $"del-msg-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, Phone);

        Guid conversationId;
        await using (var context = CreateContext())
        {
            var conversation = new Conversation
            {
                OrganizationId = orgId,
                CustomerId = customerId,
                ThreadId = Guid.NewGuid().ToString("N"),
                ExternalRef = Phone,
                Kind = ConversationKind.Salon,
            };
            context.Conversations.Add(conversation);
            context.Messages.Add(new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.System,
                Kind = MessageKind.ClientMessage,
                ContentBlocksJson = JsonSerializer.Serialize(new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "client_message",
                        ["from"] = Phone,
                        ["text"] = "Please keep my emerald silk secret",
                    },
                }),
            });
            await context.SaveChangesAsync();
            conversationId = conversation.Id;
        }

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/data/delete", DeleteBody(orgId, Phone, handle, code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var after = CreateContext();
        var kept = await after.Messages.SingleAsync(m => m.ConversationId == conversationId);
        Assert.DoesNotContain("emerald silk", kept.ContentBlocksJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, kept.ContentBlocksJson, StringComparison.Ordinal);

        // The thread skeleton survives, with its external reference cleared.
        var conversationAfter = await after.Conversations.SingleAsync(c => c.Id == conversationId);
        Assert.Null(conversationAfter.ExternalRef);
        Assert.Null(conversationAfter.CustomerId);

        var counts = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counts");
        Assert.True(counts.GetProperty("messages").GetInt32() >= 1);
    }

    // ----- model shape (item 5.4) --------------------------------------------------------------

    [Fact]
    public void DataSubjectRequestCarriesTheIdempotencyUniqueIndexAndTheCustomerIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(DataSubjectRequest));
        Assert.NotNull(entity);
        Assert.Equal("DataSubjectRequests", entity!.GetTableName());

        var unique = entity.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(
            new[] { "OrganizationId", "Kind", "IdempotencyKey" },
            unique.Properties.Select(p => p.Name).ToArray());
        Assert.Contains(entity.GetIndexes(), i => !i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CustomerId" }));

        var customerFk = entity.GetForeignKeys().Single(fk => fk.Properties.Any(p => p.Name == "CustomerId"));
        Assert.Equal(DeleteBehavior.SetNull, customerFk.DeleteBehavior);
        var orgFk = entity.GetForeignKeys().Single(fk => fk.Properties.Any(p => p.Name == "OrganizationId"));
        Assert.Equal(DeleteBehavior.Restrict, orgFk.DeleteBehavior);
    }
}
