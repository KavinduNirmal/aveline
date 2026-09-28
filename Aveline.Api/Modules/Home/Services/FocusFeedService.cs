using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Home.DTOs;
using Aveline.Api.Modules.Home.Models;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.VisualIntelligence.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Home.Services;

/// <summary>
/// The derived focus feed.
/// </summary>
/// <remarks>
/// The union of three existing facts, filtered by the caller's own dismissals:
/// <list type="bullet">
/// <item><c>wardrobe</c> — inventory at or below the reorder line;</item>
/// <item><c>patron</c> — customer events inside the preparation window;</item>
/// <item><c>commerce</c> — agent runs paused for a human decision, only for a
/// caller whose role holds <c>stats:view:agent</c>.</item>
/// </list>
/// <c>logistics</c> has no writer in the product yet, so it is reported
/// unavailable rather than counted as zero. The day is the organization's, from
/// <c>Organization.TimeZone</c>, never the device's.
/// </remarks>
public sealed class FocusFeedService : IFocusFeedService
{
    private readonly AppDbContext _context;
    private readonly IVisualService _visual;
    private readonly IConfiguration _configuration;

    public FocusFeedService(AppDbContext context, IVisualService visual, IConfiguration configuration)
    {
        _context = context;
        _visual = visual;
        _configuration = configuration;
    }

    public async Task<HomeFeedDto> GetFeedAsync(
        Guid organizationId,
        Guid userId,
        bool canReadAgentApprovals,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var timeZone = await ResolveTimeZoneAsync(organizationId, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);
        var localDate = localNow.Date;

        var items = new List<HomeFeedItemDto>();

        var wardrobeAvailable = await AddWardrobeAsync(organizationId, items, cancellationToken);
        var patronAvailable = await AddPatronAsync(organizationId, items, now, cancellationToken);
        var commerceAvailable = canReadAgentApprovals
            && await AddCommerceAsync(organizationId, items, cancellationToken);

        var visible = await FilterDismissedAsync(organizationId, userId, items, cancellationToken);

        // Server order: due first, then id. The deck is free to reorder locally.
        var ordered = visible
            .OrderBy(item => item.DueAtUtc is null)
            .ThenBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var byDomain = ordered
            .GroupBy(item => item.Domain)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var overdue = ordered.Count(item => item.DueAtUtc is not null && item.DueAtUtc < now);
        var counts = new HomeFeedCountsDto(ordered.Count, overdue, byDomain);

        return new HomeFeedDto(
            now,
            new HomeFeedWindowDto(localDate.ToString("yyyy-MM-dd"), timeZone.Id),
            new HomeFeedDataQualityDto(
                WardrobeAvailable: wardrobeAvailable,
                PatronAvailable: patronAvailable,
                CommerceAvailable: commerceAvailable,
                LogisticsAvailable: false),
            ordered,
            counts);
    }

    private async Task<bool> AddWardrobeAsync(
        Guid organizationId, List<HomeFeedItemDto> items, CancellationToken cancellationToken)
    {
        var threshold = _configuration.GetValue("Home:LowStockThreshold", 5);
        var lowStock = await _visual.GetLowStockInventoryAsync(organizationId, threshold, cancellationToken);

        foreach (var item in lowStock)
        {
            var title = $"Count in / reorder {item.ItemName}";
            var detail = item.Quantity <= 0
                ? $"Out of stock · {item.Category}"
                : $"{item.Quantity} left · reorder line {threshold}";

            items.Add(new HomeFeedItemDto(
                Id: $"wardrobe:{item.Id}",
                SourceKey: item.Id.ToString(),
                Domain: "wardrobe",
                Title: title,
                Detail: detail,
                DueAtUtc: null,
                TimeLabel: null,
                ActionLabel: "Sign Off",
                DoneMessage: "Signed off. The count is recorded.",
                ContentHash: Hash(title, detail),
                Caps: new HomeFeedCapsDto(CanComplete: true, CanAssign: false)));
        }

        return lowStock.Count > 0;
    }

