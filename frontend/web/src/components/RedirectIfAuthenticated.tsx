import { useAuth } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

import { PageLoader } from './PageLoader'

/**
 * Redirects already-authenticated users away from public auth pages (sign-in /
 * sign-up) to the app. Prevents a signed-in user from landing back on the
 * sign-in screen.
 */
export function RedirectIfAuthenticated() {
  const { isLoaded, isSignedIn } = useAuth()

  if (!isLoaded) {
    return <PageLoader />
  }

  if (isSignedIn) {
    return <Navigate to="/app" replace />
  }

  return <Outlet />
}
