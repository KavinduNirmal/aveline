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

/**
 * Aveline team roles that never complete tenant onboarding, mirroring the backend's
 * `Roles.OnboardingExemptRoles` (`Aveline.Api/Authorization/Roles.cs`).
 *
 * The backend `OnboardingMiddleware` already waives the onboarding gate for these roles: a
 * user holding one may call the API while `accountState` is still `OnboardingPending`.
 * The frontend gate has to waive it too. When it does not, an administrator whose role was
 * granted without the account being activated (an approval, or a direct
 * `UPDATE "Users" SET "UserRole" = ...`) is admitted by one guard and refused by the other:
 * `AdminPendingPage` sees the console role and redirects to `/admin`, while
 * `RequireAccountState` sees `OnboardingPending` and redirects back to `/admin/pending`.
 * That pair is an infinite redirect loop, and each lap re-fetches `/auth/claims`.
 *
 * Deliberately wider than {@link CONSOLE_ROLES}: a `moderator` is onboarding-exempt but still
 * refused at the console door by `hasConsoleRole` (strategy C2), which is a stated refusal
 * rather than a redirect.
 */
export const ONBOARDING_EXEMPT_ROLES: readonly string[] = ['moderator', 'admin', 'owner']

/** True when any of the supplied roles is exempt from tenant onboarding. */
export function isOnboardingExempt(roles: readonly string[] | null | undefined): boolean {
  return (roles ?? []).some((role) => ONBOARDING_EXEMPT_ROLES.includes(role.toLowerCase()))
}
