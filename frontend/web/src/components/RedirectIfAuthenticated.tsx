import { useAuth, useUser } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

import { isAdminSignUp } from '@/lib/admin-signup'

import { PageLoader } from './PageLoader'

/**
 * Redirects already-authenticated users away from public auth pages (sign-in /
 * sign-up) to the app. Administrator sign-ups are sent to the pending-review
 * screen instead, since they are not boutique tenants.
 */
export function RedirectIfAuthenticated() {
  const { isLoaded, isSignedIn } = useAuth()
  const { user, isLoaded: userLoaded } = useUser()

  if (!isLoaded || !userLoaded) {
    return <PageLoader />
  }

  if (isSignedIn) {
    return <Navigate to={isAdminSignUp(user) ? '/admin/pending' : '/app'} replace />
  }

  return <Outlet />
}
