/**
 * Claims minted by the Clerk `jwt-aveline-v1` template, mirroring
 * `Aveline.Api/Authorization/Roles.cs` (user_role / org_role).
 */
export interface AvelineClaims {
  user_role?: string
  org_role?: string
  org_id?: string
  org_slug?: string
}

/**
 * Roles allowed on the admin dashboard. Mirrors the API's `Managers` policy
 * (see `Aveline.Api/Configurations/AuthorizationConfiguration.cs`).
 */
const ADMIN_ROLES = new Set([
  'moderator',
  'admin',
  'owner',
  'org:boutique_manager',
  'org:boutique_supervisor',
  'org:boutique_owner',
])

/** Decodes a JWT payload into a plain object. Returns `{}` if undecodable. */
export function decodeJwtPayload(token: string): Record<string, unknown> {
  const [, payload] = token.split('.')
  if (!payload) {
    return {}
  }

  const normalized = payload.replace(/-/g, '+').replace(/_/g, '/')
  const padded = normalized.padEnd(
    normalized.length + ((4 - (normalized.length % 4)) % 4),
    '=',
  )
  const json = atob(padded)

  try {
    return JSON.parse(
      decodeURIComponent(
        Array.prototype.map.call(json, (char: string) =>
          `%${char.charCodeAt(0).toString(16).padStart(2, '0')}`,
        ).join(''),
      ),
    ) as Record<string, unknown>
  } catch {
    return JSON.parse(json) as Record<string, unknown>
  }
}

/**
 * Whether the Aveline role claims grant admin access (owner/manager),
 * matching the backend's `Managers` policy.
 */
export function hasAdminRole(
  claims: Pick<AvelineClaims, 'user_role' | 'org_role'>,
): boolean {
  const userRole = claims.user_role?.toLowerCase()
  const orgRole = claims.org_role?.toLowerCase()
  return (
    (userRole != null && ADMIN_ROLES.has(userRole)) ||
    (orgRole != null && ADMIN_ROLES.has(orgRole))
  )
}
