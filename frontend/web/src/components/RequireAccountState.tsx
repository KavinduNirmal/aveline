import { useUser } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

import { isAdminSignUp, isOnboardingExempt } from '@/lib/admin-signup'
import { useUserContext } from '@/contexts/UserContext'

import { PageLoader } from './PageLoader'

/**
 * Route guard keyed on the account lifecycle state:
 * - `Suspended`        → `/suspended`
 * - `OnboardingPending` → administrator sign-ups wait at `/admin/pending`;
 *                         everyone else profiles first (`/onboarding`)
 * - `Active`           → business route tree
 *
 * Console roles (`owner`, `admin`, `moderator`) are exempt from the pending branch because the
 * API exempts them too (`Roles.OnboardingExemptRoles`): the Aveline team never completes tenant
 * onboarding, so their `accountState` stays `OnboardingPending` by design. Bouncing them here is
 * what produced the `/admin/pending` ↔ `/admin` redirect loop — `AdminPendingPage` sees the
 * console role and forwards to `/admin`, this guard saw `OnboardingPending` and sent them back.
 */
export function RequireAccountState() {
  const { accountState, isLoading, user } = useUserContext()
  const { user: clerkUser, isLoaded: userLoaded } = useUser()

  if (isLoading || accountState === null || !userLoaded) {
    return <PageLoader />
  }

  if (accountState === 'Suspended') {
    return <Navigate to="/suspended" replace />
  }

  if (accountState === 'OnboardingPending') {
    // Aveline team roles never go through tenant onboarding (see `Roles.cs`), so the gate does
    // not apply to them. A moderator is exempt here but still refused at the console door by
    // `AdminRouteGuard`, which is a stated refusal rather than a redirect loop.
    if (isOnboardingExempt(user !== null ? [user.userRole] : [])) {
      return <Outlet />
    }

    // Administrator sign-ups are not boutique tenants: they wait for review
    // instead of being pushed through the onboarding wizard. Only an account holding no
    // console role reaches here — a role holder was admitted by the branch above.
    return <Navigate to={isAdminSignUp(clerkUser) ? '/admin/pending' : '/onboarding'} replace />
  }

  return <Outlet />
}
