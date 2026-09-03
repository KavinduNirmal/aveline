import { Navigate, Outlet } from 'react-router-dom'

import { useUserContext } from '@/contexts/UserContext'

import { PageLoader } from './PageLoader'

/**
 * Route guard that ensures the authenticated user has completed their profile onboarding.
 * If onboarding is pending, redirects to `/onboarding`.
 */
export function RequireOnboarding() {
  const { isOnboarded, isLoading } = useUserContext()

  if (isLoading || isOnboarded === null) {
    return <PageLoader />
  }

  if (!isOnboarded) {
    return <Navigate to="/onboarding" replace />
  }

  return <Outlet />
}
