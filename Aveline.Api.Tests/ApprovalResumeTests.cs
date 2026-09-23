using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-024, Decisions 3 and 5: what the API sends when an owner settles a paused deal.
/// </summary>
/// <remarks>
/// This is the seam the resume used to fail at, and every one of its three failures is pinned here:
/// it posted to <c>/agents/query</c> (a re-query, not a resume), it sent the dashboard's verbs
/// (<c>approve</c>) where the graph matches <c>approved</c>, and it sent no line items. The card also
/// carries an absolute discount where the agent's tools read a rate.
/// </remarks>
public class ApprovalResumeTests
{
    private sealed record Captured(string Path, string Body);

    private sealed class CapturingAgentClient : IAgentServiceClient
    {
        public Captured? Last { get; private set; }

        public Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            Last = new Captured(path, content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private static AppDbContext CreateInMemoryDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static async Task<(Guid OrgId, ApprovalQueueEntry Entry)> SeedAsync(
        AppDbContext context,
        string status = "pending",
        decimal subtotal = 75000m,
        decimal discount = 0m,
        Guid? conversationId = null)
    {
        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Resume Customer",
            Status = "pending_approval",
            Subtotal = subtotal,
            Discount = discount,
            Total = subtotal - discount,
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
            Status = status,
            ThreadId = "thread-resume-1",
            ConversationId = conversationId,
            CreatedAt = DateTime.UtcNow,
        };
        await new ApprovalRepository(context).AddAsync(entry);

        return (orgId, entry);
    }

    private static async Task<(ApprovalQueueResponseDto Result, Captured? Sent)> DecideAsync(
        string decision,
        decimal? revisedDiscount = null,
        bool withConversation = true,
        decimal subtotal = 75000m,
        decimal discount = 0m)
    {
        using var context = CreateInMemoryDbContext();
        var (orgId, entry) = await SeedAsync(
            context,
            subtotal: subtotal,
            discount: discount,
            conversationId: withConversation ? Guid.NewGuid() : null);

        var client = new CapturingAgentClient();
        var service = new ApprovalService(
            new ApprovalRepository(context), new OrderRepository(context), client);

        var result = await service.ProcessDecisionAsync(
            entry.Id, orgId, new ApprovalDecisionDto { Decision = decision, RevisedDiscount = revisedDiscount }, null);

        return (result, client.Last);
    }

    [Fact]
    public async Task AnApproval_ResumesTheThreadRatherThanReplayingTheRequest()
    {
        var (result, sent) = await DecideAsync("approve");

        Assert.Equal("approved", result.Status);
        Assert.NotNull(sent);
        // The old implementation posted to `/agents/query`, which re-ran the pipeline: a re-query,
        // not a resume.
        Assert.Equal("/agents/resume", sent!.Path);
        Assert.DoesNotContain("/agents/query", sent.Path);
    }

    [Theory]
    [InlineData("approve", "approved")]
    [InlineData("reject", "rejected")]
    public async Task TheDecision_CrossesTheSeamInTheGraphsVocabulary(string verb, string expected)
    {
        var (_, sent) = await DecideAsync(verb);

        Assert.NotNull(sent);
        using var body = JsonDocument.Parse(sent!.Body);
        Assert.Equal(expected, body.RootElement.GetProperty("decision").GetString());
        Assert.Equal("thread-resume-1", body.RootElement.GetProperty("thread_id").GetString());
    }

    [Fact]
    public async Task ARevision_CrossesTheSeamAsARateNotAnAmount()
    {
        // The dashboard says "LKR 7,500 off"; the agent's pricing tools read 0.10 as 10% off. Sending
        // the amount would be read as a rate and clamped to a full discount.
        var (result, sent) = await DecideAsync("revise", revisedDiscount: 7500m);

        Assert.Equal("revised", result.Status);
        Assert.NotNull(sent);
        using var body = JsonDocument.Parse(sent!.Body);
        Assert.Equal(0.1, body.RootElement.GetProperty("revised_discount").GetDouble(), precision: 4);
    }

    [Fact]
    public async Task AnOrderWithNoConversation_IsNotResumed()
    {
        // A dashboard order has a generated thread id that names no checkpoint, and no conversation.
        // Attempting a resume would 404 at best; the decision is complete without one.
        var (result, sent) = await DecideAsync("approve", withConversation: false);

        Assert.Equal("approved", result.Status);
        Assert.Null(sent);
    }

    [Fact]
    public async Task AnAlreadyDecidedApproval_IsRejectedRatherThanResumedTwice()
    {
        using var context = CreateInMemoryDbContext();
        var (orgId, entry) = await SeedAsync(context, status: "approved");

        var client = new CapturingAgentClient();
        var service = new ApprovalService(
            new ApprovalRepository(context), new OrderRepository(context), client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessDecisionAsync(
            entry.Id, orgId, new ApprovalDecisionDto { Decision = "approve" }, null));

        Assert.Null(client.Last);
    }
}
