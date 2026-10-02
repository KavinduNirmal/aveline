using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// SE3110 gap E3 — the API half of the shared API↔agent-service contract.
/// </summary>
/// <remarks>
/// <para>
/// The gap (<c>09-integration.tex:105-131</c>, "The Seam Nobody Tests"): the .NET API and the
/// Python agent service each tested only their own half of the hop, so a renamed header, path,
/// request field, response field or pause-status literal would pass both suites and break the real
/// call.
/// </para>
/// <para>
/// Every assertion below is made against the single shared artefact,
/// <c>tests/contracts/api-agent-contract.json</c>. The field sets are not hard-coded here: they are
/// read from that file, so renaming a field on the .NET side fails this test, and changing the
/// contract deliberately means changing the file, the Python model and this test together. The
/// outbound request is asserted on the bytes that actually cross the seam (a capturing
/// <see cref="IAgentServiceClient"/> behind the real service code path), not on a restated shape.
/// </para>
/// </remarks>
public class ApiAgentSharedContractTests
{
    // =====================================================================================
    // The token header — the name both sides must use
    // =====================================================================================

    [Fact]
    public void TheInternalTokenHeader_MatchesTheSharedContract()
    {
        var fromContract = LoadContract().GetProperty("internalTokenHeader").GetString();

        Assert.Equal(InternalServiceAuthHandler.HeaderName, fromContract);
    }

    // =====================================================================================
    // The outbound query request — the bytes ConversationService puts on the wire
    // =====================================================================================

    [Fact]
    public async Task TheQueryRequest_CarriesExactlyTheContractFields()
    {
        var contract = LoadContract();
        var (path, sent) = await CapturedQueryRequestAsync();

        Assert.Equal(contract.GetProperty("paths").GetProperty("query").GetString(), path);
        // Equality, not a subset: `AgentQueryRequest` sets `extra="forbid"`, so an extra field is
        // a 422 on the wire just as loudly as a missing one.
        Assert.Equal(FieldSet(contract, "queryRequestFields"), sent);
    }

    // =====================================================================================
    // The outbound resume request — the bytes ApprovalService puts on the wire
    // =====================================================================================

    [Fact]
    public async Task TheResumeRequest_CarriesExactlyTheApiSendsFields()
    {
        var contract = LoadContract();
        var (path, sent) = await CapturedResumeRequestAsync();

        Assert.Equal(contract.GetProperty("paths").GetProperty("resume").GetString(), path);

        var modelFields = FieldSet(contract, "resumeRequestFields");
        var apiSends = FieldSet(contract, "apiSendsResumeFields");

        Assert.Equal(apiSends, sent);

        // `AgentResumeRequest` also forbids extra fields. The API sends a strict subset of the
        // model's optional fields (it never sends `customer_id`), so the compatibility rule is
        // exactly this containment - asserted here so the contract cannot drift out from under the
        // Python model.
        Assert.True(
            apiSends.IsSubsetOf(modelFields),
            $"apiSendsResumeFields must be a subset of resumeRequestFields; extra: {string.Join(", ", apiSends.Except(modelFields))}");
    }

    // =====================================================================================
    // The inbound response — what the API's deserialisation target actually reads
    // =====================================================================================

    [Fact]
    public void TheResponseTarget_ExposesExactlyTheContractFields()
    {
        var contract = LoadContract();

        var responseFields = JsonPropertyNames<AgentQueryResult>();
        var resultFields = JsonPropertyNames<AgentResultEnvelope>();

        Assert.Equal(FieldSet(contract, "apiDeserialisedResponseFields"), responseFields);
        Assert.Equal(FieldSet(contract, "apiDeserialisedResultFields"), resultFields);

        // The API reads a strict subset of what the agent emits (it deliberately does not read
        // prices or line items back - see AgentQueryResult's remarks). Both subsets are checked
        // against the agent's declared envelope so a contract edit that names a field the agent
        // never sends cannot pass here.
        Assert.True(
            responseFields.IsSubsetOf(FieldSet(contract, "queryResponseFields")),
            "apiDeserialisedResponseFields must be a subset of queryResponseFields");
        Assert.True(
            resultFields.IsSubsetOf(FieldSet(contract, "agentResultFields")),
            "apiDeserialisedResultFields must be a subset of agentResultFields");
    }

    // =====================================================================================
    // The pause literal — the one status value the API acts on
    // =====================================================================================

    [Fact]
    public void ThePauseStatusLiteral_MatchesTheSharedContract()
    {
        var pending = LoadContract().GetProperty("pendingApprovalStatus").GetString();

        var paused = JsonSerializer.Serialize(new { status = "ok", result = new { status = pending } });
        var answered = JsonSerializer.Serialize(new { status = "ok", result = new { status = "success" } });

        // Exercised through the real parse path the API uses when it decides whether to queue the
        // order (ConversationService.HandleAgentOutcomeAsync → AgentQueryResult.IsPaused).
        Assert.True(AgentQueryResult.IsPaused(paused));
        Assert.False(AgentQueryResult.IsPaused(answered));
    }

