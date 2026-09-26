/**
 * Client-side mirror of `Aveline.Api/Authorization/Permissions.cs`. Used to gate which
 * dashboard sections a boutique role may view. Authoritative enforcement always happens
 * server-side via the org-scoped policies; this only prevents presenting options the
 * backend would reject with 403.
 *
 * The mirror is kept honest by `permissions.sync.test.ts`, which parses the C# source with
 * `src/test/permissions-catalog.ts` — the same parser the admin mirror uses. A permission
 * added to `Permissions.cs` without being added here fails that test.
 *
 * Only the four canonical `org:boutique_*` roles appear. Every membership carries one of
 * them (the Clerk webhook maps anything unrecognised to `org:boutique_staff`) and the
 * org-scoped policies resolve the membership's `BoutiqueRole`, so team-role branches could
 * never be reached through a tenant route. The former `org:principal` branch was a phantom
 * role that existed in no backend grant set.
 */

export const ALL_PERMISSIONS = [
  'catalog:view',
  'customers:view',
  'catalog:manage',
  'approvals:approve',
  'payments:refund',
  'reports:view',
  'settings:manage',
  'conversations:view',
  'customers:manage',
  'team:manage',
  'orders:manage',
  'billing:view',
  'billing:view:self',
  'billing:manage',
  'billing:adjust',
  'pricing:view',
  'pricing:manage',
  'pricing:backdate',
  'apikeys:view',
  'apikeys:manage',
  'stats:view',
  'stats:view:agent',
  'stats:system',
  'analytics:business:read',
  'admin:users:read',
  'admin:users:manage',
  'admin:orgs:read',
  'audit:view',
  'revenue:read',
  'revenue:manage',
  'revenue:refund',
] as const

export type Permission = (typeof ALL_PERMISSIONS)[number]

/**
 * The canonical role-to-permission grants, transcribed from `Permissions.cs` and checked
 * against it by `permissions.sync.test.ts`.
 */
export const ROLE_PERMISSIONS: Record<string, readonly Permission[]> = {
  'org:boutique_staff': [
    'catalog:view',
    'customers:view',
    'conversations:view',
    'billing:view:self',
    'approvals:approve',
  ],
  'org:boutique_manager': [
    'catalog:view',
    'customers:view',
    'catalog:manage',
    'customers:manage',
    'team:manage',
    'orders:manage',
    'reports:view',
    'conversations:view',
    'billing:view',
    'billing:view:self',
    'pricing:view',
    'stats:view',
  ],
  'org:boutique_supervisor': [
    'catalog:view',
    'customers:view',
    'catalog:manage',
    'customers:manage',
    'team:manage',
    'orders:manage',
    'approvals:approve',
    'reports:view',
    'conversations:view',
    'billing:view:self',
    'stats:view',
  ],
  'org:boutique_owner': [
    'catalog:view',
    'customers:view',
    'catalog:manage',
    'customers:manage',
    'team:manage',
    'orders:manage',
    'approvals:approve',
    'payments:refund',
    'reports:view',
    'settings:manage',
    'conversations:view',
    'billing:view',
    'billing:view:self',
    'billing:manage',
    'pricing:view',
    'apikeys:view',
    'apikeys:manage',
    'stats:view',
  ],
}

/** The four canonical boutique roles, in catalog order. */
export const BOUTIQUE_ROLES = [
  'org:boutique_staff',
  'org:boutique_manager',
  'org:boutique_supervisor',
  'org:boutique_owner',
] as const

/** Whether a boutique role holds a given permission. */
export function hasPermission(
  role: string | undefined | null,
  permission: Permission,
): boolean {
  if (!role) return false
  return (ROLE_PERMISSIONS[role] ?? []).includes(permission)
}

/**
 * Whether a boutique membership role may open the tenant dashboard.
 *
 * Derived from the presence of any org-scoped permission rather than a hand-listed set of role
 * strings, so it cannot drift from `Permissions.cs` the way the old `isTenantAdmin` did: a role
 * is admitted because the backend grants it something, not because someone remembered to add it
 * to a list here.
 */
export function canOpenTenantDashboard(role: string | undefined | null): boolean {
  return !!role && (ROLE_PERMISSIONS[role]?.length ?? 0) > 0
}
