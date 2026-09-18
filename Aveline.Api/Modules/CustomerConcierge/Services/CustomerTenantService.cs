using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// The tenant-facing customer surface: the book the log-visit picker reads, the
/// highlights Home's client row shows, and walk-in creation.
/// </summary>
public interface ICustomerTenantService
{
    Task<CustomerBookResponseDto> GetBookAsync(
        Guid organizationId,
        string? search,
        string? level,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CustomerHighlightsResponseDto> GetHighlightsAsync(
        Guid organizationId,
        int limit,
        DateTime? activitySince,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a counter walk-in, or reports the existing client it duplicates.
    /// </summary>
    Task<CustomerCreatedDto> CreateWalkInAsync(
        Guid organizationId,
        CreateWalkInCustomerRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerTenantService : ICustomerTenantService
{
    private const string NicknameKey = "nickname";

    private readonly AppDbContext _context;

    public CustomerTenantService(AppDbContext context) => _context = context;

    public async Task<CustomerBookResponseDto> GetBookAsync(
        Guid organizationId,
        string? search,
        string? level,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var query = _context.Customers
            .AsNoTracking()
            .Include(customer => customer.Preferences)
            .Where(customer => customer.OrganizationId == organizationId
                               && customer.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(level))
        {
            query = query.Where(customer => customer.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(customer =>
                (customer.FullName != null && customer.FullName.ToLower().Contains(term))
                || customer.PhoneNumber.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var customers = await query
            .OrderBy(customer => customer.FullName ?? customer.PhoneNumber)
            .ThenBy(customer => customer.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CustomerBookResponseDto(
            customers.Select(ToBookItem).ToList(),
            total,
            page,
            pageSize);
    }

    public async Task<CustomerHighlightsResponseDto> GetHighlightsAsync(
        Guid organizationId,
        int limit,
        DateTime? activitySince,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var since = activitySince ?? DateTime.UtcNow.AddDays(-14);

        var customers = await _context.Customers
            .AsNoTracking()
            .Include(customer => customer.Preferences)
            .Where(customer => customer.OrganizationId == organizationId
                               && customer.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
        {
            return new CustomerHighlightsResponseDto([]);
        }

        var customerIds = customers.Select(customer => customer.Id).ToList();

        // The only real event log the schema has. One org-wide read, newest first,
        // so the row's sentence is generated from an event rather than authored.
        var interactions = await _context.CustomerInteractions
            .AsNoTracking()
            .Where(interaction => interaction.OrganizationId == organizationId
                                  && customerIds.Contains(interaction.CustomerId))
            .OrderByDescending(interaction => interaction.CreatedAt)
            .ToListAsync(cancellationToken);

        var newest = interactions
            .GroupBy(interaction => interaction.CustomerId)
            .ToDictionary(group => group.Key, group => group.First());

        var highlights = new List<CustomerHighlightDto>();
        foreach (var customer in customers)
        {
            newest.TryGetValue(customer.Id, out var interaction);
            var lastActivity = interaction?.CreatedAt ?? customer.LastVisitAt;
            if (lastActivity is null || lastActivity < since)
            {
                continue;
            }

            highlights.Add(new CustomerHighlightDto(
                customer.Id,
                DisplayName(customer),
                customer.Level,
                Describe(interaction),
                lastActivity));
        }

        var ordered = highlights
            .OrderByDescending(highlight => highlight.LastActivityAtUtc)
            .Take(limit)
            .ToList();

        return new CustomerHighlightsResponseDto(ordered);
    }

    public async Task<CustomerCreatedDto> CreateWalkInAsync(
        Guid organizationId,
        CreateWalkInCustomerRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var name = (request.FullName ?? string.Empty).Trim();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? string.Empty
            : request.PhoneNumber.Trim();

        // A name is enough to start a profile, so the walk-in de-duplicates on the
        // name rather than on a phone number the counter may not have collected.
        var existing = name.Length > 0
            ? await _context.Customers.FirstOrDefaultAsync(
                customer => customer.OrganizationId == organizationId
                            && customer.DeletedAt == null
                            && customer.FullName != null
                            && customer.FullName.ToLower() == name.ToLower(),
                cancellationToken)
            : null;

        if (existing is not null)
        {
            return new CustomerCreatedDto(
                existing.Id,
                existing.FullName,
                existing.Level,
                existing.Status,
                "pending",
                existing.CreatedAt,
                existing.Id);
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            OrganizationId = organizationId,
            FullName = name,
            PhoneNumber = phone,
            Status = "new",
            Level = null,
            CreatedBy = actorUserId == Guid.Empty ? null : actorUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.Customers.Add(customer);

        if (!string.IsNullOrWhiteSpace(request.Nickname))
        {
            _context.CustomerPreferences.Add(new CustomerPreference
            {
                OrganizationId = organizationId,
                CustomerId = customer.Id,
                PreferenceKey = NicknameKey,
                PreferenceValue = request.Nickname!.Trim(),
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new CustomerCreatedDto(
            customer.Id,
            customer.FullName,
            customer.Level,
            customer.Status,
            "pending",
            customer.CreatedAt,
            null);
    }

    private static CustomerBookItemDto ToBookItem(Customer customer) => new(
        customer.Id,
        customer.FullName,
        customer.Preferences.FirstOrDefault(preference => preference.PreferenceKey == NicknameKey)?.PreferenceValue,
        customer.Level,
        customer.Status,
        customer.PhoneNumber,
        customer.LastVisitAt,
        customer.VisitCount,
        customer.TotalSpent);

    private static string DisplayName(Customer customer) =>
        FirstPresent(customer.FullName)
        ?? FirstPresent(customer.Preferences.FirstOrDefault(p => p.PreferenceKey == NicknameKey)?.PreferenceValue)
        ?? FirstPresent(customer.PhoneNumber)
        ?? "A client";

    private static string? FirstPresent(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// One honest sentence from a real event, in the associate's words.
    /// </summary>
    private static string Describe(CustomerInteraction? interaction)
    {
        if (interaction is null)
        {
            return "Visited the boutique.";
        }

        var channel = (interaction.Channel ?? string.Empty).ToLowerInvariant();
        var when = Relative(interaction.CreatedAt);
        return channel switch
        {
            "in_person" => $"Visited the boutique {when}.",
            "whatsapp" => $"Messaged on WhatsApp {when}.",
            "instagram" => $"Messaged on Instagram {when}.",
            "phone" => $"Called the boutique {when}.",
            _ => $"Was in touch {when}.",
        };
    }

    private static string Relative(DateTime at)
    {
        var days = (DateTime.UtcNow.Date - at.ToUniversalTime().Date).Days;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            _ => $"{days} days ago",
        };
    }
}
