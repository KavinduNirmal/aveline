import { useCallback, useEffect, useMemo, useState } from 'react'

import { ChartFrame } from '@/components/admin/charts/ChartFrame'
import { DataQualityNotice } from '@/components/admin/charts/DataQualityNotice'
import { HorizontalBarChart } from '@/components/admin/charts/HorizontalBarChart'
import { KpiTile } from '@/components/admin/charts/KpiTile'
import { TimeSeriesChart } from '@/components/admin/charts/TimeSeriesChart'
import { RangePresets, presetGranularity, presetWindow, type RangePreset } from '@/components/admin/kpi/RangePresets'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import type { ChartConfig } from '@/components/ui/chart'

import {
  fetchBusinessActiveUsers,
  fetchBusinessGrowth,
  fetchBusinessPlanMix,
  fetchBusinessSubscriptionTrend,
} from '@/lib/admin/api'
import {
  buildBusinessAxis,
  toSeries,
  type BusinessSeriesPoint,
} from '@/lib/admin/business-series'
import { businessDataQuality } from '@/lib/admin/data-quality'
import type {
  BusinessActiveUsers,
  BusinessDataQuality as BusinessQuality,
  BusinessGrowth,
  BusinessPlanMix,
  BusinessSubscriptionTrend,
} from '@/types/admin'

/**
 * The Growth console (`/admin/:userId/business`): who is joining, who is active, and what they
 * pay.
 *
 * It follows the `LoadState` pattern rather than `useQuery`, deliberately: the page reads four
 * aggregates and needs **per-endpoint failure isolation**, so one failing call names its own
 * failure instead of blanking the page.
 *
 * The honesty rules the plan requires, all rendered rather than implied:
 * - a `null` measure reads **"not measured"** (via `KpiTile`), never `0`;
 * - the leading and trailing `isPartial` buckets are shaded;
 * - a backfilled subscription bucket is drawn dashed and the note says why;
 * - the `organizationsWithBillingRow` gap is a **visible footnote**, not an unexplained
 *   difference between two numbers.
 */

type LoadState<T> =
  | { kind: 'loading' }
  | { kind: 'ready'; value: T }
  | { kind: 'error'; message: string }

function useEndpoint<T>(load: (preset: RangePreset) => Promise<T>, preset: RangePreset, tick: number) {
  const [state, setState] = useState<LoadState<T>>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    setState({ kind: 'loading' })
    load(preset)
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
    // `load` is expected to be stable (a module-level function).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preset, tick])

  return state
}

const SIGNUP_CONFIG = {
  newUsers: { label: 'New users', color: 'var(--chart-1)' },
  newOrganizations: { label: 'New boutiques', color: 'var(--chart-2)' },
} satisfies ChartConfig

const ACTIVE_CONFIG = {
  activeUsers: { label: 'Active users', color: 'var(--chart-1)' },
  activeOrganizations: { label: 'Active boutiques', color: 'var(--chart-3)' },
} satisfies ChartConfig

const PLAN_MIX_CONFIG = {
  count: { label: 'Boutiques', color: 'var(--chart-1)' },
} satisfies ChartConfig

const SUBSCRIPTION_CONFIG = {
  activeTotal: { label: 'Active subscriptions', color: 'var(--chart-1)' },
} satisfies ChartConfig

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

/** Projects the growth series onto the dense axis. */
function growthSeries(growth: BusinessGrowth): BusinessSeriesPoint[] {
  const axis = buildBusinessAxis(
    growth.window.from,
    growth.window.to,
    growth.window.granularity as 'day' | 'week' | 'month',
  )
  return toSeries(
    axis,
    growth.series.map((point) => ({
      bucketStart: point.bucketStart,
      isPartial: point.isPartial,
      values: {
        newUsers: point.newUsers,
        newOrganizations: point.newOrganizations,
      },
    })),
    growth.window.granularity as 'day' | 'week' | 'month',
    ['newUsers', 'newOrganizations'],
  )
}

function activeUserSeries(active: BusinessActiveUsers): BusinessSeriesPoint[] {
  const granularity = active.window.granularity as 'day' | 'week' | 'month'
  const axis = buildBusinessAxis(active.window.from, active.window.to, granularity)
  return toSeries(
    axis,
    active.series.map((point) => ({
      bucketStart: point.bucketStart,
      isPartial: point.isPartial,
      values: {
        activeUsers: point.activeUsers,
        activeOrganizations: point.activeOrganizations,
      },
    })),
    granularity,
    ['activeUsers', 'activeOrganizations'],
  )
}

