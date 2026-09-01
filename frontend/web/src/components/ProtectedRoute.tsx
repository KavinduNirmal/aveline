import { useAuth } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

/** Guards a route tree: redirects unauthenticated users to the sign-in page. */
export function ProtectedRoute() {
  const { isLoaded, isSignedIn } = useAuth()

  if (!isLoaded) {
    return <div className="page-loader">Loading…</div>
  }

  if (!isSignedIn) {
    return <Navigate to="/sign-in" replace />
  }

  return <Outlet />
}
