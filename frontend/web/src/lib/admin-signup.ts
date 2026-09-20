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
 * Roles admitted to the administrator console (strategy C2).
 *
 * The console is the Aveline team's internal tool. `moderator` holds only `admin:orgs:read`
 * among admin-scoped permissions — the `stats:view`-bearing surfaces it might want are
 * role-guarded to Owner/Admin — so admitting it would present a one-section panel. It is
 * refused at the door instead, and the reduced-moderator panel is deferred rather than built.
 *
 * `AdminRouteGuard` imports {@link hasConsoleRole} rather than re-listing roles, so this is the
 * single source of truth.
 */
export const CONSOLE_ROLES: readonly string[] = ['owner', 'admin']

/** True when any of the supplied roles may open the administrator console. */
export function hasConsoleRole(roles: readonly string[] | null | undefined): boolean {
  return (roles ?? []).some((role) => CONSOLE_ROLES.includes(role.toLowerCase()))
}
