using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Organizations.Webhooks;

/// <summary>Applies a verified Clerk webhook event to the local read model.</summary>
public interface IClerkWebhookSyncService
{
    Task HandleAsync(string eventType, JsonElement data, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the local <see cref="User"/>/<see cref="Organization"/> read models in sync with
/// Clerk. Idempotent by construction: every handler upserts on the external id, so a
/// redelivered event converges to the same state (FR-3.7/FR-3.8 correctness gap).
/// </summary>
public sealed class ClerkWebhookSyncService : IClerkWebhookSyncService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ClerkWebhookSyncService> _logger;

    public ClerkWebhookSyncService(AppDbContext context, ILogger<ClerkWebhookSyncService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(
        string eventType, JsonElement data, CancellationToken cancellationToken = default)
    {
        switch (eventType)
        {
            case "user.created":
            case "user.updated":
                await UpsertUserAsync(data, cancellationToken);
                break;

            case "user.deleted":
                await DeleteUserAsync(data, cancellationToken);
                break;

            case "organization.created":
            case "organization.updated":
                await UpsertOrganizationAsync(data, cancellationToken);
                break;

            case "organizationMembership.created":
            case "organizationMembership.updated":
                await UpsertMembershipAsync(data, cancellationToken);
                break;

            case "organizationMembership.deleted":
                await RemoveMembershipAsync(data, cancellationToken);
                break;

            default:
                _logger.LogDebug("Ignoring unsupported Clerk webhook event. type={Type}", eventType);
                break;
        }
    }

    private async Task UpsertUserAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var clerkId = ReadString(data, "id");
        if (string.IsNullOrWhiteSpace(clerkId))
        {
            return;
        }

        var email = ReadPrimaryEmail(data);
        var firstName = ReadString(data, "first_name") ?? string.Empty;
        var lastName = ReadString(data, "last_name") ?? string.Empty;
        var imageUrl = ReadString(data, "image_url");
        var role = ReadPublicMetadataRole(data) ?? string.Empty;

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.ClerkId == clerkId, cancellationToken);

        if (user is null)
        {
            _context.Users.Add(new User
            {
                ClerkId = clerkId,
                Email = email ?? string.Empty,
                FirstName = firstName,
                LastName = lastName,
                Username = email?.Contains('@') == true ? email.Split('@')[0] : clerkId,
                ProfileImageUrl = imageUrl,
                UserRole = role,
                AccountState = AccountState.OnboardingPending,
                HasCompletedOnboarding = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            if (email is not null)
            {
                user.Email = email;
            }

            if (firstName.Length > 0)
            {
                user.FirstName = firstName;
            }

            if (lastName.Length > 0)
            {
                user.LastName = lastName;
            }

            if (imageUrl is not null)
            {
                user.ProfileImageUrl = imageUrl;
            }

            if (role.Length > 0)
            {
                user.UserRole = role;
            }

            user.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Clerk user webhook applied. clerkId={ClerkId}", clerkId);
    }

    private async Task DeleteUserAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var clerkId = ReadString(data, "id");
        if (string.IsNullOrWhiteSpace(clerkId))
        {
            return;
        }

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.ClerkId == clerkId, cancellationToken);
        if (user is null)
        {
            return;
        }

        user.DeletedAt = DateTime.UtcNow;
        user.IsActive = false;
        user.AccountState = AccountState.Suspended;
        user.UpdatedAt = DateTime.UtcNow;

        var memberships = await _context.OrganizationMemberships
            .Where(m => m.UserId == user.Id)
            .ToListAsync(cancellationToken);
        _context.OrganizationMemberships.RemoveRange(memberships);

        var keys = await _context.ApiKeys
            .Where(k => k.CreatedByUserId == user.Id && k.Status == Modules.ApiAccess.Models.ApiKeyStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var key in keys)
        {
            key.Status = Modules.ApiAccess.Models.ApiKeyStatus.Revoked;
            key.RevokedAt = DateTime.UtcNow;
            key.RevokedReason = "The Clerk user was deleted.";
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Clerk user deletion applied. clerkId={ClerkId}", clerkId);
    }

    private async Task UpsertOrganizationAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var clerkOrgId = ReadString(data, "id");
        if (string.IsNullOrWhiteSpace(clerkOrgId))
        {
            return;
        }

        var organization = await _context.Organizations
            .FirstOrDefaultAsync(o => o.ClerkOrgId == clerkOrgId, cancellationToken);
        if (organization is null)
        {
            // Aveline creates organizations through onboarding; a Clerk org with no local
            // row is not adopted here so an unreviewed tenant cannot appear.
            _logger.LogWarning("Clerk organization has no local row. clerkOrgId={ClerkOrgId}", clerkOrgId);
            return;
        }

        var name = ReadString(data, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            organization.Name = name.Trim();
        }

        organization.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Clerk organization webhook applied. clerkOrgId={ClerkOrgId}", clerkOrgId);
    }

    private async Task UpsertMembershipAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var (organization, user) = await ResolveMembershipPartiesAsync(data, cancellationToken);
        if (organization is null || user is null)
        {
            return;
        }

        var role = MapBoutiqueRole(ReadString(data, "role"));
        var membership = await _context.OrganizationMemberships
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organization.Id && m.UserId == user.Id,
                cancellationToken);

