/**
 * Canonical 31-permission catalog mirroring `Aveline.Api/Authorization/Permissions.cs`.
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
  | "customers:manage"
  | "team:manage"
  | "orders:manage"
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
  | "analytics:business:read"
  | "admin:users:read"
  | "admin:users:manage"
  | "admin:orgs:read"
  | "audit:view"
  | "revenue:read"
  | "revenue:manage"
  | "revenue:refund"

export const ALL_PERMISSIONS: readonly Permission[] = [
  "catalog:view",
  "customers:view",
  "catalog:manage",
  "approvals:approve",
  "payments:refund",
  "reports:view",
  "settings:manage",
  "conversations:view",
  "customers:manage",
  "team:manage",
  "orders:manage",
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
  "analytics:business:read",
  "admin:users:read",
  "admin:users:manage",
  "admin:orgs:read",
  "audit:view",
  "revenue:read",
  "revenue:manage",
  "revenue:refund",
]

/**
 * The permissions `admin` is deliberately denied, mirroring `Permissions.AdminDeniedPermissions`
 * (`Permissions.cs`, the `PermissionsDeniedToAdmin` array).
 *
 * `pricing:backdate` is the original: the console gates the recompute control on it precisely so
 * an admin sees it disabled with a stated reason (slice A6). `revenue:refund` joins it because
 * sending money back is irreversible in a way that correcting the ledger is not. Both are
 * deliberate denials, which is why they are named rather than derived from a hand-written filter
 * the server cannot check.
 */
const ADMIN_DENIED_PERMISSIONS: readonly Permission[] = ["pricing:backdate", "revenue:refund"]

/**
 * Canonical Role-to-Permissions mapping from `Permissions.cs`.
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
    "analytics:business:read",
    "revenue:read",
  ],
  admin: ALL_PERMISSIONS.filter((p) => !ADMIN_DENIED_PERMISSIONS.includes(p)),
  owner: ALL_PERMISSIONS,

  "org:boutique_staff": [
    "catalog:view",
    "customers:view",
    "conversations:view",
    "billing:view:self",
    "approvals:approve",
  ],
  "org:boutique_manager": [
    "catalog:view",
    "customers:view",
    "catalog:manage",
    "customers:manage",
    "team:manage",
    "orders:manage",
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
    "customers:manage",
    "team:manage",
    "orders:manage",
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
    "customers:manage",
    "team:manage",
    "orders:manage",
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
