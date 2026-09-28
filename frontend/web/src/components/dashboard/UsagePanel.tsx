import { useCallback, useEffect, useState } from 'react'
import { ArrowUpRight } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { resolveWindowRange } from '@/hooks/useDashboardWindow'
import type { DashboardWindow } from '@/lib/dashboard-api'
import { hasPermission } from '@/lib/permissions'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import {
  fetchBlossomBalance,
  fetchBlossomUsage,
  fetchBurnRate,
  fetchEntitlementUsage,
  type BlossomBalance,
  type BlossomUsage,
  type BurnRate,
  type EntitlementUsage,
} from '@/lib/billing-api'
import {
  fetchApiLatency,
  fetchApiQuota,
  fetchApiRequests,
  type ApiLatency,
  type ApiQuotaStatus,
  type ApiRequestCount,
} from '@/lib/statistics-api'
import type { OrganizationProfileDto } from '@/types/organization'

import { ApiConsumptionPanel } from './usage/ApiConsumptionPanel'
import { BlossomBurnChart } from './usage/BlossomBurnChart'
import { BurnRatePanel } from './usage/BurnRatePanel'
import { UsageBalanceCard } from './usage/UsageBalanceCard'
import { UsageVsLimitsTable } from './usage/UsageVsLimitsTable'

interface UsagePanelProps {
  organization: OrganizationProfileDto
  role: string
  /** The shell's one window, so this section and the KPI strip describe the same period. */
  window: DashboardWindow
  /** Moves to the upgrade page. The move itself is the shell's; this only asks for it. */
  onUpgrade: () => void
}

/** What a role may read here, decided once rather than sprinkled through the render. */
interface UsageAccess {
  balance: boolean
  billing: boolean
  statistics: boolean
}

function accessFor(role: string): UsageAccess {
  return {
    // Every boutique role holds `billing:view:self`, so the balance is the universal panel.
    balance: hasPermission(role, 'billing:view:self'),
    // The richer panels (burn rate, entitlements-vs-usage, the statement) are `billing:view`.
    billing: hasPermission(role, 'billing:view'),
    // API consumption is `stats:view`. The agentic family is deliberately absent: a boutique
    // reads its usage in Blossoms, and the token/cost routes are not mounted for an organisation.
    statistics: hasPermission(role, 'stats:view'),
  }
}

interface UsageData {
  balance: BlossomBalance | null
  burnRate: BurnRate | null
  entitlements: EntitlementUsage | null
  blossomUsage: BlossomUsage | null
  requests: ApiRequestCount | null
  latency: ApiLatency | null
  quota: ApiQuotaStatus | null
}

const EMPTY_DATA: UsageData = {
  balance: null,
  burnRate: null,
  entitlements: null,
  blossomUsage: null,
  requests: null,
  latency: null,
  quota: null,
}

/**
 * The Usage section: the Blossom position every role may see, the richer billing detail for a role
 * holding `billing:view`, and API consumption behind `stats:view`.
 *
 * The Blossom is the boutique's usage unit. There is deliberately **no agentic panel**: runs,
 * tokens and provider cost are agent internals, and the org-scoped agent statistics routes are not
 * mounted for a tenant at all (the team-only `/admin/statistics/agents/**` subset is where they
 * live). A panel nobody may read is not fetched and not rendered.
 */
export function UsagePanel({ organization, role, window, onUpgrade }: UsagePanelProps) {
  const access = accessFor(role)
  const [data, setData] = useState<UsageData>(EMPTY_DATA)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  const load = usePanelLoad(
    async (signal?: AbortSignal) => {
      const range = resolveWindowRange(window)

      const balance = access.balance ? await fetchBlossomBalance(organization.id, signal) : null

      const [burnRate, entitlements, blossomUsage] = access.billing
          ? await Promise.all([
            fetchBurnRate(organization.id, signal),
            fetchEntitlementUsage(organization.id, signal),
            fetchBlossomUsage(organization.id, range, signal),
          ])
          : [null, null, null]

      const [requests, latency, quota] = access.statistics
          ? await Promise.all([
            fetchApiRequests(organization.id, range, signal),
            fetchApiLatency(organization.id, range, signal),
            fetchApiQuota(organization.id, signal),
          ])
          : [null, null, null]

      return {
        balance,
        burnRate,
        entitlements,
        blossomUsage,
        requests,
        latency,
        quota,
      }
    },
    (next) => setData(next),
    () => {
      // Clear rather than keep the previous window's figures under a new window: stale numbers
      // labelled with today's dates are worse than an error.
      setData(EMPTY_DATA)
      setError('Could not load the usage data.')
    },
    [access.balance, access.billing, access.statistics, organization.id, window],
  )

  const runLoad = useCallback(
    async (signal?: AbortSignal) => {
      setIsLoading(true)
      setError(null)
      await load(signal)
      setIsLoading(false)
    },
    [load],
  )

  useEffect(() => {
    const controller = new AbortController()
    void runLoad(controller.signal)
    return () => controller.abort()
  }, [runLoad])

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Usage</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            How this boutique is spending Blossoms and using its plan. A figure the server did not
            measure reads &quot;not measured&quot; rather than zero.
          </p>
        </div>
        {/* Outgrowing the plan is the reason to read this page, so the way out lives here. */}
        <Button type="button" onClick={onUpgrade} className="gap-1.5">
          <ArrowUpRight className="size-4" aria-hidden />
          Upgrade plan
        </Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-3">
          <Skeleton className="h-44 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : error ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3 pt-6">
            <p className="text-sm text-destructive">{error}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
              Try again
            </Button>
          </CardContent>
        </Card>
      ) : (
        <>
          {data.balance ? (
            <UsageBalanceCard balance={data.balance} />
          ) : (
            <Card>
              <CardContent className="pt-6">
                <p className="text-sm text-muted-foreground">
                  Balance unavailable — the server did not report one for this period, so no
                  percentage is shown rather than a fabricated one.
                </p>
              </CardContent>
            </Card>
          )}

          {data.entitlements ? <UsageVsLimitsTable usage={data.entitlements} /> : null}

          {data.burnRate ? <BurnRatePanel burnRate={data.burnRate} /> : null}

          {data.blossomUsage ? (
            <BlossomBurnChart
              usage={data.blossomUsage}
              allowance={data.balance?.monthlyBlossomLimit ?? null}
            />
          ) : null}

          {access.statistics && data.requests && data.latency && data.quota ? (
            <ApiConsumptionPanel
              requests={data.requests}
              latency={data.latency}
              quota={data.quota}
            />
          ) : null}

          {!access.billing ? (
            <p className="text-xs text-muted-foreground">
              Detailed billing panels are available to a manager or owner.
            </p>
          ) : null}
        </>
      )}
    </div>
  )
}