        if (membership is null)
        {
            _context.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = organization.Id,
                UserId = user.Id,
                BoutiqueRole = role,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            membership.BoutiqueRole = role;
            membership.Status = MembershipStatus.Active;
            membership.UpdatedAt = DateTime.UtcNow;
        }

        user.OrganizationRole = role;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Clerk membership webhook applied. organizationId={OrganizationId} userId={UserId} role={Role}",
            organization.Id, user.Id, role);
    }

    private async Task RemoveMembershipAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var (organization, user) = await ResolveMembershipPartiesAsync(data, cancellationToken);
        if (organization is null || user is null)
        {
            return;
        }

        var membership = await _context.OrganizationMemberships
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organization.Id && m.UserId == user.Id,
                cancellationToken);
        if (membership is not null)
        {
            _context.OrganizationMemberships.Remove(membership);
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Clerk membership removal applied. organizationId={OrganizationId} userId={UserId}",
            organization.Id, user.Id);
    }

    private async Task<(Organization? Organization, User? User)> ResolveMembershipPartiesAsync(
        JsonElement data, CancellationToken cancellationToken)
    {
        var clerkOrgId = data.TryGetProperty("organization", out var organization)
            ? ReadString(organization, "id")
            : null;
        var clerkUserId = data.TryGetProperty("public_user_data", out var publicUserData)
            ? ReadString(publicUserData, "user_id")
            : null;

        if (string.IsNullOrWhiteSpace(clerkOrgId) || string.IsNullOrWhiteSpace(clerkUserId))
        {
            return (null, null);
        }

        var localOrg = await _context.Organizations
            .FirstOrDefaultAsync(o => o.ClerkOrgId == clerkOrgId, cancellationToken);
        var localUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.ClerkId == clerkUserId, cancellationToken);
        return (localOrg, localUser);
    }

    /// <summary>
    /// Maps a Clerk organization role onto a canonical boutique role. Only the canonical
    /// Clerk role strings map to a privileged boutique role; anything else (including a
    /// custom role whose name merely contains "owner") defaults to boutique staff (M-6).
    /// </summary>
    public static string MapBoutiqueRole(string? clerkRole)
    {
        var role = clerkRole ?? string.Empty;
        return role switch
        {
            "org:boutique_owner" => Roles.BoutiqueOwner,
            "org:boutique_supervisor" => Roles.BoutiqueSupervisor,
            "org:boutique_manager" => Roles.BoutiqueManager,
            "org:boutique_staff" => Roles.BoutiqueStaff,
            _ => Roles.BoutiqueStaff,
        };
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string? ReadPrimaryEmail(JsonElement data)
    {
        if (!data.TryGetProperty("email_addresses", out var addresses)
            || addresses.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var address in addresses.EnumerateArray())
        {
            var value = ReadString(address, "email_address");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    private static string? ReadPublicMetadataRole(JsonElement data)
    {
        if (!data.TryGetProperty("public_metadata", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(metadata, "role");
    }
}
