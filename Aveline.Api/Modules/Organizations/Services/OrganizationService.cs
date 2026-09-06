using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
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

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IInvitationRepository _invitationRepository;
    private readonly IInvitationCodeStore _codeStore;
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _userCacheService;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IInvitationRepository invitationRepository,
        IInvitationCodeStore codeStore,
        IUserRepository userRepository,
        IUserCacheService userCacheService,
        ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository;
        _invitationRepository = invitationRepository;
        _codeStore = codeStore;
        _userRepository = userRepository;
        _userCacheService = userCacheService;
        _logger = logger;
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

        slug = string.IsNullOrWhiteSpace(slug) ? ToSlug(name) : slug.Trim();
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

    /// <summary>Reactivates a membership (previously suspended or pending).</summary>
    private async Task<OrganizationMembership> ActivateMembershipAsync(
        Guid organizationId,
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

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();

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

    private static string ToSlug(string name)
    {
        var slug = NonAlphanumericRegex().Replace(name.ToLowerInvariant(), "-");
        return slug.Trim('-');
    }
}
