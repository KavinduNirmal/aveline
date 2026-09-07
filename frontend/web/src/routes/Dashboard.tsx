import { useEffect, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'

import { PageLoader } from '@/components/PageLoader'
import { fetchMyOrganizations } from '@/lib/organizations'

type RedirectState = 'loading' | 'missing' | 'redirecting'

/**
 * Resolves the caller's first active boutique membership and redirects to its
 * tenant dashboard at `/app/b/{slug}`. Keeps legacy `/app` links (and any
 * `navigate('/app')` call sites) working after the tenant dashboard is introduced.
 */
export function DashboardRedirect() {
  const navigate = useNavigate()
  const [state, setState] = useState<RedirectState>('loading')

  useEffect(() => {
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
  }, [navigate])

  if (state === 'missing') {
    return <Navigate to="/forbidden" replace />
  }

  return <PageLoader />
}
