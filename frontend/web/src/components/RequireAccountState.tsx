import { useUser } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

import { isAdminSignUp } from '@/lib/admin-signup'
import { useUserContext } from '@/contexts/UserContext'

import { PageLoader } from './PageLoader'

/**
 * Route guard keyed on the account lifecycle state:
 * - `Suspended`        → `/suspended`
 * - `OnboardingPending` → administrator sign-ups wait at `/admin/pending`;
 *                         everyone else profiles first (`/onboarding`)
 * - `Active`           → business route tree
 */
export function RequireAccountState() {
  const { accountState, isLoading } = useUserContext()
  const { user, isLoaded: userLoaded } = useUser()

  if (isLoading || accountState === null || !userLoaded) {
    return <PageLoader />
  }

  if (accountState === 'Suspended') {
    return <Navigate to="/suspended" replace />
  }

  if (accountState === 'OnboardingPending') {
    // Administrator sign-ups are not boutique tenants: they wait for review
    // instead of being pushed through the onboarding wizard.
    return <Navigate to={isAdminSignUp(user) ? '/admin/pending' : '/onboarding'} replace />
  }

  return <Outlet />
}
