import { useCallback, useEffect, useState } from 'react'

import { AlertList } from '@/components/admin/system/AlertList'
import { AlertAcknowledgeDialog } from '@/components/admin/system/AlertAcknowledgeDialog'
import { GrafanaLink } from '@/components/admin/system/GrafanaLink'
import { OmittedMetrics } from '@/components/admin/system/OmittedMetrics'
import { ReadinessTable } from '@/components/admin/system/ReadinessTable'
import { KpiTile } from '@/components/admin/charts/KpiTile'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

import { acknowledgeAlert, fetchSystemAlerts, fetchSystemOverview } from '@/lib/admin/api'
import type { SystemAlertAckResponse, SystemAlertDto, SystemOverview } from '@/types/admin'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; overview: SystemOverview; alerts: SystemAlertDto[] }
  | { kind: 'error'; message: string }

/**
 * The system page.
 *
 * There is **no fallback**: a failed overview renders the failure. The delivered version
 * substituted a fully fabricated healthy system on any error (`readiness.status: "Healthy"`,
 * three invented probe rows, `uptimeSeconds: 84200`, `requestsPerSecond: 12.4`). That is the
 * defect this page exists to remove.
 *
 * The alert list is read with `status=Firing` **explicitly**, so a `Resolved` row can never be
 * counted as active, and the acknowledgement patches the row it came from **by `id`** while
 * reading only `status`/`acknowledgedAt` — the acknowledge response is the entity and has no
 * `ruleName` (`SystemStatisticsEndpoints.cs:140`).
 */
export function AdminSystemView() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [pending, setPending] = useState<SystemAlertDto | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)

  const loadData = useCallback(async () => {
    setState({ kind: 'loading' })
    try {
      const [overview, alertPage] = await Promise.all([
        fetchSystemOverview(),
        // `Firing` is passed explicitly: omitting it would let `Resolved` rows inflate the count.
        fetchSystemAlerts({ status: 'Firing' }),
      ])
      setState({ kind: 'ready', overview, alerts: alertPage.items })
    } catch (err: unknown) {
      setState({
        kind: 'error',
        message: err instanceof Error ? err.message : 'Unknown error',
      })
    }
  }, [])

  useEffect(() => {
    void loadData()
  }, [loadData])

  const onAcknowledged = useCallback((response: SystemAlertAckResponse) => {
    setState((previous) => {
      if (previous.kind !== 'ready') return previous
      return {
        ...previous,
        // Patched strictly by `id`. Only the two fields the entity genuinely owns are read, so
        // the row keeps its list-row `ruleName` label.
        alerts: previous.alerts.map((alert) =>
          alert.id === response.id
            ? { ...alert, status: response.status, acknowledgedAt: response.acknowledgedAt }
            : alert,
        ),
      }
    })
  }, [])

  if (state.kind === 'loading') {
    return <p className="text-sm text-muted-foreground">Loading system telemetry…</p>
  }

  if (state.kind === 'error') {
    return (
      <Card className="max-w-xl border-destructive/30 shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">
            System overview could not be loaded
          </CardTitle>
          <CardDescription className="text-xs">
            {state.message}. Nothing is shown rather than a substitute system.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button variant="outline" size="sm" className="text-xs" onClick={() => void loadData()}>
            Retry
          </Button>
        </CardContent>
      </Card>
    )
  }

  const { overview, alerts } = state
  const omitted = Array.from(
    new Set([
      ...overview.omitted,
      ...overview.errors.omitted,
      ...overview.throughput.omitted,
      ...overview.queues.omitted,
    ]),
  )

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            System health &amp; alerts
          </h2>
          <p className="text-sm text-muted-foreground">
            Dependency readiness, throughput and error rate, and the alerts currently firing.
          </p>
        </div>
        <Button variant="outline" size="sm" className="text-xs" onClick={() => void loadData()}>
          Refresh
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <KpiTile
          label="Uptime"
          value={Math.round(overview.uptimeSeconds / 60)}
          unit="raw"
          source="GET /admin/statistics/system/overview → uptimeSeconds (minutes)"
        />
        <KpiTile
          label="Error rate"
          value={overview.errors.errorRate}
          unit="percent"
          source={`GET /admin/statistics/system/overview → errors.errorRate (${overview.errors.windowSize})`}
        />
        <KpiTile
          label="Requests / sec"
          value={overview.throughput.requestsPerSecond}
          unit="raw"
          source="GET /admin/statistics/system/overview → throughput.requestsPerSecond"
        />
        <KpiTile
          label="Firing alerts"
          value={alerts.length}
          unit="count"
          source="GET /admin/statistics/system/alerts?status=Firing"
        />
      </div>

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Dependency readiness</CardTitle>
          <CardDescription className="text-xs">
            {overview.readiness.checks.length} probe
            {overview.readiness.checks.length === 1 ? '' : 's'} reported by the API process
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ReadinessTable readiness={overview.readiness} />
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Firing alerts</CardTitle>
          <CardDescription className="text-xs">
            Queried with <span className="font-mono">status=Firing</span>, so resolved rows are
            never counted as active.
          </CardDescription>
        </CardHeader>
        <CardContent className="px-0">
          <AlertList
            alerts={alerts}
            onAcknowledge={(alert) => {
              setPending(alert)
              setDialogOpen(true)
            }}
          />
        </CardContent>
      </Card>

      <OmittedMetrics omitted={omitted} />

      <GrafanaLink dashboard="overview" label="Platform metrics — Grafana" />

      <AlertAcknowledgeDialog
        alert={pending}
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        onAcknowledged={onAcknowledged}
        acknowledge={acknowledgeAlert}
      />
    </div>
  )
}
