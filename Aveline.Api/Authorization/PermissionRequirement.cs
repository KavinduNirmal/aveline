using Microsoft.AspNetCore.Authorization;

namespace Aveline.Api.Authorization;

/// <summary>
/// Authorization requirement for a named permission, granted via the role-permission
/// catalog in <see cref="Permissions"/>.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
