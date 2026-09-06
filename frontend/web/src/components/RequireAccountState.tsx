import { Navigate, Outlet } from 'react-router-dom'

import { useUserContext } from '@/contexts/UserContext'

import { PageLoader } from './PageLoader'

/**
 * Route guard keyed on the account lifecycle state:
 * - `Suspended`        → `/suspended`
 * - `OnboardingPending` → profile first (`/onboarding`), then org setup (`/org-setup`)
 * - `Active`           → business route tree
 */
export function RequireAccountState() {
  const { user, accountState, isLoading } = useUserContext()

  if (isLoading || accountState === null) {
    return <PageLoader />
  }

  if (accountState === 'Suspended') {
    return <Navigate to="/suspended" replace />
  }

  if (accountState === 'OnboardingPending') {
    const needsProfile = user == null || !user.hasCompletedOnboarding
    return <Navigate to={needsProfile ? '/onboarding' : '/org-setup'} replace />
  }

  return <Outlet />
}
