using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Records a counter interaction and keeps the customer's visit counters real.
/// </summary>
/// <remarks>
/// Before this service, <c>VisitCount</c>, <c>LastVisitAt</c> and
/// <c>TotalSpent</c> had **no writer** outside migrations, so the loyalty rule
/// read fields that never moved and every client stayed <c>new</c>. The counter
/// increment is atomic on a relational provider (a read-modify-write loses one of
/// two concurrent visits at the counter).
/// </remarks>
public interface ICustomerVisitService
{
    /// <summary>
    /// Records the interaction, or returns <c>null</c> when the customer is not in
    /// this organization.
    /// </summary>
    Task<VisitReceiptDto?> RecordAsync(
        Guid organizationId,
        Guid customerId,
        Guid actorUserId,
        RecordCustomerInteractionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerVisitService : ICustomerVisitService
{
    private static readonly HashSet<string> KnownChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "in_person", "phone", "whatsapp", "instagram",
    };

    private readonly AppDbContext _context;

    public CustomerVisitService(AppDbContext context) => _context = context;

    public async Task<VisitReceiptDto?> RecordAsync(
        Guid organizationId,
        Guid customerId,
        Guid actorUserId,
        RecordCustomerInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(
            candidate => candidate.Id == customerId
                         && candidate.OrganizationId == organizationId
                         && candidate.DeletedAt == null,
            cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var channel = KnownChannels.Contains(request.Channel)
            ? request.Channel.ToLowerInvariant()
            : "phone";
        var direction = string.Equals(request.Direction, "outbound", StringComparison.OrdinalIgnoreCase)
            ? "outbound"
            : "inbound";
        var occurredAt = request.OccurredAtUtc == default ? DateTime.UtcNow : request.OccurredAtUtc;

        // A visit counts only for an inbound in-person interaction: a WhatsApp
        // reply is an interaction, not someone standing at the counter.
        var countedAsVisit = channel == "in_person" && direction == "inbound";
        var purchaseTotal = request.PurchaseTotal is > 0m ? request.PurchaseTotal.Value : 0m;

        var interaction = new CustomerInteraction
        {
            OrganizationId = organizationId,
            CustomerId = customerId,
            Channel = channel,
            Direction = direction,
            MessageContent = request.Note,
            StaffMemberId = actorUserId == Guid.Empty ? null : actorUserId,
            CreatedAt = occurredAt,
        };
        _context.CustomerInteractions.Add(interaction);
        await _context.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var visitCount = customer.VisitCount + (countedAsVisit ? 1 : 0);
        var totalSpent = customer.TotalSpent + purchaseTotal;
        var lastVisitAt = countedAsVisit ? occurredAt : customer.LastVisitAt;
        var tier = CustomerLoyaltyService.RecommendStatus(totalSpent, visitCount, lastVisitAt, now);

        if (_context.Database.IsRelational())
        {
            // One statement, so two concurrent visits cannot lose an increment.
            await _context.Customers
                .Where(candidate => candidate.Id == customerId
                                    && candidate.OrganizationId == organizationId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            candidate => candidate.VisitCount,
                            candidate => candidate.VisitCount + (countedAsVisit ? 1 : 0))
                        .SetProperty(
                            candidate => candidate.TotalSpent,
                            candidate => candidate.TotalSpent + purchaseTotal)
                        .SetProperty(
                            candidate => candidate.LastVisitAt,
                            candidate => countedAsVisit ? occurredAt : candidate.LastVisitAt)
                        .SetProperty(candidate => candidate.Status, tier)
                        .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken);
        }
        else
        {
            // The in-memory provider does not support ExecuteUpdate; tests use this
            // path, and the SQL above is what proves the concurrency guarantee.
            customer.VisitCount = visitCount;
            customer.TotalSpent = totalSpent;
            customer.LastVisitAt = lastVisitAt;
            customer.Status = tier;
            customer.UpdatedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new VisitReceiptDto(
            interaction.Id,
            customerId,
            occurredAt,
            channel,
            countedAsVisit,
            visitCount,
            lastVisitAt,
            tier,
            BlossomsCharged: 0m);
    }
}