    private async Task<bool> AddPatronAsync(
        Guid organizationId, List<HomeFeedItemDto> items, DateTime now, CancellationToken cancellationToken)
    {
        var windowDays = _configuration.GetValue("Home:PatronWindowDays", 7);
        var horizon = now.AddDays(windowDays);

        var events = await _context.CustomerEvents
            .AsNoTracking()
            .Include(customerEvent => customerEvent.Customer)
            .Where(customerEvent =>
                customerEvent.OrganizationId == organizationId
                && customerEvent.IsActive
                && customerEvent.EventDate >= now.AddDays(-1)
                && customerEvent.EventDate <= horizon)
            .OrderBy(customerEvent => customerEvent.EventDate)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var customerEvent in events)
        {
            var name = customerEvent.Customer?.FullName
                       ?? customerEvent.Customer?.PhoneNumber
                       ?? "a client";
            var title = $"Prepare for {name}'s {customerEvent.EventType}";
            var detail = string.IsNullOrWhiteSpace(customerEvent.Description)
                ? $"Coming up {customerEvent.EventDate:yyyy-MM-dd}"
                : customerEvent.Description!;

            items.Add(new HomeFeedItemDto(
                Id: $"patron:{customerEvent.Id}",
                SourceKey: customerEvent.Id.ToString(),
                Domain: "patron",
                Title: title,
                Detail: detail,
                DueAtUtc: DateTime.SpecifyKind(customerEvent.EventDate, DateTimeKind.Utc),
                TimeLabel: null,
                ActionLabel: "Mark ready",
                DoneMessage: "Marked ready.",
                ContentHash: Hash(title, detail),
                Caps: new HomeFeedCapsDto(CanComplete: true, CanAssign: false)));
        }

        return events.Count > 0;
    }

    private async Task<bool> AddCommerceAsync(
        Guid organizationId, List<HomeFeedItemDto> items, CancellationToken cancellationToken)
    {
        var paused = await _context.AgentWorkflowRuns
            .AsNoTracking()
            .Where(run => run.OrganizationId == organizationId
                          && run.Status == AgentRunStatus.PausedForApproval)
            .OrderBy(run => run.PausedAt ?? run.StartedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var run in paused)
        {
            var title = $"Sign off the agent's {run.WorkflowId}";
            var detail = "The agent paused for a human decision.";

            items.Add(new HomeFeedItemDto(
                Id: $"commerce:{run.Id}",
                SourceKey: run.Id.ToString(),
                Domain: "commerce",
                Title: title,
                Detail: detail,
                DueAtUtc: run.PausedAt,
                TimeLabel: null,
                ActionLabel: "Approve",
                DoneMessage: "Approved.",
                ContentHash: Hash(title, detail),
                Caps: new HomeFeedCapsDto(CanComplete: true, CanAssign: false)));
        }

        return paused.Count > 0;
    }

    private async Task<List<HomeFeedItemDto>> FilterDismissedAsync(
        Guid organizationId,
        Guid userId,
        List<HomeFeedItemDto> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var dismissals = await _context.FocusDismissals
            .AsNoTracking()
            .Where(dismissal => dismissal.OrganizationId == organizationId && dismissal.UserId == userId)
            .ToListAsync(cancellationToken);

        if (dismissals.Count == 0)
        {
            return items;
        }

        var bySource = dismissals.ToDictionary(
            dismissal => (dismissal.Domain, dismissal.SourceKey),
            dismissal => dismissal);

        return items
            .Where(item =>
                !bySource.TryGetValue((item.Domain, item.SourceKey), out var dismissal)
                || (dismissal.ContentHash is not null && dismissal.ContentHash != item.ContentHash))
            .ToList();
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var timeZoneId = await _context.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The canonical hash a dismissal is bound to.</summary>
    public static string Hash(string title, string detail) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{title}\n{detail}"))).ToLowerInvariant();
}
