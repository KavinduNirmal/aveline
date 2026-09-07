import { useEffect, useState } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router-dom'

import { DashboardShell } from '@/components/dashboard/DashboardShell'
import { PageLoader } from '@/components/PageLoader'
import { Button } from '@/components/ui/button'
import { isTenantAdmin } from '@/lib/permissions'
import {
  fetchOrganizationBySlug,
  fetchOrganizationUsage,
} from '@/lib/organizations'
import type {
  OrganizationProfileDto,
  OrganizationUsageSummary,
} from '@/types/organization'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'notFound' }
  | { kind: 'noAccess' }
  | { kind: 'error'; message: string }
  | {
      kind: 'ready'
      organization: OrganizationProfileDto
      usage: OrganizationUsageSummary | null
      role: string
    }

/**
 * Tenant dashboard at /app/b/{slug}: resolves the boutique by slug, enforces that the
 * caller holds a membership in it, and mounts the app shell.
 */
export function TenantDashboard() {
  const { slug } = useParams<'slug'>()
  const navigate = useNavigate()
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  useEffect(() => {
    if (!slug) {
      setState({ kind: 'notFound' })
      return
    }
    const targetSlug = slug
    let cancelled = false
    async function load() {
      try {
        const res = await fetchOrganizationBySlug(targetSlug)
        if (cancelled) return
        // Tenant isolation + dashboard access: only an ACTIVE, admin-capable membership
        // (owner/manager/supervisor) may open the dashboard. The role comes from the
        // canonical membership returned by the API — not the Clerk JWT claims.
        const role = res.membership?.boutiqueRole ?? ''
        if (
          !res.membership ||
          res.membership.status !== 'Active' ||
          !isTenantAdmin(role)
        ) {
          setState({ kind: 'noAccess' })
          return
        }
        let usage: OrganizationUsageSummary | null = null
        try {
          usage = await fetchOrganizationUsage(res.organization.id)
        } catch {
          usage = null // usage is best-effort; the overview tolerates its absence
        }
        if (!cancelled) {
          setState({
            kind: 'ready',
            organization: res.organization,
            usage,
            role,
          })
        }
      } catch (err: unknown) {
        if (cancelled) return
        const status =
          typeof err === 'object' && err !== null && 'status' in err
            ? (err as { status?: number }).status
            : undefined
        setState(status === 404 ? { kind: 'notFound' } : { kind: 'error', message: 'Unable to load this boutique.' })
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [slug])

  switch (state.kind) {
    case 'loading':
      return <PageLoader />
    case 'noAccess':
      return <Navigate to="/forbidden" replace />
    case 'notFound':
      return (
        <ShellFallback title="Boutique not found">
          <p className="text-sm text-muted-foreground">
            We couldn't find a boutique at <span className="font-mono">{slug}</span>. The link may be incorrect or the boutique no longer exists.
          </p>
          <Button variant="outline" onClick={() => navigate('/app')}>
            Back to your dashboard
          </Button>
        </ShellFallback>
      )
    case 'error':
      return (
        <ShellFallback title="Something went wrong">
          <p className="text-sm text-muted-foreground">{state.message}</p>
          <Button variant="outline" onClick={() => navigate('/app')}>
            Back to your dashboard
          </Button>
        </ShellFallback>
      )
    case 'ready':
      return (
        <DashboardShell
          organization={state.organization}
          usage={state.usage}
          role={state.role}
        />
      )
  }
}

function ShellFallback({
  title,
  children,
}: {
  title: string
  children: React.ReactNode
}) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <h1 className="font-serif text-3xl font-medium tracking-tight">{title}</h1>
      <div className="max-w-md">{children}</div>
    </div>
  )
}
