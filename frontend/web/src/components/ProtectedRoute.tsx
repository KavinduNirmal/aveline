import { useAuth } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

import { PageLoader } from './PageLoader'

/** Guards a route tree: redirects unauthenticated users to the sign-in page. */
export function ProtectedRoute() {
  const { isLoaded, isSignedIn } = useAuth()

  if (!isLoaded) {
    return <PageLoader />
  }

  if (!isSignedIn) {
    return <Navigate to="/sign-in" replace />
  }

  return <Outlet />
}
