import { useUser } from '@clerk/react'
import { Navigate, Outlet } from 'react-router-dom'

import { isAdminSignUp } from '@/lib/admin-signup'

import { PageLoader } from './PageLoader'

/**
 * Keeps administrator sign-ups out of the tenant onboarding wizard. They are not
 * boutique tenants, so they wait at `/admin/pending` for review instead.
 */
export function RedirectAdminSignUps() {
  const { user, isLoaded } = useUser()

  if (!isLoaded) {
    return <PageLoader />
  }

  if (isAdminSignUp(user)) {
    return <Navigate to="/admin/pending" replace />
  }

  return <Outlet />
}
