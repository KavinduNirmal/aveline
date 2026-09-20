import { useCallback, useEffect, useState } from 'react'

import { KpiTile } from '@/components/admin/charts/KpiTile'
import { DataQualityNotice } from '@/components/admin/charts/DataQualityNotice'
import { GrafanaLink } from '@/components/admin/system/GrafanaLink'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

import { fetchAgentOverview } from '@/lib/admin/api'
import { agentDataQuality } from '@/lib/admin/data-quality'
import type { AgentOverviewDto } from '@/types/admin'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; overview: AgentOverviewDto }
  | { kind: 'error'; message: string }

/**
 * The agent statistics family.
 *
 * The agent family has its **own** data-quality vocabulary — five instrumentation booleans
 * (`AgentDataQualityDto`). It must not be rendered with the system family's `omitted[]` wording,
 * and `totalRuns === 0` is a **different message** from a false flag: *"no runs recorded yet"*
 * is not *"not instrumented"*.
 */
export function AdminStatisticsAgentsView() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  const load = useCallback(async () => {
    setState({ kind: 'loading' })
    try {
      setState({ kind: 'ready', overview: await fetchAgentOverview() })
    } catch (err: unknown) {
      setState({
        kind: 'error',
        message: err instanceof Error ? err.message : 'Unknown error',
      })
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  if (state.kind === 'loading') {
    return <p className="text-sm text-muted-foreground">Loading agent statistics…</p>
  }

  if (state.kind === 'error') {
    return (
      <Card className="border-destructive/30 shadow-xs max-w-xl">
        <CardHeader>
          <CardTitle className="font-serif text-base">
            Agent statistics could not be loaded
          </CardTitle>
          <CardDescription className="text-xs">
            {state.message}. Nothing is shown rather than a substitute.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button variant="outline" size="sm" className="text-xs" onClick={() => void load()}>
            Retry
          </Button>
        </CardContent>
      </Card>
    )
  }

  const { overview } = state
  const report = agentDataQuality({
    totalRuns: overview.totalRuns,
    latencyInstrumented: overview.dataQuality.latencyInstrumented,
    nodeFailuresObserved: overview.dataQuality.nodeFailuresObserved,
    perStepAttribution: overview.dataQuality.perStepAttribution,
    toolInstrumented: overview.dataQuality.toolInstrumented,
    costInstrumented: overview.dataQuality.costInstrumented,
  })

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Agent statistics
          </h2>
          <p className="text-sm text-muted-foreground">
            Run volume and reliability. Every instrumentation gap is named by the agent family's
            own vocabulary; a zero run count is reported as such, not as an instrumented zero.
          </p>
        </div>
        <Button variant="outline" size="sm" className="text-xs" onClick={() => void load()}>
          Refresh
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <KpiTile
          label="Total runs"
          value={overview.totalRuns}
          unit="count"
          source="GET /admin/statistics/agents/overview → totalRuns"
        />
        <KpiTile
          label="Running"
          value={overview.running}
          unit="count"
          source="GET /admin/statistics/agents/overview → running"
        />
        <KpiTile
          label="Paused for approval"
          value={overview.pausedForApproval}
          unit="count"
          source="GET /admin/statistics/agents/overview → pausedForApproval"
        />
        <KpiTile
          label="Success rate"
          value={overview.successRate}
          unit="percent"
          source={`GET /admin/statistics/agents/overview → successRate (${overview.succeeded} succeeded, ${overview.failed} failed)`}
        />
      </div>

      <DataQualityNotice report={report} tone={overview.totalRuns === 0 ? 'info' : 'critical'} />

      <GrafanaLink
        dashboard="business"
        label="Agent run time series — Grafana"
      />
    </div>
  )
}
