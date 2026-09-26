using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Common;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.Conversations.Services;
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
    /// The book at a glance: its size, and the few clients active most recently.
    /// </summary>
    Task<CustomerBookSummaryDto> GetBookSummaryAsync(
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

    /// <summary>
    /// One client's record, or <c>null</c> when the id is not in this organization (or is deleted).
    /// The two cases are deliberately indistinguishable to the caller.
    /// </summary>
    Task<TenantCustomerDetailDto?> GetDetailAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the writable subset of a client's record. Returns <c>null</c> when the client is not
    /// in this organization, and throws <see cref="CustomerPhoneConflictException"/> when the new
    /// phone number is already another client's identity key in the same organization.
    /// </summary>
    Task<TenantCustomerDetailDto?> UpdateAsync(
        Guid organizationId,
        Guid customerId,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a client. Returns <c>true</c> when the client is now (or already was) deleted,
    /// and <c>null</c> when it is not in this organization. Idempotent by design: the deletion is the
    /// end state, and the soft-delete filter means the caller cannot tell a second delete from a
    /// first anyway.
    /// </summary>
    Task<bool?> DeleteAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>The client's interaction history, newest first.</summary>
    Task<CustomerInteractionPageDto?> GetInteractionsAsync(
        Guid organizationId,
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The new phone number belongs to another client in the same organization. The unique
/// <c>(OrganizationId, PhoneNumber)</c> index is the constraint this makes legible: without it a
/// collision surfaced as a 500.
/// </summary>
public sealed class CustomerPhoneConflictException(string phoneNumber)
    : Exception($"The phone number '{phoneNumber}' is already in use by another client in this organization.")
{
    public string PhoneNumber { get; } = phoneNumber;
}

/// <summary>
/// The client has orders that are not in a terminal state. This is a **business guard, not
/// referential integrity**: <c>Order.CustomerId</c> is a bare <c>Guid</c> with no foreign key or
/// navigation, and <c>Order.CustomerName</c> is denormalised, so nothing is actually orphaned and a
/// deleted client's orders stay readable. The guard exists so a sale-to-delete flow cannot remove a
/// client mid-transaction.
/// </summary>
public sealed class CustomerHasOpenOrdersException(int openOrders)
    : Exception($"The client has {openOrders} order(s) that are not in a terminal state.")
{
    public int OpenOrders { get; } = openOrders;
}

public sealed class CustomerTenantService : ICustomerTenantService
{
    private const string NicknameKey = "nickname";

    /// <summary>
    /// How far back a client must have acted to appear as a highlight. One definition, because
    /// Home's client row and the agent's book summary both order by it.
    /// </summary>
    private static readonly TimeSpan HighlightActivityWindow = TimeSpan.FromDays(14);

    private readonly AppDbContext _context;
    private readonly IConversationService _conversations;

    public CustomerTenantService(AppDbContext context, IConversationService conversations)
    {
        _context = context;
        _conversations = conversations;
    }

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

    public async Task<CustomerBookSummaryDto> GetBookSummaryAsync(
        Guid organizationId,
        int limit,
        DateTime? activitySince,
        CancellationToken cancellationToken = default)
    {
        // Resolved once here and handed to the highlights read, so the window has one definition
        // even though two surfaces now ask for it.
        var since = activitySince ?? DateTime.UtcNow - HighlightActivityWindow;

        // Reused rather than reimplemented: "which clients count as recently active, and what is
        // the sentence describing them" is one rule, and Home already reads it from here.
        var highlights = await GetHighlightsAsync(organizationId, limit, since, cancellationToken);

        var total = await _context.Customers
            .AsNoTracking()
            .CountAsync(
                customer => customer.OrganizationId == organizationId && customer.DeletedAt == null,
                cancellationToken);

        return new CustomerBookSummaryDto(total, since, highlights.Items);
    }

    public async Task<CustomerHighlightsResponseDto> GetHighlightsAsync(
        Guid organizationId,
        int limit,
        DateTime? activitySince,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var since = activitySince ?? DateTime.UtcNow - HighlightActivityWindow;

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

        // The unique `(OrganizationId, PhoneNumber)` index is the real constraint, but on a
        // relational provider a collision surfaced as an unmapped `DbUpdateException` and therefore
        // a 500. Checking first turns it into a typed 409 on every provider, so the behaviour the
        // client sees does not depend on the storage engine.
        if (phone.Length > 0)
        {
            var normalised = PhoneNormalizer.ToE164(phone) ?? phone;
            var phoneTaken = await _context.Customers
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.OrganizationId == organizationId
                                 && candidate.DeletedAt == null
                                 && candidate.PhoneNumber == normalised,
                    cancellationToken);
            if (phoneTaken)
            {
                throw new CustomerPhoneConflictException(normalised);
            }

            phone = normalised;
        }

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
            // D-6: the response reports the client's actual consent row. A client created before
            // the consent row existed still reads as `pending`, the one absent-row value.
            var existingConsent = await _context.CustomerConsents
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.OrganizationId == organizationId
                                 && candidate.CustomerId == existing.Id,
                    cancellationToken);

            return new CustomerCreatedDto(
                existing.Id,
                existing.FullName,
                existing.Level,
                existing.Status,
                existingConsent?.ConsentStatus ?? ConsentStatuses.AbsentRow,
                existing.CreatedAt,
                existing.Id);
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            OrganizationId = organizationId,
            FullName = name,
            Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? null : request.Nickname.Trim(),
            PhoneNumber = phone,
            Status = "new",
            Level = null,
            CreatedBy = actorUserId == Guid.Empty ? null : actorUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.Customers.Add(customer);

        // D-6: the consent row is created with the client, so the DTO reports a state that
        // actually exists in the database and the consent metrics have a row to count.
        var consent = new CustomerConsent
        {
            OrganizationId = organizationId,
            CustomerId = customer.Id,
            ConsentStatus = ConsentStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.CustomerConsents.Add(consent);

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

        // The client's concierge thread is created with the client. Without it the new client is
        // absent from the Salon list - the list is conversations, not clients - so a walk-in would
        // exist in the book but have nowhere to be discussed until somebody opened it by hand.
        await _conversations.EnsureCustomerSalonAsync(organizationId, customer.Id, cancellationToken);

        return new CustomerCreatedDto(
            customer.Id,
            customer.FullName,
            customer.Level,
            customer.Status,
            consent.ConsentStatus,
            customer.CreatedAt,
            null);
    }

    public async Task<TenantCustomerDetailDto?> GetDetailAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .Include(candidate => candidate.Preferences)
            .Include(candidate => candidate.Tags)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == customerId
                             && candidate.OrganizationId == organizationId
                             && candidate.DeletedAt == null,
                cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var interactionCount = await _context.CustomerInteractions
            .AsNoTracking()
            .CountAsync(
                interaction => interaction.OrganizationId == organizationId
                               && interaction.CustomerId == customerId,
                cancellationToken);

        return ToDetail(customer, interactionCount);
    }

    public async Task<TenantCustomerDetailDto?> UpdateAsync(
        Guid organizationId,
        Guid customerId,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(candidate => candidate.Preferences)
            .Include(candidate => candidate.Tags)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == customerId
                             && candidate.OrganizationId == organizationId
                             && candidate.DeletedAt == null,
                cancellationToken);

        if (customer is null)
        {
            return null;
        }

        if (request.FullName is not null)
        {
            var name = request.FullName.Trim();
            if (name.Length is 0 or > 200)
            {
                throw new ArgumentException("A name of 1 to 200 characters is required.", nameof(request));
            }

            customer.FullName = name;
        }

        if (request.Email is not null)
        {
            var email = request.Email.Trim();
            if (email.Length > 255)
            {
                throw new ArgumentException("An email of at most 255 characters is required.", nameof(request));
            }

            customer.Email = email.Length == 0 ? null : email;
        }

        if (request.Level is not null)
        {
            var level = request.Level.Trim();
            if (level.Length > 16)
            {
                throw new ArgumentException("A level of at most 16 characters is required.", nameof(request));
            }

            customer.Level = level.Length == 0 ? null : level;
        }

        if (request.PhoneNumber is not null)
        {
            // Normalise through the existing helper rather than a second implementation. A number
            // that is not a recognised Sri Lankan form is rejected rather than stored raw, so the
            // future lookup path (which normalises at query time) can still find it.
            var raw = request.PhoneNumber.Trim();
            var normalised = PhoneNormalizer.ToE164(raw);
            if (normalised is null)
            {
                if (raw.Length == 0)
                {
                    throw new ArgumentException("A phone number is required.", nameof(request));
                }

                throw new ArgumentException(
                    "That does not look like a Sri Lankan phone number.", nameof(request));
            }

            var takenByAnother = await _context.Customers
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.OrganizationId == organizationId
                                 && candidate.Id != customerId
                                 && candidate.DeletedAt == null
                                 && candidate.PhoneNumber == normalised,
                    cancellationToken);
            if (takenByAnother)
            {
                throw new CustomerPhoneConflictException(normalised);
            }

            customer.PhoneNumber = normalised;
        }

        if (request.Nickname is not null)
        {
            var nickname = request.Nickname.Trim();
            customer.Nickname = nickname.Length == 0 ? null : nickname;
            var preference = customer.Preferences
                .FirstOrDefault(candidate => candidate.PreferenceKey == NicknameKey);
            if (preference is null)
            {
                _context.CustomerPreferences.Add(new CustomerPreference
                {
                    OrganizationId = organizationId,
                    CustomerId = customerId,
                    PreferenceKey = NicknameKey,
                    PreferenceValue = nickname,
                });
            }
            else
            {
                preference.PreferenceValue = nickname;
            }
        }

        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var interactionCount = await _context.CustomerInteractions
            .AsNoTracking()
            .CountAsync(
                interaction => interaction.OrganizationId == organizationId
                               && interaction.CustomerId == customerId,
                cancellationToken);

        return ToDetail(customer, interactionCount);
    }

    public async Task<bool?> DeleteAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            // The soft-delete query filter would hide an already-deleted row and turn a repeated
            // delete into a 404. The tenant scope is preserved explicitly instead, so "already
            // deleted in *this* organisation" is reachable while another organisation's row is not.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == customerId
                             && candidate.OrganizationId == organizationId,
                cancellationToken);

        if (customer is null)
        {
            return null;
        }

        // Already deleted: the end state is reached, so this is the same answer rather than a 404.
        if (customer.DeletedAt is not null)
        {
            return true;
        }

        var openOrders = await CountOpenOrdersAsync(organizationId, customerId, cancellationToken);
        if (openOrders > 0)
        {
            throw new CustomerHasOpenOrdersException(openOrders);
        }

        customer.DeletedAt = DateTime.UtcNow;
        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CustomerInteractionPageDto?> GetInteractionsAsync(
        Guid organizationId,
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var exists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == customerId
                             && candidate.OrganizationId == organizationId
                             && candidate.DeletedAt == null,
                cancellationToken);
        if (!exists)
        {
            return null;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _context.CustomerInteractions
            .AsNoTracking()
            .Where(interaction => interaction.OrganizationId == organizationId
                                  && interaction.CustomerId == customerId);

        var total = await query.CountAsync(cancellationToken);
        var interactions = await query
            .OrderByDescending(interaction => interaction.CreatedAt)
            .ThenByDescending(interaction => interaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CustomerInteractionPageDto(
            interactions.Select(ToInteractionItem).ToList(),
            total,
            page,
            pageSize);
    }

    /// <summary>
    /// Orders whose status has an outgoing transition, plus the transient states. Terminal states
    /// (`completed`, `cancelled`, `rejected`) are the three with no outgoing edge in
    /// <c>OrderService.ValidTransitions</c>, so they are the only ones that do not block a delete.
    /// </summary>
    private static readonly string[] TerminalOrderStatuses = ["completed", "cancelled", "rejected"];

    private Task<int> CountOpenOrdersAsync(
        Guid organizationId, Guid customerId, CancellationToken cancellationToken)
        => _context.Orders
            .AsNoTracking()
            .CountAsync(
                order => order.OrganizationId == organizationId
                         && order.CustomerId == customerId
                         && !TerminalOrderStatuses.Contains(order.Status.ToLower()),
                cancellationToken);

    private static CustomerInteractionItemDto ToInteractionItem(CustomerInteraction interaction) => new(
        interaction.Id,
        interaction.CreatedAt,
        interaction.Channel,
        interaction.Direction,
        interaction.MessageContent,
        string.Equals(interaction.Channel, "in_person", StringComparison.OrdinalIgnoreCase)
        && string.Equals(interaction.Direction, "inbound", StringComparison.OrdinalIgnoreCase));

    private static TenantCustomerDetailDto ToDetail(Customer customer, int interactionCount) => new(
        customer.Id,
        customer.FullName,
        customer.Nickname ?? customer.Preferences.FirstOrDefault(preference => preference.PreferenceKey == NicknameKey)?.PreferenceValue,
        customer.PhoneNumber,
        customer.Email,
        customer.Level,
        customer.Status,
        customer.TotalSpent,
        customer.VisitCount,
        customer.LastVisitAt,
        // Constant 1: the tier is derived by CustomerLoyaltyService, so the client is told not to
        // render an editable control for it.
        LoyaltyTierIsDerived: 1,
        customer.CreatedAt,
        customer.UpdatedAt,
        interactionCount,
        customer.Tags.Select(tag => tag.Tag).OrderBy(tag => tag).ToList(),
        customer.Preferences.Where(p => p.PreferenceKey != NicknameKey).Select(CustomerPreferenceDto.From).ToList());

    private static CustomerBookItemDto ToBookItem(Customer customer) => new(
        customer.Id,
        customer.FullName,
        customer.Nickname ?? customer.Preferences.FirstOrDefault(preference => preference.PreferenceKey == NicknameKey)?.PreferenceValue,
        customer.Level,
        customer.Status,
        customer.PhoneNumber,
        customer.LastVisitAt,
        customer.VisitCount,
        customer.TotalSpent);

    private static string DisplayName(Customer customer) =>
        FirstPresent(customer.FullName)
        ?? FirstPresent(customer.Nickname)
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