/** The index of the trailing bucket the window has not closed, or -1. */
function partialIndex(points: readonly BusinessSeriesPoint[]): number {
  const index = points.findIndex((point) => point.isPartial === true)
  return points.length > 0 && points[points.length - 1].isPartial ? points.length - 1 : index
}

function deltaPct(current: number, previous: number): number | null {
  if (previous === 0) return null
  return Math.round(((current - previous) / previous) * 1000) / 10
}

export function AdminBusinessGrowthView() {
  const [preset, setPreset] = useState<RangePreset>('30d')
  const [tick, setTick] = useState(0)

  const loadGrowth = useCallback(
    (selected: RangePreset) =>
      fetchBusinessGrowth({
        ...presetWindow(selected),
        granularity: presetGranularity(selected),
      }),
    [],
  )
  const loadActiveUsers = useCallback(
    (selected: RangePreset) =>
      fetchBusinessActiveUsers({
        ...presetWindow(selected),
        granularity: presetGranularity(selected),
      }),
    [],
  )
  const loadPlanMix = useCallback(() => fetchBusinessPlanMix(), [])
  const loadSubscriptions = useCallback(
    (selected: RangePreset) =>
      fetchBusinessSubscriptionTrend({
        ...presetWindow(selected),
        granularity: presetGranularity(selected),
      }),
    [],
  )

  const growth = useEndpoint<BusinessGrowth>(loadGrowth, preset, tick)
  const activeUsers = useEndpoint<BusinessActiveUsers>(loadActiveUsers, preset, tick)
  const planMix = useEndpoint<BusinessPlanMix>(loadPlanMix, preset, tick)
  const subscriptions = useEndpoint<BusinessSubscriptionTrend>(loadSubscriptions, preset, tick)

  const signups = growth.kind === 'ready' ? growthSeries(growth.value) : []
  const active = activeUsers.kind === 'ready' ? activeUserSeries(activeUsers.value) : []

  const signupPartial = useMemo(() => partialIndex(signups), [signups])
  const activePartial = useMemo(() => partialIndex(active), [active])

  const planMixData =
    planMix.kind === 'ready'
      ? planMix.value.tiers.map((tier, index) => ({
          category: tier.planTier,
          count: tier.organizationCount,
          fill: `var(--chart-${(index % 5) + 1})`,
        }))
      : []

  // Every endpoint can carry its own caveat, so the notice **merges all four** rather than
  // picking one: dropping the others would silently hide a caveat the server took the trouble to
  // send (for example, a backfilled subscription bucket alongside an attribution undercount).
  const quality = useMemo(() => {
    const sources = [growth, activeUsers, planMix, subscriptions].flatMap((state) =>
      state.kind === 'ready' ? [state.value.dataQuality as BusinessQuality] : [],
    )

    if (sources.length === 0) return null

    const merged = {
      userAttributionAvailable: sources.every((source) => source.userAttributionAvailable),
      unresolvedAttributionCount: Math.max(
        ...sources.map((source) => source.unresolvedAttributionCount),
      ),
      subscriptionHistoryBackfilled: sources.some((source) => source.subscriptionHistoryBackfilled),
      lastActivityIsReconstructed: sources.some((source) => source.lastActivityIsReconstructed),
      agentMetricsUninstrumented: sources.some((source) => source.agentMetricsUninstrumented),
      notes: Array.from(new Set(sources.flatMap((source) => source.notes))),
    }

    return businessDataQuality(merged)
  }, [activeUsers, growth, planMix, subscriptions])

  const subscriptionPartial = subscriptions.kind === 'ready' ? partialIndex(
    toSeries(
      buildBusinessAxis(
        subscriptions.value.window.from,
        subscriptions.value.window.to,
        subscriptions.value.window.granularity as 'day' | 'week' | 'month',
      ),
      subscriptions.value.series.map((point) => ({
        bucketStart: point.bucketStart,
        isPartial: point.isPartial,
        values: { activeTotal: point.activeTotal },
      })),
      subscriptions.value.window.granularity as 'day' | 'week' | 'month',
      ['activeTotal'],
    ),
  ) : -1

  const backfilledBuckets =
    subscriptions.kind === 'ready'
      ? subscriptions.value.series.filter((point) => point.isBackfilled).map((point) => point.bucketStart)
      : []

  const subscriptionSeries =
    subscriptions.kind === 'ready'
      ? toSeries(
          buildBusinessAxis(
            subscriptions.value.window.from,
            subscriptions.value.window.to,
            subscriptions.value.window.granularity as 'day' | 'week' | 'month',
          ),
          subscriptions.value.series.map((point) => ({
            bucketStart: point.bucketStart,
            isPartial: point.isPartial,
            values: { activeTotal: point.activeTotal },
          })),
          subscriptions.value.window.granularity as 'day' | 'week' | 'month',
          ['activeTotal'],
        )
      : []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-serif text-3xl font-medium tracking-tight text-foreground">Growth</h1>
          <p className="text-xs text-muted-foreground">
            Who is joining, who is active, and what they pay.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <RangePresets value={preset} onChange={setPreset} />
          <Button variant="outline" size="sm" className="text-xs" onClick={() => setTick(tick + 1)}>
            Refresh
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        {growth.kind === 'ready' ? (
          <KpiTile
            label="New users"
            value={growth.value.totals.newUsers}
            unit="count"
            source="business/growth → totals.newUsers"
            delta={deltaPct(growth.value.totals.newUsers, growth.value.previousTotals.newUsers)}
          />
        ) : (
          <FailureCard
            title="New users"
            message={growth.kind === 'error' ? growth.message : 'loading'}
          />
        )}
        {growth.kind === 'ready' ? (
          <KpiTile
            label="New boutiques"
            value={growth.value.totals.newOrganizations}
            unit="count"
            source="business/growth → totals.newOrganizations"
            delta={deltaPct(
              growth.value.totals.newOrganizations,
              growth.value.previousTotals.newOrganizations,
            )}
          />
        ) : (
          <FailureCard
            title="New boutiques"
            message={growth.kind === 'error' ? growth.message : 'loading'}
          />
        )}
        {activeUsers.kind === 'ready' ? (
          <KpiTile
            label="Daily active users"
            value={activeUsers.value.rolling.dau}
            unit="count"
            source="business/active-users → rolling.dau"
          />
        ) : (
          <FailureCard
            title="Daily active users"
            message={activeUsers.kind === 'error' ? activeUsers.message : 'loading'}
          />
        )}
        {planMix.kind === 'ready' ? (
          <KpiTile
            label="Premium share"
            value={planMix.value.premium.shareOfOrganizations}
            unit="percent"
            source="business/plan-mix → premium.shareOfOrganizations"
          />
        ) : (
          <FailureCard
            title="Premium share"
            message={planMix.kind === 'error' ? planMix.message : 'loading'}
          />
        )}
      </div>

      <ChartFrame title="Signups" config={SIGNUP_CONFIG}>
        <TimeSeriesChart
          data={signups}
          series={[
            { dataKey: 'newUsers', label: 'New users' },
            { dataKey: 'newOrganizations', label: 'New boutiques' },
          ]}
          partialBucket={signupPartial >= 0 ? signups[signupPartial]?.bucket : undefined}
        />
      </ChartFrame>

      <ChartFrame title="Active users" config={ACTIVE_CONFIG}>
        <TimeSeriesChart
          data={active}
          series={[
            { dataKey: 'activeUsers', label: 'Active users' },
            { dataKey: 'activeOrganizations', label: 'Active boutiques' },
          ]}
          partialBucket={activePartial >= 0 ? active[activePartial]?.bucket : undefined}
        />
      </ChartFrame>

      <ChartFrame
        title="Plan mix (current)"
        config={PLAN_MIX_CONFIG}
        description="Organizations per tier, from Organizations.PlanTier"
      >
        <HorizontalBarChart
          data={planMixData}
          config={PLAN_MIX_CONFIG}
        />
        {planMix.kind === 'ready' && (
          <p className="mt-2 text-[11px] text-muted-foreground">
            {planMix.value.organizationsWithBillingRow} of {planMix.value.organizationsTotal}{' '}
            boutiques have a billing record. The tier count comes from the organization itself, so a
            boutique that never changed plan is counted even though it has no subscription row.
          </p>
        )}
      </ChartFrame>

      <ChartFrame
        title="Subscriptions"
        config={SUBSCRIPTION_CONFIG}
        description="Active organizations by day, from the daily snapshot"
      >
        <TimeSeriesChart
          data={subscriptionSeries}
          series={[{ dataKey: 'activeTotal', label: 'Active subscriptions' }]}
          partialBucket={
            subscriptionPartial >= 0 ? subscriptionSeries[subscriptionPartial]?.bucket : undefined
          }
          backfilledBuckets={backfilledBuckets}
        />
      </ChartFrame>

      {quality !== null && <DataQualityNotice report={quality} title="Business data quality" />}
    </div>
  )
}
