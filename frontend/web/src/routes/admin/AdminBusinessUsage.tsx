import { useCallback, useEffect, useMemo, useState } from 'react'

import { ChartFrame } from '@/components/admin/charts/ChartFrame'
import { DataQualityNotice } from '@/components/admin/charts/DataQualityNotice'
import { KpiTile } from '@/components/admin/charts/KpiTile'
import { TimeSeriesChart } from '@/components/admin/charts/TimeSeriesChart'
import { DataTable, type Column } from '@/components/admin/data/DataTable'
import { RangePresets, presetGranularity, presetWindow, type RangePreset } from '@/components/admin/kpi/RangePresets'
import { OrgPicker } from '@/components/admin/orgs/OrgPicker'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import type { ChartConfig } from '@/components/ui/chart'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

import { fetchBusinessOrganizationUsage, fetchBusinessUsage } from '@/lib/admin/api'
import { buildBusinessAxis, toSeries } from '@/lib/admin/business-series'
import { businessDataQuality } from '@/lib/admin/data-quality'
import type {
  AdminOrganizationDto,
  BusinessDataQuality,
  BusinessOrganizationUsage,
  BusinessRankingMetric,
  BusinessUsage,
  OrganizationUsageItem,
} from '@/types/admin'

/**
 * The Usage & Engagement console (`/admin/:userId/business/usage`): product usage per bucket, the
 * organization ranking, and the org drill-down.
 *
 * The drill-down is a **query parameter on the same endpoint**, not a second route: the server
 * additionally requires `admin:orgs:read` when `organizationId` is present, because it enumerates
 * one tenant's usage.
 *
 * Two distinctions the page has to keep, and does:
 * - **"no usage" is not "not instrumented."** A zero inside the window is `0`; an unavailable
 *   measure is `null` and reads "not measured".
 * - **A platform total includes unattributed API requests** (`BR-6.1`), and the server's own note
 *   says so; with an organization selected those rows are excluded because they belong to nobody.
 */

type LoadState<T> =
  | { kind: 'loading' }
  | { kind: 'ready'; value: T }
  | { kind: 'error'; message: string }

const USAGE_CONFIG = {
  messagesSent: { label: 'Messages sent', color: 'var(--chart-1)' },
} satisfies ChartConfig

const AGENT_CONFIG = {
  agentRuns: { label: 'Agent runs', color: 'var(--chart-2)' },
} satisfies ChartConfig

const API_CONFIG = {
  apiRequests: { label: 'API calls', color: 'var(--chart-3)' },
} satisfies ChartConfig

const BLOSSOM_CONFIG = {
  blossomUnits: { label: 'Blossoms consumed', color: 'var(--chart-5)' },
} satisfies ChartConfig

const RANKING_METRICS: ReadonlyArray<{ value: BusinessRankingMetric; label: string }> = [
  { value: 'apiRequests', label: 'API calls' },
  { value: 'messages', label: 'Messages' },
  { value: 'agentRuns', label: 'Agent runs' },
  { value: 'blossomUnits', label: 'Blossoms' },
]

const RANKING_LABEL: Record<BusinessRankingMetric, string> = {
  apiRequests: 'API calls',
  messages: 'Messages',
  agentRuns: 'Agent runs',
  blossomUnits: 'Blossoms',
}

