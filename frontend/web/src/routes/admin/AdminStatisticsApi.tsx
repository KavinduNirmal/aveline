import { useCallback, useEffect, useState } from 'react'

import { KpiTile } from '@/components/admin/charts/KpiTile'
import { DataQualityNotice } from '@/components/admin/charts/DataQualityNotice'
import { AlertList } from '@/components/admin/system/AlertList'
import { GrafanaLink } from '@/components/admin/system/GrafanaLink'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

import { fetchSystemAlerts, fetchSystemOverview } from '@/lib/admin/api'
import { systemDataQuality } from '@/lib/admin/data-quality'
import type { SystemAlertDto, SystemOverview } from '@/types/admin'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; overview: SystemOverview; alerts: SystemAlertDto[] }
  | { kind: 'error'; message: string }

/**
 * The API statistics family.
 *
 * The API family's three booleans (`rollupComplete`, `rawLogSampled`, `latencyBuckets`) live on
 * the tenant API-statistics endpoint, which is **not** on the console's client surface; the
 * console therefore shows the **system** family's `omitted[]` verbatim rather than inventing a
 * placeholder for a vocabulary it cannot read. The honesty rule is the point: name the gap.
 *
 * The firing-alert list is read with `status=Firing` explicitly, so a `Resolved` row is never
 * counted as active.
 */
export function AdminStatisticsApiView() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  const load = useCallback(async () => {
    setState({ kind: 'loading' })
    try {
      const [overview, alertPage] = await Promise.all([
        fetchSystemOverview(),
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
    void load()
  }, [load])

  if (state.kind === 'loading') {
    return <p className="text-sm text-muted-foreground">Loading API statistics…</p>
  }

  if (state.kind === 'error') {
    return (
      <Card className="border-destructive/30 shadow-xs max-w-xl">
        <CardHeader>
          <CardTitle className="font-serif text-base">
            API statistics could not be loaded
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

  const { overview, alerts } = state
  const report = systemDataQuality({
    omitted: overview.errors.omitted,
    dataQuality: undefined,
  })

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            API statistics
          </h2>
          <p className="text-sm text-muted-foreground">
            Request volume, error rate and the API alerts currently firing. A metric the server
            omits is named here, never rendered as a zero.
          </p>
        </div>
        <Button variant="outline" size="sm" className="text-xs" onClick={() => void load()}>
          Refresh
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <KpiTile
          label="Requests / sec"
          value={overview.throughput.requestsPerSecond}
          unit="raw"
          source="GET /admin/statistics/system/overview → throughput.requestsPerSecond"
        />
        <KpiTile
          label="Error rate"
          value={overview.errors.errorRate}
          unit="percent"
          source={`GET /admin/statistics/system/overview → errors.errorRate (${overview.errors.windowSize})`}
        />
        <KpiTile
          label="Requests"
          value={overview.errors.requestCount}
          unit="count"
          source="GET /admin/statistics/system/overview → errors.requestCount"
        />
        <KpiTile
          label="Errors"
          value={overview.errors.errorCount}
          unit="count"
          source="GET /admin/statistics/system/overview → errors.errorCount"
        />
      </div>

      <DataQualityNotice report={report} title="API data quality" />

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Firing API alerts</CardTitle>
          <CardDescription className="text-xs">
            Queried with <span className="font-mono">status=Firing</span>, so resolved rows are
            never counted as active.
          </CardDescription>
        </CardHeader>
        <CardContent className="px-0">
          <AlertList alerts={alerts} onAcknowledge={() => undefined} />
        </CardContent>
      </Card>

      <GrafanaLink dashboard="business" label="API latency and error series — Grafana" />
    </div>
  )
}
