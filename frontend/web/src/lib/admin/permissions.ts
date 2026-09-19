/**
 * Canonical 24-permission catalog mirroring `Aveline.Api/Authorization/Permissions.cs`.
 * This client mirror is used strictly for presentation and navigation gating.
 * Authoritative enforcement is always performed server-side.
 */
export type Permission =
  | "catalog:view"
  | "customers:view"
  | "catalog:manage"
  | "approvals:approve"
  | "payments:refund"
  | "reports:view"
  | "settings:manage"
  | "conversations:view"
  | "billing:view"
  | "billing:view:self"
  | "billing:manage"
  | "billing:adjust"
  | "pricing:view"
  | "pricing:manage"
  | "pricing:backdate"
  | "apikeys:view"
  | "apikeys:manage"
  | "stats:view"
  | "stats:view:agent"
  | "stats:system"
  | "admin:users:read"
  | "admin:users:manage"
  | "admin:orgs:read"
  | "audit:view"

export const ALL_PERMISSIONS: readonly Permission[] = [
  "catalog:view",
  "customers:view",
  "catalog:manage",
  "approvals:approve",
  "payments:refund",
  "reports:view",
  "settings:manage",
  "conversations:view",
  "billing:view",
  "billing:view:self",
  "billing:manage",
  "billing:adjust",
  "pricing:view",
  "pricing:manage",
  "pricing:backdate",
  "apikeys:view",
  "apikeys:manage",
  "stats:view",
  "stats:view:agent",
  "stats:system",
  "admin:users:read",
  "admin:users:manage",
  "admin:orgs:read",
  "audit:view",
]

/**
 * Canonical Role-to-Permissions mapping from `Permissions.cs:99-122`.
 */
export const ROLE_PERMISSIONS: Record<string, readonly Permission[]> = {
  staff: ["catalog:view", "conversations:view"],
  customer_relations: ["catalog:view", "customers:view", "conversations:view"],
  moderator: [
    "catalog:view",
    "customers:view",
    "approvals:approve",
    "conversations:view",
    "billing:view",
    "stats:view",
    "stats:view:agent",
    "admin:orgs:read",
  ],
  admin: ALL_PERMISSIONS.filter((p) => p !== "pricing:backdate"),
  owner: ALL_PERMISSIONS,

  "org:boutique_staff": [
    "catalog:view",
    "customers:view",
    "conversations:view",
    "billing:view:self",
  ],
  "org:boutique_manager": [
    "catalog:view",
    "customers:view",
    "catalog:manage",
    "reports:view",
    "conversations:view",
    "billing:view",
    "billing:view:self",
    "pricing:view",
    "stats:view",
  ],
  "org:boutique_supervisor": [
    "catalog:view",
    "customers:view",
    "catalog:manage",
    "approvals:approve",
    "reports:view",
    "conversations:view",
    "billing:view:self",
    "stats:view",
  ],
  "org:boutique_owner": [
    "catalog:view",
    "customers:view",
    "catalog:manage",
    "approvals:approve",
    "payments:refund",
    "reports:view",
    "settings:manage",
    "conversations:view",
    "billing:view",
    "billing:view:self",
    "billing:manage",
    "pricing:view",
    "apikeys:view",
    "apikeys:manage",
    "stats:view",
    "stats:view:agent",
  ],
}

/**
 * Resolves whether a set of roles grants a specific permission.
 */
export function hasAdminPermission(
  roles: readonly string[] | null | undefined,
  permission: Permission,
): boolean {
  if (!roles || roles.length === 0) return false
  return roles.some((role) => {
    const grants = ROLE_PERMISSIONS[role.toLowerCase()]
    return grants?.includes(permission) ?? false
  })
}

/**
 * Returns all distinct permissions granted to a list of roles.
 */
export function resolvePermissions(
  roles: readonly string[] | null | undefined,
): Set<Permission> {
  const result = new Set<Permission>()
  if (!roles) return result

  for (const role of roles) {
    const grants = ROLE_PERMISSIONS[role.toLowerCase()]
    if (grants) {
      for (const p of grants) {
        result.add(p)
      }
    }
  }
  return result
}