    // =====================================================================================
    // Helpers — the real code paths, and the shared artefact
    // =====================================================================================

    /// <summary>The query payload as <c>ConversationService.TriggerAgentAsync</c> serialises it.</summary>
    private static async Task<(string Path, SortedSet<string> Fields)> CapturedQueryRequestAsync()
    {
        var conversations = new Mock<IConversationRepository>();
        var messages = new Mock<IMessageRepository>();
        messages
            .Setup(r => r.ListLatestAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Message>());

        var agent = new CapturingAgentClient();
        var service = new ConversationService(
            conversations.Object,
            messages.Object,
            new Mock<ISignOffDecisionRepository>().Object,
            new Mock<IConversationReadStateRepository>().Object,
            new Mock<IMessageAttachmentRepository>().Object,
            new Mock<IAttachmentStore>().Object,
            agent,
            NullLogger<ConversationService>.Instance);

        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Guid.NewGuid(),
            Kind = ConversationKind.Salon,
            OwnerUserId = Guid.NewGuid(),
            ThreadId = "thread-contract-query",
            Status = ConversationStatus.Active,
        };

        await service.TriggerAgentAsync(conversation, "what is in stock?", cancellationToken: CancellationToken.None);

        Assert.NotNull(agent.LastBody);
        return (agent.LastPath!, TopLevelFieldNames(agent.LastBody!));
    }

    /// <summary>The resume payload as <c>ApprovalService.ProcessDecisionAsync</c> serialises it.</summary>
    private static async Task<(string Path, SortedSet<string> Fields)> CapturedResumeRequestAsync()
    {
        using var context = CreateInMemoryDbContext();
        var orgId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Contract Customer",
            Status = "pending_approval",
            Subtotal = 75000m,
            Discount = 0m,
            Total = 75000m,
            TotalCost = 65000m,
            Margin = 0.1333m,
            CreatedAt = DateTime.UtcNow,
        };
        await new OrderRepository(context).CreateAsync(order);

        var entry = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "high_value_order",
            Status = "pending",
            ThreadId = "thread-contract-resume",
            // An approval raised by the dashboard has no conversation, and is deliberately not
            // resumed; a conversation id is what makes this the real resume path.
            ConversationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };
        await new ApprovalRepository(context).AddAsync(entry);

        var agent = new CapturingAgentClient();
        var service = new ApprovalService(
            new ApprovalRepository(context), new OrderRepository(context), agent);

        await service.ProcessDecisionAsync(
            entry.Id, orgId, new ApprovalDecisionDto { Decision = "approve" }, null);

        Assert.NotNull(agent.LastBody);
        return (agent.LastPath!, TopLevelFieldNames(agent.LastBody!));
    }

    private static AppDbContext CreateInMemoryDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static SortedSet<string> TopLevelFieldNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = NewSet();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }

    /// <summary>
    /// The JSON names a type binds, read from the real <see cref="JsonPropertyNameAttribute"/>s and
    /// excluding <see cref="JsonIgnoreAttribute"/> members. This is the contract as the serializer
    /// sees it, not a restatement of it.
    /// </summary>
    private static SortedSet<string> JsonPropertyNames<T>()
    {
        var names = NewSet();
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            names.Add(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name);
        }

        return names;
    }

    private static SortedSet<string> FieldSet(JsonElement contract, string key)
    {
        var names = NewSet();
        foreach (var element in contract.GetProperty(key).EnumerateArray())
        {
            names.Add(element.GetString()!);
        }

        return names;
    }

    private static SortedSet<string> NewSet() => new(StringComparer.Ordinal);

    private static JsonElement LoadContract()
    {
        using var stream = File.OpenRead(FindContractFile());
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    /// <summary>
    /// Locate <c>tests/contracts/api-agent-contract.json</c> by walking up from the test assembly,
    /// the working directory and this source file, so no absolute path is baked in.
    /// </summary>
    private static string FindContractFile([CallerFilePath] string sourceFile = "")
    {
        var relative = Path.Combine("tests", "contracts", "api-agent-contract.json");

        foreach (var start in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     Path.GetDirectoryName(sourceFile) ?? string.Empty,
                 })
        {
            if (string.IsNullOrEmpty(start))
            {
                continue;
            }

            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException(
            $"Could not find '{relative}' above the test assembly, working directory or source file.");
    }

    private sealed class CapturingAgentClient : IAgentServiceClient
    {
        public string? LastPath { get; private set; }

        public string? LastBody { get; private set; }

        public async Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            LastPath = path;
            LastBody = await content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
