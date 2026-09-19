/**
 * Helpers for the administrator sign-up flow.
 *
 * Administrator sign-up stamps `unsafeMetadata.accountType = "admin"` on the
 * Clerk user (see `AdminSignUpPage`). That metadata is the only signal available
 * before the request is approved, because the role claim
 * (`public_metadata.role`) is only granted on approval.
 */

/** Minimal shape of the Clerk user we depend on. */
export interface AdminSignUpUser {
  unsafeMetadata?: Record<string, unknown> | null
}

/** True when the user registered through the administrator sign-up flow. */
export function isAdminSignUp(user: AdminSignUpUser | null | undefined): boolean {
  return user?.unsafeMetadata?.accountType === 'admin'
}

/**
 * Roles admitted to the administrator console. Kept in sync with
 * `components/admin/AdminRouteGuard.tsx`.
 */
const CONSOLE_ROLES: readonly string[] = [
  'admin',
  'owner',
  'moderator',
  'org:boutique_owner',
  'org:boutique_manager',
  'org:boutique_supervisor',
]

/** True when any of the supplied roles may open the administrator console. */
export function hasConsoleRole(roles: readonly string[] | null | undefined): boolean {
  return (roles ?? []).some((role) => CONSOLE_ROLES.includes(role.toLowerCase()))
}
