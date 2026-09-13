using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Organizations.Services;

/// <summary>
/// Organization ownership, staff membership, and invitation lifecycle. Memberships
/// are the canonical record; the owner membership is created with the organization,
/// and staff memberships become active only after an invitation is accepted.
/// </summary>
public partial class OrganizationService : IOrganizationService
{
    /// <summary>Default life of an invitation. Mirrors the code-store TTL so they never drift.</summary>
    private static readonly TimeSpan DefaultInviteValidity = TimeSpan.FromHours(24);

    /// <summary>Every role a membership may hold. Owner is included but is not invitable.</summary>
    private static readonly string[] BoutiqueRoles =
    [
        Roles.BoutiqueOwner,
        Roles.BoutiqueSupervisor,
        Roles.BoutiqueManager,
        Roles.BoutiqueStaff,
    ];

    private const int MaxNameLength = 200;
    private const int MaxMemberPageSize = 100;

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IInvitationRepository _invitationRepository;
    private readonly IInvitationCodeStore _codeStore;
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _userCacheService;
    private readonly ILogger<OrganizationService> _logger;
    private readonly IEntitlementResolver? _entitlementResolver;
    private readonly IAuditService? _auditService;
    private readonly IEventBus? _eventBus;

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IInvitationRepository invitationRepository,
        IInvitationCodeStore codeStore,
        IUserRepository userRepository,
        IUserCacheService userCacheService,
        ILogger<OrganizationService> logger,
        IEntitlementResolver? entitlementResolver = null,
        IAuditService? auditService = null,
        IEventBus? eventBus = null)
    {
        _organizationRepository = organizationRepository;
        _invitationRepository = invitationRepository;
        _codeStore = codeStore;
        _userRepository = userRepository;
        _userCacheService = userCacheService;
        _logger = logger;
        _entitlementResolver = entitlementResolver;
        _auditService = auditService;
        _eventBus = eventBus;
    }

    /// <summary>
    /// Invalidates the cached authorization-relevant user state so the next request
    /// re-syncs from the canonical membership tables instead of a stale snapshot.
    /// </summary>
    private async Task InvalidateUserCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        await _userCacheService.InvalidateAsync(user.ClerkId, cancellationToken);
    }

    public async Task<Organization> CreateOrganizationAsync(
        Guid ownerUserId,
        string name,
        string? clerkOrgId,
        string? slug = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var requested = string.IsNullOrWhiteSpace(slug) ? name : slug;
        slug = OrgSlug.From(requested);
        if (string.IsNullOrEmpty(slug))
        {
            throw new ArgumentException("The boutique slug must contain letters or numbers.");
        }

        if (await _organizationRepository.ExistsBySlugAsync(slug, cancellationToken))
        {
            throw new OrganizationSlugAlreadyInUseException(slug);
        }

        var organization = new Organization
        {
            Name = name.Trim(),
            Slug = slug,
            ClerkOrgId = string.IsNullOrWhiteSpace(clerkOrgId) ? null : clerkOrgId.Trim(),
            OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        organization = await _organizationRepository.CreateAsync(organization, cancellationToken);

        // The owner is the first member of their boutique.
        await _organizationRepository.AddMembershipAsync(
            new OrganizationMembership
            {
                OrganizationId = organization.Id,
                UserId = ownerUserId,
                BoutiqueRole = Roles.BoutiqueOwner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            cancellationToken);

        _logger.LogInformation(
            "Organization created. organizationId={OrganizationId} slug={Slug} ownerUserId={OwnerUserId}",
            organization.Id, organization.Slug, ownerUserId);

        await InvalidateUserCacheAsync(ownerUserId, cancellationToken);

        return organization;
    }

    public async Task<InviteMemberResult> InviteMemberAsync(
        Guid organizationId,
        Guid inviterUserId,
        InviteMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Organization '{organizationId}' not found.");

        var code = InvitationTokens.GenerateCode();
        var validity = request.ValidFor ?? DefaultInviteValidity;
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            InvitedByUserId = inviterUserId,
            RecipientEmail = string.IsNullOrWhiteSpace(request.RecipientEmail)
                ? null
                : request.RecipientEmail.Trim().ToLowerInvariant(),
            RecipientUserId = request.RecipientUserId,
            // Hash kept in Postgres as a durable fallback so the code remains redeemable if
            // the code store is flushed before the Redis TTL expires.
            TokenHash = InvitationTokens.Hash(code),
            BoutiqueRole = request.BoutiqueRole,
            ExpiresAt = DateTime.UtcNow.Add(validity),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        invitation = await _invitationRepository.CreateAsync(invitation, cancellationToken);

        // Write the code -> invitationId mapping to the transient store. This is the primary
        // accept path; fail fast (and compensate the just-inserted log row) if it is down.
        try
        {
            await _codeStore.StoreAsync(code, invitation.Id, validity, cancellationToken);
        }
        catch (Exception)
        {
            await _invitationRepository.DeleteAsync(invitation.Id, cancellationToken);
            throw new InvitationCodeStoreUnavailableException();
        }

        _logger.LogInformation(
            "Invitation created. invitationId={InvitationId} organizationId={OrganizationId} role={Role} recipientEmail={RecipientEmail}",
            invitation.Id, organization.Id, request.BoutiqueRole, invitation.RecipientEmail);

        return new InviteMemberResult(invitation, code);
    }

    public async Task<AcceptedMembership> AcceptInvitationAsync(
        string code,
        Guid acceptingUserId,
        string? acceptingEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var normalized = code.Trim();

        // Primary path: resolve via the transient code store; fall back to the durable
        // Postgres hash when the store misses (e.g. after a Redis flush).
        var invitation = await ResolveInvitationByCodeAsync(normalized, cancellationToken)
            ?? throw new InvitationNotFoundException();

        var now = DateTime.UtcNow;

        if (invitation.AcceptedAt is not null)
        {
            throw new InvitationNotAcceptableException("already accepted");
        }

        if (invitation.RevokedAt is not null)
        {
            throw new InvitationNotAcceptableException("revoked");
        }

        if (invitation.ExpiresAt <= now)
        {
            throw new InvitationNotAcceptableException("expired");
        }

        if (invitation.RecipientUserId is not null && invitation.RecipientUserId != acceptingUserId)
        {
            throw new InvitationRecipientMismatchException();
        }

        if (invitation.RecipientEmail is not null
            && !string.Equals(invitation.RecipientEmail, acceptingEmail?.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new InvitationRecipientMismatchException();
        }

        var existing = await _organizationRepository.GetMembershipAsync(
            invitation.OrganizationId, acceptingUserId, cancellationToken);
        if (existing is not null)
        {
            throw new MembershipAlreadyExistsException();
        }

        var membership = new OrganizationMembership
        {
            OrganizationId = invitation.OrganizationId,
            UserId = acceptingUserId,
            BoutiqueRole = invitation.BoutiqueRole,
            Status = MembershipStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Atomically activate the membership and mark the invitation accepted.
        await _invitationRepository.AcceptAsync(invitation, membership, now, cancellationToken);

        // One-time use: best-effort remove the transient code mapping.
        await RemoveCodeAsync(normalized, cancellationToken);

        var organization = await _organizationRepository.GetByIdAsync(
            invitation.OrganizationId, cancellationToken);

        _logger.LogInformation(
            "Invitation accepted. invitationId={InvitationId} userId={UserId} organizationId={OrganizationId} role={Role}",
            invitation.Id, acceptingUserId, invitation.OrganizationId, invitation.BoutiqueRole);

        await InvalidateUserCacheAsync(acceptingUserId, cancellationToken);

        return new AcceptedMembership(
            invitation.OrganizationId,
            acceptingUserId,
            invitation.BoutiqueRole,
            organization?.ClerkOrgId);
    }

    public async Task<IReadOnlyList<OrganizationMembership>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _organizationRepository.ListMembershipsForUserAsync(userId, cancellationToken);
    }

    public async Task<OrganizationProfileWithMembershipDto?> GetOrganizationProfileBySlugAsync(
        string slug,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetBySlugAsync(slug, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        var membership = await _organizationRepository.GetMembershipAsync(
            organization.Id, userId, cancellationToken);

        return new OrganizationProfileWithMembershipDto(
            Organization: MapProfile(organization),
            Membership: membership is null ? null : MapMembership(organization, membership));
    }

    public async Task<OrganizationProfileDto?> GetOrganizationProfileAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        return organization is null ? null : MapProfile(organization);
    }

    private static OrganizationProfileDto MapProfile(Organization org) => new(
        org.Id,
        org.Name,
        org.Slug,
        org.ClerkOrgId,
        org.OwnerUserId,
        org.Address,
        org.PhoneNumber,
        org.Description,
        org.LogoUrl,
        org.PlanTier,
        org.HasCompletedOnboarding,
        org.CreatedAt);

    private static OrganizationMembershipView MapMembership(Organization org, OrganizationMembership m) => new(
        m.OrganizationId,
        org.Name,
        org.Slug,
        m.BoutiqueRole,
        m.Status.ToString());

    public async Task<IReadOnlyList<OrganizationInvitation>> ListPendingInvitationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _invitationRepository.ListPendingByOrganizationAsync(organizationId, cancellationToken);
    }

    public async Task RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid revokingUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _invitationRepository.GetByIdAsync(invitationId, cancellationToken)
            ?? throw new InvitationNotFoundException();

        if (invitation.OrganizationId != organizationId)
        {
            throw new InvitationNotFoundException();
        }

        if (invitation.AcceptedAt is not null)
        {
            throw new CannotRevokeAcceptedInvitationException();
        }

        invitation.RevokedAt = DateTime.UtcNow;
        invitation.RevokedByUserId = revokingUserId;
        await _invitationRepository.UpdateAsync(invitation, cancellationToken);

        _logger.LogInformation(
            "Invitation revoked. invitationId={InvitationId} organizationId={OrganizationId} revokedBy={RevokedBy}",
            invitation.Id, organizationId, revokingUserId);
    }

    public async Task<bool> HasActiveMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _organizationRepository.UserHasActiveMembershipAsync(userId, cancellationToken);
    }

    public async Task<OrganizationMembership> SetMembershipStatusAsync(
        Guid organizationId,
        Guid memberUserId,
        MembershipStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status == MembershipStatus.Active)
        {
            return await ActivateMembershipAsync(organizationId, memberUserId, cancellationToken);
        }

        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Organization '{organizationId}' not found.");

        if (organization.OwnerUserId == memberUserId)
        {
            throw new CannotManageOwnerMembershipException();
        }

        var membership = await _organizationRepository.GetMembershipAsync(
            organizationId, memberUserId, cancellationToken)
            ?? throw new MembershipNotFoundException(organizationId, memberUserId);

        membership.Status = status;
        await _organizationRepository.UpdateMembershipAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Membership status updated. organizationId={OrganizationId} userId={UserId} status={Status}",
            organizationId, memberUserId, status);

        await InvalidateUserCacheAsync(memberUserId, cancellationToken);
        return membership;
    }

    public async Task RemoveMembershipAsync(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Organization '{organizationId}' not found.");

        if (organization.OwnerUserId == memberUserId)
        {
            throw new CannotManageOwnerMembershipException();
        }

        var membership = await _organizationRepository.GetMembershipAsync(
            organizationId, memberUserId, cancellationToken)
            ?? throw new MembershipNotFoundException(organizationId, memberUserId);

        await _organizationRepository.RemoveMembershipAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Membership removed. organizationId={OrganizationId} userId={UserId}",
            organizationId, memberUserId);

        await InvalidateUserCacheAsync(memberUserId, cancellationToken);
    }

    public async Task<Organization> UpdateSettingsAsync(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Organization '{organizationId}' not found.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length is 0 or > MaxNameLength)
            {
                throw new ArgumentException($"The organization name must be 1..{MaxNameLength} characters.");
            }

            organization.Name = name;
        }

        if (request.Slug is not null)
        {
            var slug = OrgSlug.From(request.Slug);
            if (string.IsNullOrEmpty(slug))
            {
                throw new ArgumentException("The boutique slug must contain letters or numbers.");
            }

            if (!string.Equals(slug, organization.Slug, StringComparison.Ordinal))
            {
                if (await _organizationRepository.ExistsBySlugAsync(slug, cancellationToken))
                {
                    throw new OrganizationSlugAlreadyInUseException(slug);
                }

                organization.Slug = slug;
            }
        }

        await EnsureAiContextEntitledAsync(organization, request, cancellationToken);

        organization.Address = Apply(request.Address, organization.Address);
        organization.PhoneNumber = Apply(request.PhoneNumber, organization.PhoneNumber);
        organization.Description = Apply(request.Description, organization.Description);
        organization.LogoUrl = Apply(request.LogoUrl, organization.LogoUrl);
        organization.BrandVoice = Apply(request.BrandVoice, organization.BrandVoice);
        organization.BusinessRules = Apply(request.BusinessRules, organization.BusinessRules);
        organization.PreferredColorsFabrics = Apply(request.PreferredColorsFabrics, organization.PreferredColorsFabrics);
        organization.CustomerPreferences = Apply(request.CustomerPreferences, organization.CustomerPreferences);

        if (request.BillingEmail is not null)
        {
            organization.BillingEmail = NormalizeEmail(request.BillingEmail, "billingEmail");
        }

        if (request.ContactEmail is not null)
        {
            organization.ContactEmail = NormalizeEmail(request.ContactEmail, "contactEmail");
        }

        if (request.Currency is not null)
        {
            var currency = request.Currency.Trim().ToUpperInvariant();
            if (currency.Length != 3 || !currency.All(char.IsLetter))
            {
                throw new ArgumentException("Currency must be a 3-letter ISO-4217 code.");
            }

            organization.Currency = currency;
        }

        if (request.TimeZone is not null)
        {
            var timeZone = request.TimeZone.Trim();
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
            catch (Exception) when (timeZone.Length > 0)
            {
                throw new ArgumentException($"'{timeZone}' is not a known IANA time zone.");
            }

            organization.TimeZone = timeZone;
        }

        organization.UpdatedAt = DateTime.UtcNow;
        await _organizationRepository.UpdateAsync(organization, cancellationToken);
        _logger.LogInformation(
            "Organization settings updated. organizationId={OrganizationId} actorUserId={ActorUserId}",
            organizationId, actorUserId);

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(new AuditEntryRequest(
                AuditAction.OrganizationSettingsUpdated,
                "Organization",
                organization.Id.ToString(),
                OrganizationId: organization.Id,
                ActorKind: AuditActorKind.User,
                ActorUserId: actorUserId,
                After: OrganizationSettingsDto.From(organization)), cancellationToken);
        }

        if (_eventBus is not null)
        {
            await _eventBus.PublishAsync(
                "org.settings.updated",
                organization.Id,
                new { organizationId = organization.Id },
                cancellationToken: cancellationToken);
        }

        return organization;
    }

    public async Task<PagedOrganizationMembers> ListMembersAsync(
        Guid organizationId,
        string? status,
        string? role,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxMemberPageSize ? 20 : pageSize;

        var memberships = await _organizationRepository.ListMembershipsWithUsersAsync(
            organizationId, cancellationToken);

        var query = memberships.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => string.Equals(m.Status.ToString(), status.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(m => string.Equals(m.BoutiqueRole, role.Trim(), StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m =>
                Contains(m.User?.Email, term)
                || Contains(m.User?.FirstName, term)
                || Contains(m.User?.LastName, term)
                || Contains(m.User?.DisplayName, term));
        }

        var filtered = query.ToList();
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapMember)
            .ToArray();

        return new PagedOrganizationMembers(items, page, pageSize, filtered.Count);
    }

    public async Task<OrganizationMemberDto> ChangeMemberRoleAsync(
        Guid organizationId,
        Guid memberUserId,
        string newRole,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newRole)
            || !BoutiqueRoles.Contains(newRole.Trim(), StringComparer.Ordinal))
        {
            throw new InvalidBoutiqueRoleException(newRole ?? string.Empty);
        }

        var role = newRole.Trim();

        var membership = await _organizationRepository.GetMembershipAsync(
            organizationId, memberUserId, cancellationToken)
            ?? throw new MembershipNotFoundException(organizationId, memberUserId);

        if (memberUserId == actingUserId)
        {
            throw new CannotChangeOwnRoleException();
        }

        var actorMembership = await _organizationRepository.GetMembershipAsync(
            organizationId, actingUserId, cancellationToken);
        var actorRole = actorMembership?.BoutiqueRole;

        if (membership.BoutiqueRole == Roles.BoutiqueOwner && role != Roles.BoutiqueOwner)
        {
            var memberships = await _organizationRepository.ListMembershipsForOrganizationAsync(
                organizationId, cancellationToken);
            var activeOwners = memberships.Count(m =>
                m.BoutiqueRole == Roles.BoutiqueOwner && m.Status == MembershipStatus.Active);

            if (activeOwners <= 1)
            {
                throw new CannotDemoteLastOwnerException();
            }
        }

        if ((role == Roles.BoutiqueOwner || membership.BoutiqueRole == Roles.BoutiqueOwner)
            && actorRole != Roles.BoutiqueOwner)
        {
            throw new OwnerRoleChangeNotPermittedException();
        }

        var previousRole = membership.BoutiqueRole;
        membership.BoutiqueRole = role;
        await _organizationRepository.UpdateMembershipAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Membership role changed. organizationId={OrganizationId} userId={UserId} from={PreviousRole} to={Role}",
            organizationId, memberUserId, previousRole, role);

        await InvalidateUserCacheAsync(memberUserId, cancellationToken);

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(new AuditEntryRequest(
                AuditAction.MembershipRoleChanged,
                "OrganizationMembership",
                membership.Id.ToString(),
                OrganizationId: organizationId,
                ActorKind: AuditActorKind.User,
                ActorUserId: actingUserId,
                Before: new { BoutiqueRole = previousRole },
                After: new { BoutiqueRole = role }), cancellationToken);
        }

        if (_eventBus is not null)
        {
            await _eventBus.PublishAsync(
                "membership.role.changed",
                organizationId,
                new { organizationId, userId = memberUserId, role },
                cancellationToken: cancellationToken);
        }

        return MapMember(membership);
    }

    private async Task EnsureAiContextEntitledAsync(
        Organization organization,
        UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var touchesAiContext = request.BrandVoice is not null
                               || request.BusinessRules is not null
                               || request.PreferredColorsFabrics is not null
                               || request.CustomerPreferences is not null;

        if (!touchesAiContext || _entitlementResolver is null)
        {
            return;
        }

        var customContext = (await _entitlementResolver.GetAsync(
            organization.Id, "ai.customContext", at: null, cancellationToken))?.Text ?? "none";

        if (customContext == "none")
        {
            throw new ArgumentException(
                "Custom AI context is not available on the Seed plan. Please upgrade to Bloom or Orchid to configure bespoke brand voice and rules.");
        }

        if (customContext == "basic" && request.CustomerPreferences is not null)
        {
            throw new ArgumentException("Deep customer memory rules require Orchid or Rose plans.");
        }
    }

    private static OrganizationMemberDto MapMember(OrganizationMembership membership)
    {
        var user = membership.User;
        return new OrganizationMemberDto(
            membership.UserId,
            user?.Email ?? string.Empty,
            user?.FirstName ?? string.Empty,
            user?.LastName ?? string.Empty,
            user?.DisplayName,
            user?.ProfileImageUrl,
            membership.BoutiqueRole,
            membership.Status.ToString(),
            membership.CreatedAt);
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>Empty string clears a nullable field; null leaves it unchanged.</summary>
    private static string? Apply(string? value, string? current) =>
        value is null ? current : string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEmail(string value, string field)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 254 || !trimmed.Contains('@'))
        {
            throw new ArgumentException($"'{field}' must be a valid email address.");
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>Reactivates a membership (previously suspended or pending).</summary>
    private async Task<OrganizationMembership> ActivateMembershipAsync(        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        var membership = await _organizationRepository.GetMembershipAsync(
            organizationId, memberUserId, cancellationToken)
            ?? throw new MembershipNotFoundException(organizationId, memberUserId);

        membership.Status = MembershipStatus.Active;
        await _organizationRepository.UpdateMembershipAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Membership activated. organizationId={OrganizationId} userId={UserId}",
            organizationId, memberUserId);

        await InvalidateUserCacheAsync(memberUserId, cancellationToken);
        return membership;
    }

    /// <summary>Resolves an invitation from a code via the transient store, falling back to the durable hash.</summary>
    private async Task<OrganizationInvitation?> ResolveInvitationByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var invitationId = await _codeStore.GetAsync(code, cancellationToken);
        if (invitationId is not null)
        {
            return await _invitationRepository.GetByIdAsync(invitationId.Value, cancellationToken);
        }

        return await _invitationRepository.GetByTokenHashAsync(
            InvitationTokens.Hash(code), cancellationToken);
    }

    /// <summary>Best-effort removal of a redeemed code's transient mapping.</summary>
    private async Task RemoveCodeAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            await _codeStore.RemoveAsync(code, cancellationToken);
        }
        catch (Exception ex)
        {
            // The Postgres log is authoritative; a leftover code is harmless and TTL-expires.
            _logger.LogWarning(ex, "Could not remove redeemed invitation code from the store. It will TTL-expire.");
        }
    }
}
