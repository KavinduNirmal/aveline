import { useEffect, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'

import { PageLoader } from '@/components/PageLoader'
import { useUserContext } from '@/contexts/UserContext'
import { hasConsoleRole } from '@/lib/admin-signup'
import { fetchMyOrganizations } from '@/lib/organizations'

type RedirectState = 'loading' | 'missing' | 'redirecting'

/**
 * Resolves the caller's dashboard.
 *
 * **Aveline-team operators have no boutique**, so the tenant resolution below could only ever
 * send them to `/forbidden`. Anyone holding a console role goes to the administrator console
 * instead; everyone else keeps the existing behaviour of resolving their first active boutique
 * membership and landing at `/app/b/{slug}`.
 *
 * This is also the fix for every `/app` link in the product — the site navigation, the
 * signed-in redirect and the in-shell "Back to your dashboard" button all resolve here.
 */
export function DashboardRedirect() {
  const navigate = useNavigate()
  const { user, isLoading } = useUserContext()
  const [state, setState] = useState<RedirectState>('loading')

  const isOperator = hasConsoleRole(user !== null ? [user.userRole] : [])

  useEffect(() => {
    if (isLoading || user === null) return
    // The console is this user's dashboard; there is nothing to resolve.
    if (isOperator) return

    let cancelled = false
    async function resolve() {
      try {
        const memberships = await fetchMyOrganizations()
        if (cancelled) return
        const active = memberships.find((m) => m.status === 'Active' && m.slug)
        if (!cancelled) {
          if (active?.slug) {
            setState('redirecting')
            navigate(`/app/b/${active.slug}`, { replace: true })
          } else {
            setState('missing')
          }
        }
      } catch {
        if (!cancelled) setState('missing')
      }
    }
    void resolve()
    return () => {
      cancelled = true
    }
  }, [isLoading, isOperator, navigate, user])

  if (isLoading || user === null) {
    return <PageLoader />
  }

  if (isOperator) {
    return <Navigate to={`/admin/${user.id}/dashboard`} replace />
  }

  if (state === 'missing') {
    return <Navigate to="/forbidden" replace />
  }

  return <PageLoader />
}
