using System.Text.Json;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Models;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 5 item 5.3, Postgres-backed (plan §7.3, §11 Phase 5, §12.2). The three properties that only
/// a real Postgres can prove:
///
/// <list type="number">
///   <item>the pgvector <c>embedding vector(1536)</c> column is <b>not in the EF model</b>, so the
///     only way to know it is gone is to ask the database — this asserts zero non-null embeddings
///     for the erased customer (R-7);</item>
///   <item>the soft-delete query filters on <c>Customers</c> and <c>CustomerMemory</c> hide rows
///     that still hold PII, so the erasure must load with <c>IgnoreQueryFilters()</c> and the test
///     must prove zero rows remain <b>including soft-deleted ones</b> (R-16);</item>
///   <item>there is no EF global tenant filter, so a same-phone customer in another organization
///     must be untouched (R-17).</item>
/// </list>
/// </summary>
public class PrivacyErasurePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_privacy_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private AppDbContext? _context;
    private readonly ITestOutputHelper _output;
    private readonly ICustomerCacheInvalidator _caches = new NoopCacheInvalidator();

    public PrivacyErasurePostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        _context = new AppDbContext(options);

        await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class NoopCacheInvalidator : ICustomerCacheInvalidator
    {
        public Task<int> InvalidateAsync(
            Guid organizationId, Guid customerId, string phoneE164,
            string? fullName = null, string? email = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private ErasureService CreateService()
        => new(
            _context!,
            new InMemoryDistributedJobLock(),
            _caches,
            NullAuditService.Instance,
            TimeProvider.System,
            NullLogger<ErasureService>.Instance);

    private static float[] Embedding(int axis)
    {
        var vector = new float[1536];
        vector[axis] = 1f;
        return vector;
    }

    private async Task<Guid> SeedOrganizationAsync(string slug)
    {
        var user = new User
        {
            ClerkId = $"user_{Guid.NewGuid():N}",
            FirstName = "Test",
            LastName = "Owner",
            Email = "owner@aveline.test",
            Username = $"owner_{Guid.NewGuid():N}",
            OrganizationId = "org_legacy",
            PhoneNumber = "+94770000000",
            UserRole = "owner",
            OrganizationRole = "org:owner",
        };
        _context!.Users.Add(user);

        var org = new Organization
        {
            Name = "Test Boutique",
            Slug = slug,
            OwnerUserId = user.Id,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private async Task<Guid> SeedCustomerAsync(
        Guid orgId, string phone, string name, DateTime? deletedAt = null)
    {
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phone,
            FullName = name,
            DeletedAt = deletedAt,
        };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<Guid> SeedMemoryAsync(Guid orgId, Guid customerId, string content, int axis)
    {
        var repository = new CustomerMemoryRepository(_context!);
        var memory = await repository.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId,
            CustomerId = customerId,
            Content = content,
            Category = "fact",
        });
        await repository.UpdateEmbeddingAsync(orgId, memory.Id, Embedding(axis));
        return memory.Id;
    }

    private static ErasureRequest Request(Guid orgId, string phone, string key)
        => new(orgId, phone, ErasureScope.Org, key, ConsentActor.System, "customer_otp_erasure");

    [Fact]
    public async Task ErasureDeletesThePgvectorEmbeddingAndTheSoftDeletedCustomerRow()
    {
        var orgId = await SeedOrganizationAsync($"erase-embed-{Guid.NewGuid():N}");
        // The whole point of R-16: the row the erasure must remove is hidden by the global
        // `DeletedAt == null` filter, so an ordinary EF query would report success and leave PII.
        var customerId = await SeedCustomerAsync(orgId, "+94771234567", "Sarah Perera", deletedAt: DateTime.UtcNow);
        await SeedMemoryAsync(orgId, customerId, "Sarah prefers emerald silk", axis: 0);

        var result = await CreateService().EraseAsync(Request(orgId, "+94771234567", "embed-key"));

        Assert.False(result.Replayed);
        Assert.True(result.Counts["memories"] >= 1);

        // Zero Customers rows, including soft-deleted ones.
        Assert.Equal(
            0,
            await _context!.Customers.IgnoreQueryFilters()
                .CountAsync(c => c.OrganizationId == orgId && c.PhoneNumber == "+94771234567"));

        // The embedding column is outside the EF model, so ask Postgres directly.
        var embeddings = await _context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM \"CustomerMemory\" WHERE \"CustomerId\" = {0} AND embedding IS NOT NULL",
                customerId)
            .SingleAsync();
        Assert.Equal(0, embeddings);

        Assert.Equal(
            0,
            await _context.CustomerMemories.IgnoreQueryFilters().CountAsync(m => m.CustomerId == customerId));

        // The tombstone survives (Q-4) and the request record is retained.
        Assert.True(await _context.PrivacyErasureTombstones.AnyAsync(
            t => t.OrganizationId == orgId && t.Status == ConsentStatuses.Revoked));
        Assert.True(await _context.DataSubjectRequests.AnyAsync(
            r => r.Id == result.RequestId && r.Status == DataSubjectRequestStatuses.Completed && r.ResultJson != null));
    }

    [Fact]
    public async Task ErasureAnonymisesSourcingRequestsAndRetainsTheNulledInboundLogRow()
    {
        var orgId = await SeedOrganizationAsync($"erase-anon-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, "+94771234567", "Sarah Perera");

        var sourcing = new SourcingRequest
        {
            OrgId = orgId,
            CustomerId = customerId,
            ItemDescription = "Silk saree for the wedding",
            Status = "pending",
        };
        _context!.SourcingRequests.Add(sourcing);
        _context.InboundMessageLogs.Add(new InboundMessageLog
        {
            OrganizationId = orgId,
            Channel = "whatsapp",
            Direction = "inbound",
            ExternalId = "wamid.PRIVACY.1",
            From = "+94771234567",
            To = "+94770000000",
            Content = "stop messaging me",
            ReceivedAt = DateTime.UtcNow,
        });
        var conversation = new Conversation
        {
            OrganizationId = orgId,
            CustomerId = customerId,
            ThreadId = Guid.NewGuid().ToString("N"),
            ExternalRef = "+94771234567",
            Kind = ConversationKind.Salon,
        };
        _context.Conversations.Add(conversation);
        _context.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.System,
            Kind = MessageKind.ClientMessage,
            ContentBlocksJson = JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "client_message",
                    ["from"] = "+94771234567",
                    ["text"] = "Please forget my wedding plans",
                },
            }),
        });
        await _context.SaveChangesAsync();

        await CreateService().EraseAsync(Request(orgId, "+94771234567", "anon-key"));

        _context.ChangeTracker.Clear();

        var sourcingAfter = await _context.SourcingRequests.SingleAsync(s => s.Id == sourcing.Id);
        Assert.Null(sourcingAfter.CustomerId);
        Assert.Equal("Silk saree for the wedding", sourcingAfter.ItemDescription);

        // DR-3: the log proves the opt-out was received, so the row survives without its content.
        var logAfter = await _context.InboundMessageLogs.SingleAsync(l => l.ExternalId == "wamid.PRIVACY.1");
        Assert.Null(logAfter.From);
        Assert.Null(logAfter.Content);
        Assert.Equal("+94770000000", logAfter.To);

        var conversationAfter = await _context.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Null(conversationAfter.ExternalRef);
        Assert.Null(conversationAfter.CustomerId);

        var messageAfter = await _context.Messages.SingleAsync(m => m.ConversationId == conversation.Id);
        Assert.DoesNotContain("wedding plans", messageAfter.ContentBlocksJson, StringComparison.Ordinal);
        Assert.DoesNotContain("+94771234567", messageAfter.ContentBlocksJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErasingOneOrganisationLeavesTheSamePhoneInAnotherOrganisationUntouched()
    {
        var orgA = await SeedOrganizationAsync($"erase-a-{Guid.NewGuid():N}");
        var orgB = await SeedOrganizationAsync($"erase-b-{Guid.NewGuid():N}");
        var customerA = await SeedCustomerAsync(orgA, "+94771234567", "Org A Sarah");
        var customerB = await SeedCustomerAsync(orgB, "+94771234567", "Org B Sarah");
        await SeedMemoryAsync(orgA, customerA, "Org A memory", axis: 0);
        await SeedMemoryAsync(orgB, customerB, "Org B memory", axis: 1);

        await CreateService().EraseAsync(Request(orgA, "+94771234567", "iso-key"));

        _context!.ChangeTracker.Clear();
        Assert.Equal(
            0,
            await _context.Customers.IgnoreQueryFilters()
                .CountAsync(c => c.OrganizationId == orgA && c.PhoneNumber == "+94771234567"));
        Assert.Equal(
            1,
            await _context.Customers.IgnoreQueryFilters()
                .CountAsync(c => c.OrganizationId == orgB && c.PhoneNumber == "+94771234567"));
        Assert.True(await _context.CustomerMemories.IgnoreQueryFilters().AnyAsync(m => m.CustomerId == customerB));

        // The tombstone is scoped to the organisation that erased, not the phone globally.
        Assert.True(await _context.PrivacyErasureTombstones.AnyAsync(t => t.OrganizationId == orgA));
        Assert.False(await _context.PrivacyErasureTombstones.AnyAsync(t => t.OrganizationId == orgB));
    }

    [Fact]
    public async Task TheSameIdempotencyKeyTwiceReturnsTheStoredResultAndDeletesOnce()
    {
        var orgId = await SeedOrganizationAsync($"erase-idem-{Guid.NewGuid():N}");
        var customerId = await SeedCustomerAsync(orgId, "+94771234567", "Sarah Perera");
        await SeedMemoryAsync(orgId, customerId, "Sarah prefers emerald silk", axis: 0);

        var service = CreateService();
        var first = await service.EraseAsync(Request(orgId, "+94771234567", "stable-key"));
        var second = await service.EraseAsync(Request(orgId, "+94771234567", "stable-key"));

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.RequestId, second.RequestId);
        // `jsonb` canonicalises key order and whitespace, so the stored text is compared
        // semantically: the same keys with the same counts.
        Assert.Equal(
            first.Counts.OrderBy(pair => pair.Key),
            second.Counts.OrderBy(pair => pair.Key));

        _context!.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await _context.DataSubjectRequests.CountAsync(
                r => r.OrganizationId == orgId
                     && r.Kind == DataSubjectRequestKinds.Delete
                     && r.IdempotencyKey == "stable-key"));

        // The stored ResultJson is the first result, not a re-derivation (counts only, no content).
        var stored = await _context.DataSubjectRequests.SingleAsync(
            r => r.OrganizationId == orgId && r.IdempotencyKey == "stable-key");
        var storedCounts = JsonSerializer.Deserialize<Dictionary<string, int>>(stored.ResultJson!);
        Assert.Equal(
            first.Counts.OrderBy(pair => pair.Key),
            storedCounts!.OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task AnExportForOneOrganisationContainsNoRowsFromAnotherWithTheSamePhone()
    {
        var orgA = await SeedOrganizationAsync($"export-a-{Guid.NewGuid():N}");
        var orgB = await SeedOrganizationAsync($"export-b-{Guid.NewGuid():N}");
        var customerA = await SeedCustomerAsync(orgA, "+94771234567", "Org A Sarah");
        var customerB = await SeedCustomerAsync(orgB, "+94771234567", "Org B Sarah");
        await SeedMemoryAsync(orgA, customerA, "Org A secret memory", axis: 0);
        await SeedMemoryAsync(orgB, customerB, "Org B secret memory", axis: 1);

        var export = new DataSubjectExportService(_context!, TimeProvider.System);
        var document = await export.BuildAsync(orgA, "+94771234567");

        Assert.NotNull(document);
        var text = document!.Document.GetRawText();
        Assert.DoesNotContain("Org B secret memory", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Org B Sarah", text, StringComparison.Ordinal);
        Assert.Contains("Org A secret memory", text, StringComparison.Ordinal);
        Assert.Equal(orgA, document.Document.GetProperty("subject").GetProperty("organizationId").GetGuid());
        Assert.True(document.Counts["memories"] >= 1);
    }

    [Fact]
    public async Task ErasureFailsLoudlyWhenTheLeaseIsAlreadyHeld()
    {
        var orgId = await SeedOrganizationAsync($"erase-lease-{Guid.NewGuid():N}");
        await SeedCustomerAsync(orgId, "+94771234567", "Sarah Perera");

        var jobLock = new InMemoryDistributedJobLock();
        var service = new ErasureService(
            _context!, jobLock, _caches, NullAuditService.Instance,
            TimeProvider.System, NullLogger<ErasureService>.Instance);

        var lease = await jobLock.TryAcquireAsync($"privacy:erasure:{orgId:D}:{PhoneFingerprint.Of("+94771234567")}");
        Assert.NotNull(lease);

        await Assert.ThrowsAsync<ErasureInProgressException>(
            () => service.EraseAsync(Request(orgId, "+94771234567", "lease-key")));

        await lease!.DisposeAsync();
    }
}