function useUsage(preset: RangePreset, organizationId: string | undefined, tick: number) {
  const [state, setState] = useState<LoadState<BusinessUsage>>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    setState({ kind: 'loading' })
    fetchBusinessUsage({
      ...presetWindow(preset),
      granularity: presetGranularity(preset),
      organizationId,
    })
      .then((value) => {
        if (!cancelled) setState({ kind: 'ready', value })
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setState({
            kind: 'error',
            message: err instanceof Error ? err.message : 'Unknown error',
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [preset, organizationId, tick])

  return state
}

function useRanking(preset: RangePreset, metric: BusinessRankingMetric, tick: number) {
  const [state, setState] = useState<LoadState<BusinessOrganizationUsage>>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    setState({ kind: 'loading' })
    fetchBusinessOrganizationUsage({
      ...presetWindow(preset),
      metric,
      limit: 20,
      organizationId: undefined,
    })
      .then((value) => {
        if (!cancelled) setState({ kind: 'ready', value })
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setState({
            kind: 'error',
            message: err instanceof Error ? err.message : 'Unknown error',
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [preset, metric, tick])

  return state
}

function FailureCard({ title, message }: { title: string; message: string }) {
  return (
    <Card className="border-destructive/30 shadow-xs" role="alert">
      <CardContent className="p-4 text-xs text-muted-foreground">
        <div className="font-medium text-foreground">{title}</div>
        <div className="mt-1">{message}. Nothing is shown here rather than a substitute.</div>
      </CardContent>
    </Card>
  )
}

export function AdminBusinessUsageView() {
  const [preset, setPreset] = useState<RangePreset>('30d')
  const [metric, setMetric] = useState<BusinessRankingMetric>('apiRequests')
  const [organization, setOrganization] = useState<AdminOrganizationDto | null>(null)
  const [tick, setTick] = useState(0)
  const [sort, setSort] = useState<{ key: string; direction: 'asc' | 'desc' }>({
    key: 'rank',
    direction: 'asc',
  })

  const usage = useUsage(preset, organization?.id, tick)
  const ranking = useRanking(preset, metric, tick)

  const granularity =
    usage.kind === 'ready'
      ? (usage.value.window.granularity as 'day' | 'week' | 'month')
      : presetGranularity(preset)

  const usageSeries = useMemo(() => {
    if (usage.kind !== 'ready') return []
    const axis = buildBusinessAxis(usage.value.window.from, usage.value.window.to, granularity)
    return toSeries(
      axis,
      usage.value.series.map((point) => ({
        bucketStart: point.bucketStart,
        isPartial: point.isPartial,
        values: {
          messagesSent: point.messagesSent,
          agentRuns: point.agentRuns,
          apiRequests: point.apiRequests,
          blossomUnits: point.blossomUnits,
        },
      })),
      granularity,
      ['messagesSent', 'agentRuns', 'apiRequests', 'blossomUnits'],
    )
  }, [usage, granularity])

  const partialBucket =
    usageSeries.at(-1)?.isPartial === true ? (usageSeries.at(-1)?.bucket as string) : undefined

  const quality = useMemo(() => {
    const sources: BusinessDataQuality[] = []
    if (usage.kind === 'ready') sources.push(usage.value.dataQuality)
    if (ranking.kind === 'ready') sources.push(ranking.value.dataQuality)
    if (sources.length === 0) return null

    return businessDataQuality({
      userAttributionAvailable: sources.every((source) => source.userAttributionAvailable),
      unresolvedAttributionCount: Math.max(
        ...sources.map((source) => source.unresolvedAttributionCount),
      ),
      subscriptionHistoryBackfilled: sources.some((source) => source.subscriptionHistoryBackfilled),
      lastActivityIsReconstructed: sources.some((source) => source.lastActivityIsReconstructed),
      agentMetricsUninstrumented: sources.some((source) => source.agentMetricsUninstrumented),
      notes: Array.from(new Set(sources.flatMap((source) => source.notes))),
    })
  }, [usage, ranking])

  const rows = useMemo(() => {
    if (ranking.kind !== 'ready') return []
    const items = [...ranking.value.items]
    const direction = sort.direction === 'asc' ? 1 : -1
    const value = (row: OrganizationUsageItem): string | number => {
      switch (sort.key) {
        case 'name':
          return row.name
        case 'planTier':
          return row.planTier
        case 'messagesSent':
          return row.messagesSent
        case 'agentRuns':
          return row.agentRuns
        case 'apiRequests':
          return row.apiRequests
        case 'blossomUnits':
          return row.blossomUnits
        case 'daysSinceLastActivity':
          return row.daysSinceLastActivity ?? Number.POSITIVE_INFINITY
        default:
          return row.rank
      }
    }
    return items.sort((left, right) => {
      const a = value(left)
      const b = value(right)
      if (typeof a === 'string' && typeof b === 'string') return a.localeCompare(b) * direction
      return (Number(a) - Number(b)) * direction
    })
  }, [ranking, sort])

  const columns: Column<OrganizationUsageItem>[] = [
    { key: 'rank', header: 'Rank', render: (row) => row.rank, sortable: true },
    { key: 'name', header: 'Boutique', render: (row) => row.name, sortable: true },
    { key: 'planTier', header: 'Tier', render: (row) => row.planTier, sortable: true },
    {
      key: 'messagesSent',
      header: 'Messages',
      render: (row) => row.messagesSent.toLocaleString(),
      sortable: true,
    },
    { key: 'agentRuns', header: 'Runs', render: (row) => row.agentRuns, sortable: true },
    {
      key: 'apiRequests',
      header: 'API calls',
      render: (row) => row.apiRequests.toLocaleString(),
      sortable: true,
    },
    {
      key: 'blossomUnits',
      header: 'Blossoms',
      render: (row) => row.blossomUnits,
      sortable: true,
    },
    {
      key: 'lastActivity',
      header: 'Last activity',
      render: (row) =>
        row.lastActivityAt === null ? (
          <span className="text-muted-foreground">not measured</span>
        ) : (
          new Date(row.lastActivityAt).toISOString().slice(0, 10)
        ),
    },
    {
      key: 'daysSinceLastActivity',
      header: 'Days idle',
      render: (row) =>
        row.daysSinceLastActivity === null ? (
          <span className="text-muted-foreground">not measured</span>
        ) : (
          row.daysSinceLastActivity
        ),
      sortable: true,
    },
  ]

  const onSortChange = useCallback((key: string) => {
    setSort((current) =>
      current.key === key
        ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' }
        : { key, direction: 'asc' },
    )
  }, [])

  const totals = usage.kind === 'ready' ? usage.value.totals : null

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-serif text-3xl font-medium tracking-tight text-foreground">
            Usage &amp; engagement
          </h1>
          <p className="text-xs text-muted-foreground">
            Messages, agent runs, API calls and Blossom consumption per organization.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <RangePresets value={preset} onChange={setPreset} />
          <Button variant="outline" size="sm" className="text-xs" onClick={() => setTick(tick + 1)}>
            Refresh
          </Button>
        </div>
      </div>

      <div className="max-w-md">
        <OrgPicker
          value={organization}
          onSelect={setOrganization}
          label="Organization (drill-down)"
        />
        {organization !== null && (
          <Button
            variant="ghost"
            size="sm"
            className="mt-2 text-xs"
            onClick={() => setOrganization(null)}
          >
            Clear organization
          </Button>
        )}
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        {totals !== null ? (
          <>
            <KpiTile
              label="Messages sent"
              value={totals.messagesSent}
              unit="count"
              source="business/usage → totals.messagesSent"
            />
            <KpiTile
              label="Agent runs"
              value={totals.agentRuns}
              unit="count"
              source="business/usage → totals.agentRuns"
            />
            <KpiTile
              label="API calls"
              value={totals.apiRequests}
              unit="count"
              source="business/usage → totals.apiRequests"
            />
            <KpiTile
              label="Blossoms consumed"
              value={totals.blossomUnits}
              unit="raw"
              source="business/usage → totals.blossomUnits"
            />
          </>
        ) : (
          <FailureCard
            title="Usage"
            message={usage.kind === 'error' ? usage.message : 'loading'}
          />
        )}
      </div>

      <ChartFrame title="Messages sent" config={USAGE_CONFIG}>
        <TimeSeriesChart
          data={usageSeries}
          series={[{ dataKey: 'messagesSent', label: 'Messages sent' }]}
          partialBucket={partialBucket}
        />
      </ChartFrame>

      <ChartFrame title="Agent runs" config={AGENT_CONFIG}>
        <TimeSeriesChart
          data={usageSeries}
          series={[{ dataKey: 'agentRuns', label: 'Agent runs' }]}
          partialBucket={partialBucket}
        />
      </ChartFrame>

      <ChartFrame title="API calls" config={API_CONFIG}>
        <TimeSeriesChart
          data={usageSeries}
          series={[{ dataKey: 'apiRequests', label: 'API calls' }]}
          partialBucket={partialBucket}
        />
      </ChartFrame>

      <ChartFrame
        title="Blossom consumption"
        config={BLOSSOM_CONFIG}
        description="Units consumed per bucket, from the billing rollup"
      >
        <TimeSeriesChart
          data={usageSeries}
          series={[{ dataKey: 'blossomUnits', label: 'Blossoms consumed' }]}
          partialBucket={partialBucket}
        />
      </ChartFrame>

      <Card className="border-border shadow-xs">
        <CardContent className="flex flex-col gap-3 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <div className="font-serif text-base font-medium text-foreground">
                Organizations by usage
              </div>
              <p className="text-[11px] text-muted-foreground">
                Ranked by {RANKING_LABEL[metric]}. Last activity is a reconstruction over
                conversation, agent-run, API-metric and audit timestamps, not a recorded fact.
              </p>
            </div>
            <ToggleGroup
              type="single"
              value={metric}
              onValueChange={(next) => {
                if (next !== '') setMetric(next as BusinessRankingMetric)
              }}
            >
              {RANKING_METRICS.map((option) => (
                <ToggleGroupItem key={option.value} value={option.value} className="text-xs">
                  {option.label}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
          </div>

          {ranking.kind === 'error' ? (
            <FailureCard title="Organizations by usage" message={ranking.message} />
          ) : (
            <DataTable
              columns={columns}
              rows={rows}
              getRowKey={(row) => row.organizationId}
              state={ranking.kind === 'loading' ? 'loading' : 'ready'}
              emptyMessage="No organization recorded usage in this window. That is a quiet window, not an instrumentation gap."
              sort={sort}
              onSortChange={onSortChange}
              caption="Organizations ranked by the selected usage measure"
            />
          )}
        </CardContent>
      </Card>

      {quality !== null && <DataQualityNotice report={quality} title="Business data quality" />}
    </div>
  )
}
