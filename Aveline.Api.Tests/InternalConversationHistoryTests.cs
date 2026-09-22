using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration coverage for the internal conversation transcript read
/// (<c>GET /internal/conversations/{id}/messages</c>, ADR-023 W1.1).
/// </summary>
/// <remarks>
/// Runs against real PostgreSQL rather than the in-memory provider because the window is defined
/// by a database-side <c>ORDER BY ... TAKE</c>: the "newest N, returned oldest-first" contract is
/// precisely what a fake would assert about itself. Tenancy, ordering and the clamp are all
/// properties of the query, so they are exercised through the real one.
/// </remarks>
public class InternalConversationHistoryTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _orgId;
    private Guid _conversationId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var bootstrap = new AppDbContext(Options()))
        {
            await bootstrap.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
            await bootstrap.Database.MigrateAsync();
        }

        await SeedAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    /// <summary>
    /// Seeds one conversation with four messages in a known order: a customer question, an agent
    /// reply, an attachment-only message with no prose, and a staff note.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var context = new AppDbContext(Options());

        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"hist_{ownerId:N}", Email = "hist@aveline.lk",
            FirstName = "Hist", LastName = "Owner", Username = $"hist_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "History Boutique", Slug = $"hist-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);

        var conversation = new Conversation
        {
            OrganizationId = org.Id,
            ThreadId = $"thread-{ownerId:N}",
            Kind = ConversationKind.Salon,
        };
        context.Conversations.Add(conversation);

        var baseTime = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);
        context.Messages.AddRange(
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.System,
                Kind = MessageKind.ClientMessage,
                ContentBlocksJson =
                    """[{"type":"client_message","from":"94763475058","text":"Any pinkish gowns?"}]""",
                CreatedAt = baseTime,
            },
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.Agent,
                AuthorAgentKey = "ava",
                Kind = MessageKind.Note,
                ContentBlocksJson =
                    """[{"type":"text","text":"We have three soft pink gowns in stock."}]""",
                CreatedAt = baseTime.AddMinutes(1),
            },
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.User,
                Kind = MessageKind.Note,
                // No prose at all: must flatten to null rather than an invented description.
                ContentBlocksJson =
                    """[{"type":"attachment","url":"https://example.test/a.jpg","contentType":"image/jpeg"}]""",
                CreatedAt = baseTime.AddMinutes(2),
            },
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.User,
                Kind = MessageKind.Note,
                ContentBlocksJson = """[{"type":"text","text":"Hold the emerald one for her."}]""",
                CreatedAt = baseTime.AddMinutes(3),
            });

        await context.SaveChangesAsync();

        _orgId = org.Id;
        _conversationId = conversation.Id;
    }

    private async Task<HttpResponseMessage> GetHistoryAsync(
        Guid conversationId,
        Guid organizationId,
        int? limit = null,
        bool withToken = true)
    {
        var query = $"?organizationId={organizationId}";
        if (limit is not null)
        {
            query += $"&limit={limit}";
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/internal/conversations/{conversationId}/messages{query}");
        if (withToken)
        {
            request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonDocument.Parse(raw).RootElement.Clone();
        return (response.StatusCode, body);
    }

    [Fact]
    public async Task History_IsReturnedOldestFirstWithProseExtracted()
    {
        var (status, body) = await ReadAsync(await GetHistoryAsync(_conversationId, _orgId));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("conversationId").GetGuid().Should().Be(_conversationId);
        body.GetProperty("organizationId").GetGuid().Should().Be(_orgId);

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(4);

        // Oldest first: the customer's question leads, the staff note trails.
        items[0].GetProperty("text").GetString().Should().Be("Any pinkish gowns?");
        items[0].GetProperty("authorKind").GetString().Should().Be("System");
        items[1].GetProperty("text").GetString().Should().Be("We have three soft pink gowns in stock.");
        items[1].GetProperty("agentKey").GetString().Should().Be("ava");

        // The attachment-only turn has no prose and must stay null rather than inventing text.
        items[2].GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);

        items[3].GetProperty("text").GetString().Should().Be("Hold the emerald one for her.");
    }

    [Fact]
    public async Task History_ReturnsTheNewestTurnsWhenLimited()
    {
        // The window is the most recent turns, not the first page: a bounded read must keep what
        // just happened, which is what makes a reference resolvable.
        var (status, body) = await ReadAsync(await GetHistoryAsync(_conversationId, _orgId, limit: 2));

        status.Should().Be(HttpStatusCode.OK);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        // The two newest turns: the attachment-only message (no prose) and the staff note. Oldest
        // first within the window, so the null-prose turn leads.
        items[0].GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("text").GetString().Should().Be("Hold the emerald one for her.");
    }

    [Fact]
    public async Task History_IsScopedToTheOrganization()
    {
        // A conversation id from another tenant must be a 404, not a transcript.
        var (status, _) = await ReadAsync(await GetHistoryAsync(_conversationId, Guid.CreateVersion7()));

        status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_RequiresAnOrganizationId()
    {
        var (status, _) = await ReadAsync(await GetHistoryAsync(_conversationId, Guid.Empty));

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task History_RequiresTheInternalToken()
    {
        var (status, _) = await ReadAsync(await GetHistoryAsync(_conversationId, _orgId, withToken: false));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000)]
    public async Task History_TreatsNonsensicalLimitsAsBoundedRatherThanFailing(int limit)
    {
        // The window is a context-budget decision, so a bad value degrades to something bounded
        // (at least one turn) instead of a 400. The upper clamp is exercised by construction here
        // only as "no more than the conversation holds"; the ceiling itself is a service constant.
        var (status, body) = await ReadAsync(await GetHistoryAsync(_conversationId, _orgId, limit: limit));

        status.Should().Be(HttpStatusCode.OK);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items.Count.Should().BeLessThanOrEqualTo(Math.Max(limit, 1));
    }

    [Fact]
    public async Task History_IsEmptyRatherThanMissingForAConversationWithNoMessages()
    {
        Guid emptyConversationId;
        await using (var context = new AppDbContext(Options()))
        {
            var conversation = new Conversation
            {
                OrganizationId = _orgId,
                ThreadId = $"thread-empty-{Guid.CreateVersion7():N}",
            };
            context.Conversations.Add(conversation);
            await context.SaveChangesAsync();
            emptyConversationId = conversation.Id;
        }

        var (status, body) = await ReadAsync(await GetHistoryAsync(emptyConversationId, _orgId));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }
}
