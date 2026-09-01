import { useAuth } from '@clerk/react'
import { useEffect, useState } from 'react'
import { Navigate, Outlet } from 'react-router-dom'

import { type AvelineClaims, decodeJwtPayload, hasAdminRole } from '@/lib/auth'

import { PageLoader } from './PageLoader'

/** Clerk JWT template that mints the Aveline role claims. */
const JWT_TEMPLATE = 'jwt-aveline-v1'

/**
 * Guards a route tree for admin (owner/manager) users by inspecting the
 * `jwt-aveline-v1` claims. Non-admins are redirected to `/forbidden`.
 */
export function RequireAdmin() {
  const { isLoaded, isSignedIn, getToken } = useAuth()
  const [isAdmin, setIsAdmin] = useState<boolean | null>(null)

  useEffect(() => {
    let cancelled = false

    async function check() {
      const token = await getToken({ template: JWT_TEMPLATE })
      const claims = token
        ? (decodeJwtPayload(token) as AvelineClaims)
        : undefined
      if (!cancelled) {
        setIsAdmin(claims != null && hasAdminRole(claims))
      }
    }

    if (isSignedIn && isAdmin === null) {
      void check()
    }
    return () => {
      cancelled = true
    }
  }, [getToken, isAdmin, isSignedIn])

  if (!isLoaded || isAdmin === null) {
    return <PageLoader />
  }
  if (!isSignedIn) {
    return <Navigate to="/sign-in" replace />
  }
  if (!isAdmin) {
    return <Navigate to="/forbidden" replace />
  }

  return <Outlet />
}
