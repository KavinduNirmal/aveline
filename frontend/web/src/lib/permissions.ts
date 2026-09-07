/**
 * Client-side mirror of `Aveline.Api/Authorization/Permissions.cs`. Used to gate which
 * dashboard sections a boutique role may view. Authoritative enforcement always happens
 * server-side via the org-scoped policies; this only prevents presenting options the
 * backend would reject with 403.
 */

export type Permission =
  | 'catalog:view'
  | 'customers:view'
  | 'catalog:manage'
  | 'approvals:approve'
  | 'payments:refund'
  | 'reports:view'
  | 'settings:manage'

const ALL: readonly Permission[] = [
  'catalog:view',
  'customers:view',
  'catalog:manage',
  'approvals:approve',
  'payments:refund',
  'reports:view',
  'settings:manage',
]

const ROLE_PERMISSIONS: Record<string, readonly Permission[]> = {
  // Boutique (org-scoped) roles — these are what `membership.boutiqueRole` carries.
  'org:boutique_staff': ['catalog:view', 'customers:view'],
  'org:boutique_manager': ['catalog:view', 'customers:view', 'catalog:manage', 'reports:view'],
  'org:boutique_supervisor': [
    'catalog:view',
    'customers:view',
    'catalog:manage',
    'approvals:approve',
    'reports:view',
  ],
  'org:boutique_owner': ALL,
  'org:principal': ALL,
  // Aveline team roles (used only if a non-boutique role slips through).
  staff: ['catalog:view'],
  customer_relations: ['catalog:view', 'customers:view'],
  moderator: ['catalog:view', 'customers:view', 'approvals:approve'],
  admin: ALL,
  owner: ALL,
}

/** Whether a boutique role holds a given permission. */
export function hasPermission(
  role: string | undefined | null,
  permission: Permission,
): boolean {
  if (!role) return false
  return (ROLE_PERMISSIONS[role] ?? []).includes(permission)
}

/**
 * Roles admitted to the tenant dashboard (matching active boutique memberships).
 * Evaluated against the authoritative boutique membership role returned by the API.
 */
const TENANT_ADMIN_ROLES = new Set([
  'org:boutique_owner',
  'org:boutique_manager',
  'org:boutique_supervisor',
  'org:boutique_staff',
  'org:principal',
  'owner',
  'admin',
  'moderator',
  'staff',
  'customer_relations',
])

/** Whether a boutique membership role may open the tenant dashboard. */
export function isTenantAdmin(role: string | undefined | null): boolean {
  return !!role && TENANT_ADMIN_ROLES.has(role)
}

