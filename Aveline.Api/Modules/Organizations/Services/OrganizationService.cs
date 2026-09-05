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
    private static readonly TimeSpan DefaultInviteValidity = TimeSpan.FromDays(7);

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IInvitationRepository _invitationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _userCacheService;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IInvitationRepository invitationRepository,
        IUserRepository userRepository,
        IUserCacheService userCacheService,
        ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository;
        _invitationRepository = invitationRepository;
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
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            InvitedByUserId = inviterUserId,
            RecipientEmail = string.IsNullOrWhiteSpace(request.RecipientEmail)
                ? null
                : request.RecipientEmail.Trim().ToLowerInvariant(),
            RecipientUserId = request.RecipientUserId,
            TokenHash = InvitationTokens.Hash(code),
            BoutiqueRole = request.BoutiqueRole,
            ExpiresAt = DateTime.UtcNow.Add(request.ValidFor ?? DefaultInviteValidity),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        invitation = await _invitationRepository.CreateAsync(invitation, cancellationToken);

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

        var invitation = await _invitationRepository.GetByTokenHashAsync(
            InvitationTokens.Hash(code), cancellationToken)
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

    public async Task<bool> HasActiveMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _organizationRepository.UserHasActiveMembershipAsync(userId, cancellationToken);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();

    private static string ToSlug(string name)
    {
        var slug = NonAlphanumericRegex().Replace(name.ToLowerInvariant(), "-");
        return slug.Trim('-');
    }
}
