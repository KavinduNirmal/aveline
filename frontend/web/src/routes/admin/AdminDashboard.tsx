import { useCallback, useEffect, useState } from "react"
import { Link, useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import type { ChartConfig } from "@/components/ui/chart"
import { ActivityChart } from "@/components/admin/charts/ActivityChart"
import { AreaTrendChart } from "@/components/admin/charts/AreaTrendChart"
import { ChartFrame } from "@/components/admin/charts/ChartFrame"
import { DataQualityNotice } from "@/components/admin/charts/DataQualityNotice"
import { DistributionBar, type DistributionSegment } from "@/components/admin/charts/DistributionBar"
import { GrafanaCard } from "@/components/admin/charts/GrafanaCard"
import { KpiTile } from "@/components/admin/charts/KpiTile"
import { TimeSeriesChart } from "@/components/admin/charts/TimeSeriesChart"
import { ErrorState } from "@/components/admin/data/states/ErrorState"
import {
  agentDataQuality,
  formatMetricValue,
  systemDataQuality,
} from "@/lib/admin/data-quality"
import { bucketActivity, deltaPct, type BucketUnit } from "@/lib/admin/activity-series"
import { buildBusinessAxis, toSeries } from "@/lib/admin/business-series"
import { businessDataQuality } from "@/lib/admin/data-quality"
import { presetWindow } from "@/components/admin/kpi/RangePresets"
import { grafanaLink, type GrafanaDashboard } from "@/lib/admin/grafana"
import {
  fetchAgentOverview,
  fetchBusinessActiveUsers,
  fetchBusinessGrowth,
  fetchBusinessPlanMix,
  fetchSystemAlerts,
  fetchSystemOverview,
  listAdminRequests,
  queryAuditEntries,
} from "@/lib/admin/api"
import type {
  AdminApprovalRequestSummary,
  AgentOverviewDto,
  AuditLogEntry,
  BusinessActiveUsers,
  BusinessGrowth,
  BusinessPlanMix,
  SystemAlertDto,
  SystemOverview,
} from "@/types/admin"
import { ExternalLink } from "lucide-react"

type Async<T> =
  | { kind: "loading" }
  | { kind: "ready"; value: T }
  | { kind: "error"; message: string }

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : "Unknown error"
}

/** A KPI's link out. It routes; it never redraws the series. */
function GrafanaTileLink({ dashboard }: { dashboard: GrafanaDashboard }) {
  const link = grafanaLink(dashboard)
  if (!link.enabled) {
    return <span className="text-[10px] text-muted-foreground">Grafana off</span>
  }
  return (
    <a
      href={link.href ?? "#"}
      target="_blank"
      rel="noreferrer noopener"
      className="text-[10px] text-primary hover:underline inline-flex items-center gap-0.5"
    >
      Grafana
      <ExternalLink className="size-2.5" />
    </a>
  )
}

const ACTIVITY_CONFIG = {
  activity: { label: "Business actions", color: "var(--chart-1)" },
} satisfies ChartConfig

const SIGNUP_CONFIG = {
  newUsers: { label: "New users", color: "var(--chart-2)" },
} satisfies ChartConfig

const ACTIVE_USERS_CONFIG = {
  activeUsers: { label: "Active users", color: "var(--chart-1)" },
} satisfies ChartConfig

const PLAN_MIX_CONFIG = {
  Seed: { label: "Seed (free)", color: "var(--chart-4)" },
  Bloom: { label: "Bloom", color: "var(--chart-1)" },
  Orchid: { label: "Orchid", color: "var(--chart-2)" },
  Rose: { label: "Rose", color: "var(--chart-3)" },
  Enterprise: { label: "Enterprise", color: "var(--chart-5)" },
} satisfies ChartConfig

/** The permission the business-KPI section needs. Absent, the section does not render at all. */
const BUSINESS_PERMISSION = "analytics:business:read"

const QUEUE_FIELDS = [
  ["Telemetry channel", "telemetryChannelDepth"],
  ["Event bus backlog", "eventBusBacklog"],
  ["Notification backlog", "notificationBacklog"],
  ["Inbound messages", "inboundMessageBacklog"],
  ["Agent runs running", "agentRunsRunning"],
] as const

/**
 * The dashboard: **V1–V11** of the plan's §4, the triage-and-action surface.
 *
 * *"Is anything wrong, and what do I do about it?"* in under ten seconds. Grafana owns the
 * platform's time series; this surface owns the current state, the queue, the exception and the
 * action. Every number traces to a response, and a failed call renders its failure.
 */
export function AdminDashboardView() {
  const { userId } = useParams<{ userId: string }>()
  const { email, permissions } = useAdminSession()
  // The section is gated on the permission set directly, so a caller without it issues **no**
  // business request. `Gate` does the same for nav entries; this is the same rule applied to a
  // section rather than an entry.
  const canReadBusiness = permissions.has(BUSINESS_PERMISSION)
  const displayName = email ? email.split("@")[0] : "administrator"

  const [overview, setOverview] = useState<Async<SystemOverview>>({ kind: "loading" })
  const [alerts, setAlerts] = useState<Async<SystemAlertDto[]>>({ kind: "loading" })
  const [audits, setAudits] = useState<Async<AuditLogEntry[]>>({ kind: "loading" })
  const [requests, setRequests] = useState<Async<AdminApprovalRequestSummary[]>>({
    kind: "loading",
  })
  const [agents, setAgents] = useState<Async<AgentOverviewDto>>({ kind: "loading" })
  const [range, setRange] = useState<"7d" | "12m">("7d")

  // The three landing-page business KPIs. Each is loaded independently so one failure names itself
  // instead of blanking the section.
  const [businessGrowth, setBusinessGrowth] = useState<Async<BusinessGrowth>>({ kind: "loading" })
  const [businessActive, setBusinessActive] = useState<Async<BusinessActiveUsers>>({
    kind: "loading",
  })
  const [businessPlanMix, setBusinessPlanMix] = useState<Async<BusinessPlanMix>>({
    kind: "loading",
  })

  const businessWindow = useCallback(() => ({
    ...presetWindow("30d"),
    granularity: "day" as const,
  }), [])

  const load = useCallback(async () => {
    setOverview({ kind: "loading" })
    setAlerts({ kind: "loading" })
    setAudits({ kind: "loading" })
    setRequests({ kind: "loading" })
    setAgents({ kind: "loading" })

    try {
      setOverview({ kind: "ready", value: await fetchSystemOverview() })
    } catch (err: unknown) {
      setOverview({ kind: "error", message: messageOf(err) })
    }
    try {
      // V2: pass `status=Firing` explicitly, so `Resolved` rows are never counted as active.
      const page = await fetchSystemAlerts({ status: "Firing", pageSize: 5 })
      setAlerts({ kind: "ready", value: page.items })
    } catch (err: unknown) {
      setAlerts({ kind: "error", message: messageOf(err) })
    }
    try {
      // A wider read than the table needs: the same page feeds the volume chart, which buckets a
      // 7-day or 12-month window. It is one request, not two.
      const page = await queryAuditEntries({ pageSize: 200 })
      setAudits({ kind: "ready", value: page.items })
    } catch (err: unknown) {
      setAudits({ kind: "error", message: messageOf(err) })
    }
    try {
      setRequests({ kind: "ready", value: await listAdminRequests() })
    } catch (err: unknown) {
      setRequests({ kind: "error", message: messageOf(err) })
    }
    try {
      setAgents({ kind: "ready", value: await fetchAgentOverview() })
    } catch (err: unknown) {
      setAgents({ kind: "error", message: messageOf(err) })
    }
  }, [])

  /** The business reads are a separate pass, because they are gated separately. */
  const loadBusiness = useCallback(async () => {
    if (!canReadBusiness) return
    setBusinessGrowth({ kind: "loading" })
    setBusinessActive({ kind: "loading" })
    setBusinessPlanMix({ kind: "loading" })

    const window = businessWindow()
    try {
      setBusinessGrowth({ kind: "ready", value: await fetchBusinessGrowth(window) })
    } catch (err: unknown) {
      setBusinessGrowth({ kind: "error", message: messageOf(err) })
    }
    try {
      setBusinessActive({ kind: "ready", value: await fetchBusinessActiveUsers(window) })
    } catch (err: unknown) {
      setBusinessActive({ kind: "error", message: messageOf(err) })
    }
    try {
      setBusinessPlanMix({ kind: "ready", value: await fetchBusinessPlanMix() })
    } catch (err: unknown) {
      setBusinessPlanMix({ kind: "error", message: messageOf(err) })
    }
  }, [canReadBusiness, businessWindow])

  useEffect(() => {
    void load()
    void loadBusiness()
  }, [load, loadBusiness])

  const pending =
    requests.kind === "ready"
      ? requests.value.filter((request) => request.status === "Pending")
      : null

  const auditEntries = audits.kind === "ready" ? audits.value : []
  const bucketUnit: BucketUnit = range === "7d" ? "day" : "month"
  const activityPoints = bucketActivity(auditEntries, {
    unit: bucketUnit,
    buckets: range === "7d" ? 7 : 12,
  })
  const activityDelta = deltaPct(activityPoints)
  const recentActions = auditEntries.slice(0, 8)
  const errorEntries = auditEntries.filter((entry) => /error|fail|revoke/i.test(entry.action))
  const oldestPending =
    pending !== null && pending.length > 0
      ? pending.reduce((oldest, item) =>
          item.requestedAt < oldest.requestedAt ? item : oldest,
        ).requestedAt
      : null

  // ── Derived business series ───────────────────────────────────────────────────────────────
  // The axis comes from the server's own window, so the client never invents buckets; a bucket
  // the server did not send stays `null` and is drawn as a gap.

  const signupPoints =
    businessGrowth.kind === "ready"
      ? toSeries(
          buildBusinessAxis(
            businessGrowth.value.window.from,
            businessGrowth.value.window.to,
            "day",
          ),
          businessGrowth.value.series.map((point) => ({
            bucketStart: point.bucketStart,
            isPartial: point.isPartial,
            values: {
              newUsers: point.newUsers,
              newOrganizations: point.newOrganizations,
            },
          })),
          "day",
          ["newUsers", "newOrganizations"],
        )
      : []

  const activeUserPoints =
    businessActive.kind === "ready"
      ? toSeries(
          buildBusinessAxis(
            businessActive.value.window.from,
            businessActive.value.window.to,
            "day",
          ),
          businessActive.value.series.map((point) => ({
            bucketStart: point.bucketStart,
            isPartial: point.isPartial,
            values: { activeUsers: point.activeUsers },
          })),
          "day",
          ["activeUsers"],
        )
      : []

  const activeUserPartial =
    activeUserPoints.at(-1)?.isPartial === true
      ? (activeUserPoints.at(-1)?.bucket as string)
      : undefined

  const signupPartial =
    signupPoints.at(-1)?.isPartial === true
      ? (signupPoints.at(-1)?.bucket as string)
      : undefined

  const planMixSegments: DistributionSegment[] =
    businessPlanMix.kind === "ready"
      ? businessPlanMix.value.tiers
          .filter((tier) => tier.organizationCount > 0)
          .map((tier) => ({
            key: tier.planTier,
            label: tier.planTier,
            value: tier.organizationCount,
            free: tier.isFree,
          }))
      : []

  const businessQuality = (() => {
    const qualities = [businessGrowth, businessActive, businessPlanMix].flatMap((state) =>
      state.kind === "ready" ? [state.value.dataQuality] : [],
    )
    if (qualities.length === 0) return null
    return businessDataQuality({
      userAttributionAvailable: qualities.every((quality) => quality.userAttributionAvailable),
      unresolvedAttributionCount: Math.max(
        ...qualities.map((quality) => quality.unresolvedAttributionCount),
      ),
      subscriptionHistoryBackfilled: qualities.some(
        (quality) => quality.subscriptionHistoryBackfilled,
      ),
      lastActivityIsReconstructed: qualities.some(
        (quality) => quality.lastActivityIsReconstructed,
      ),
      agentMetricsUninstrumented: qualities.some(
        (quality) => quality.agentMetricsUninstrumented,
      ),
      notes: Array.from(new Set(qualities.flatMap((quality) => quality.notes))),
    })
  })()

  return (
    <div className="flex flex-col gap-6 pb-12">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="font-serif text-3xl font-medium tracking-tight text-foreground">
            Welcome, {displayName}
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Is anything wrong, and what needs doing about it?
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => void load()} className="text-xs">
          Refresh
        </Button>
      </div>

      {/* V1 — readiness banner. A failed call renders an error, never a fabricated "Healthy". */}
      {overview.kind === "error" ? (
        <ErrorState
          error={{ message: overview.message }}
          title="System overview could not be loaded"
          onRetry={() => void load()}
        />
      ) : overview.kind === "loading" ? (
        <p className="text-xs text-muted-foreground">Loading system pulse…</p>
      ) : (
        <>
          {/* V1 — readiness, as a single strip. It carries four facts and needs four facts' worth of
              space; a full-width card for "Healthy" was mostly empty. A failed call still renders
              an error above, never a fabricated "Healthy". */}
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
            <span
              className={
                overview.value.readiness.status === "Healthy"
                  ? "inline-flex items-center gap-1.5 font-medium text-success"
                  : "inline-flex items-center gap-1.5 font-medium text-destructive"
              }
            >
              <span className="size-2 rounded-full bg-current" aria-hidden="true" />
              Readiness: {overview.value.readiness.status}
            </span>
            <span className="text-muted-foreground">
              {overview.value.readiness.checks.length} dependency probes
            </span>
            <span className="font-mono text-[11px] text-muted-foreground">
              {overview.value.version.gitSha} · {overview.value.version.environment}
            </span>
            <span className="font-mono text-[11px] text-muted-foreground">
              uptime {formatMetricValue(overview.value.uptimeSeconds, "seconds")}
            </span>
          </div>

          {/* V6 — the platform-health KPIs. The numbers are the ten-second answer. */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <KpiTile
              label="Requests / sec"
              value={overview.value.throughput.requestsPerSecond}
              source="system/overview → throughput.requestsPerSecond"
              action={<GrafanaTileLink dashboard="overview" />}
            />
            <KpiTile
              label="Error rate"
              value={overview.value.errors.errorRate}
              unit="percent"
              source={`system/overview → errors.errorRate (${overview.value.errors.windowSize})`}
              approximate
              action={<GrafanaTileLink dashboard="overview" />}
            />
            <KpiTile
              label="Firing alerts"
              value={alerts.kind === "ready" ? alerts.value.length : null}
              source="system/alerts?status=Firing"
            />
          </div>

          {/* Business actions logged. The source is Postgres (`GET /admin/audit`), so this is the
              console's own chart and not a second copy of a Grafana panel (Q11). The y-axis carries
              its unit and the description names the three things it deliberately excludes, because
              a bare "volume" chart does not say what it is a volume *of*. */}
          <ChartFrame
            title="Business actions logged"
            description="Count of write operations recorded in the audit ledger per period: ledger entries, entitlement overrides, plan changes, approvals and pricing edits. Excludes reads, failed requests and sign-ins — the audit table records only actions that changed something."
            config={ACTIVITY_CONFIG}
            action={
              <div className="flex items-center gap-2">
                {activityDelta !== null && (
                  <Badge variant="secondary" className="font-mono text-[10px]">
                    {activityDelta >= 0 ? "+" : ""}
                    {activityDelta}%
                  </Badge>
                )}
                <ToggleGroup
                  type="single"
                  value={range}
                  onValueChange={(value) => {
                    if (value !== "") setRange(value as "7d" | "12m")
                  }}
                >
                  <ToggleGroupItem value="7d" className="text-xs">
                    Weekly
                  </ToggleGroupItem>
                  <ToggleGroupItem value="12m" className="text-xs">
                    Monthly
                  </ToggleGroupItem>
                </ToggleGroup>
              </div>
            }
          >
            <ActivityChart points={activityPoints} />
          </ChartFrame>

          {/* The three business KPIs. Each uses a different chart type — a line, an area and a
              stacked distribution bar — so the page does not read as one repeated chart. The whole
              block is gated on `analytics:business:read`.

              A failed read is rendered by **`ChartFrame`'s own state**, never inside the chart: a
              message placed inside `ChartContainer` lands in recharts' measured `0×0` box and wraps
              one character per line. */}
          {canReadBusiness && (
            <div className="flex min-w-0 flex-col gap-4">
              <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
                <div className="min-w-0">
                  <h2 className="font-serif text-lg font-medium tracking-tight text-foreground">
                    Business snapshot
                  </h2>
                  <p className="text-[11px] text-muted-foreground">
                    Trailing 30 days, from Aveline&rsquo;s own Postgres tables. Every figure is a
                    count; a blank reads &ldquo;not measured&rdquo; rather than zero.
                  </p>
                </div>
                <Button asChild variant="outline" size="sm" className="shrink-0 text-xs">
                  <Link to={`/admin/${userId}/business`}>Open Growth</Link>
                </Button>
              </div>

              <div className="grid min-w-0 grid-cols-1 gap-4 xl:grid-cols-3">
                {/* KPI 1 — new signups, as a line trend. */}
                <ChartFrame
                  title="New signups"
                  description="New users and new boutiques per day, from the local first-seen timestamp (GET business/growth)"
                  config={SIGNUP_CONFIG}
                  action={<GrafanaTileLink dashboard="business" />}
                  state={businessGrowth.kind === "error" ? "error" : "ready"}
                  stateMessage={
                    businessGrowth.kind === "error" ? businessGrowth.message : undefined
                  }
                >
                  <TimeSeriesChart
                    data={signupPoints}
                    series={[
                      { dataKey: "newUsers", label: "New users" },
                      { dataKey: "newOrganizations", label: "New boutiques" },
                    ]}
                    partialBucket={signupPartial}
                  />
                </ChartFrame>

                {/* KPI 2 — active users, as an area trend with the DAU reading above it. */}
                <ChartFrame
                  title="Active users"
                  description="Distinct authenticated users who sent at least one request per day. An unattributed window reads not measured (GET business/active-users)"
                  config={ACTIVE_USERS_CONFIG}
                  action={<GrafanaTileLink dashboard="business" />}
                  state={businessActive.kind === "error" ? "error" : "ready"}
                  stateMessage={
                    businessActive.kind === "error" ? businessActive.message : undefined
                  }
                >
                  <div className="flex h-full w-full min-w-0 flex-col">
                    <p className="shrink-0 text-[11px] text-muted-foreground">
                      Daily active:{" "}
                      <span className="font-serif text-base font-semibold text-foreground">
                        {businessActive.kind === "ready"
                          ? formatMetricValue(businessActive.value.rolling.dau, "count")
                          : "…"}
                      </span>
                      {businessActive.kind === "ready" && (
                        <span className="ml-2">
                          weekly {formatMetricValue(businessActive.value.rolling.wau, "count")} ·
                          monthly {formatMetricValue(businessActive.value.rolling.mau, "count")}
                        </span>
                      )}
                    </p>
                    <div className="min-h-0 w-full min-w-0 flex-1">
                      <AreaTrendChart
                        data={activeUserPoints}
                        series={[{ dataKey: "activeUsers", label: "Active users" }]}
                        config={ACTIVE_USERS_CONFIG}
                        partialBucket={activeUserPartial}
                      />
                    </div>
                  </div>
                </ChartFrame>

                {/* KPI 3 — the plan mix, as a single stacked distribution bar. */}
                <ChartFrame
                  title="Plan mix"
                  description="Organizations per tier, from Organizations.PlanTier, which is authoritative for every organization (GET business/plan-mix)"
                  config={PLAN_MIX_CONFIG}
                  state={businessPlanMix.kind === "error" ? "error" : "ready"}
                  stateMessage={
                    businessPlanMix.kind === "error" ? businessPlanMix.message : undefined
                  }
                >
                  <div className="flex h-full w-full min-w-0 flex-col gap-2">
                    <DistributionBar segments={planMixSegments} config={PLAN_MIX_CONFIG} />
                    {businessPlanMix.kind === "ready" && (
                      <p className="text-[11px] text-muted-foreground">
                        {businessPlanMix.value.organizationsWithBillingRow} of{" "}
                        {businessPlanMix.value.organizationsTotal} boutiques have a billing record.
                      </p>
                    )}
                  </div>
                </ChartFrame>
              </div>

              {/* One merged notice, so no endpoint's caveat is dropped. */}
              {businessQuality !== null && (
                <DataQualityNotice report={businessQuality} title="Business data quality" />
              )}
            </div>
          )}

          {/* V8 — queue depths; a null field renders "not measured", never 0. */}
          <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-5 gap-4">
            {QUEUE_FIELDS.map(([label, field]) => (
              <KpiTile
                key={field}
                label={label}
                value={overview.value.queues[field]}
                source="system/overview → queues"
              />
            ))}
          </div>

          {/* V7 — the throughput honesty contract, not a chart. */}
          <DataQualityNotice
            report={systemDataQuality({
              omitted: overview.value.throughput.omitted,
              dataQuality: {
                points: overview.value.throughput.omitted.length,
                measured: overview.value.throughput.requestsPerSecond !== null,
              },
            })}
            title="Throughput"
          />
        </>
      )}

      {/* V5 — Blossom reconciliation: a critical banner, never a tile. */}
      <Card className="border-border/60 bg-muted/20 shadow-xs">
        <CardContent className="p-3 text-xs text-muted-foreground flex flex-wrap items-center justify-between gap-2">
          <span>
            Blossom reconciliation status unavailable — no organization is selected. The console
            shows the boolean per organization; the drift series is Grafana&rsquo;s.
          </span>
          <Button asChild variant="outline" size="sm" className="text-xs">
            <Link to={`/admin/${userId}/blossoms`}>Open the ledger</Link>
          </Button>
        </CardContent>
      </Card>

      {/* V2 — firing alerts. */}
      <Card className="border-border shadow-xs overflow-hidden">
        <CardHeader>
          <CardTitle className="font-serif text-base">Firing alerts</CardTitle>
          <CardDescription className="text-xs">
            Product alert rules only; operator alerts live in Grafana by design.
          </CardDescription>
        </CardHeader>
        {alerts.kind === "error" ? (
          <CardContent className="text-xs text-muted-foreground">
            Alerts could not be loaded: {alerts.message}
          </CardContent>
        ) : alerts.kind === "loading" ? (
          <CardContent className="text-xs text-muted-foreground">Loading alerts…</CardContent>
        ) : alerts.value.length === 0 ? (
          <CardContent className="text-xs text-muted-foreground">No firing alerts.</CardContent>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Alert</TableHead>
                <TableHead>Severity</TableHead>
                <TableHead>Occurrences</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {alerts.value.map((alert) => (
                <TableRow key={alert.id}>
                  <TableCell className="text-xs text-foreground">{alert.title}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">{alert.severity}</TableCell>
                  <TableCell className="text-xs font-mono text-muted-foreground">
                    {alert.occurrenceCount}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      {/* V3 and V9 — the queue and the derived error hotspot. */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card className="border-border shadow-xs">
          <CardHeader>
            <CardTitle className="font-serif text-base">Pending access requests</CardTitle>
            <CardDescription className="text-xs">
              Counted on status === &quot;Pending&quot;; no Grafana equivalent exists.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            {requests.kind === "error" ? (
              <p className="text-xs text-muted-foreground">
                Access requests could not be loaded: {requests.message}
              </p>
            ) : requests.kind === "loading" ? (
              <p className="text-xs text-muted-foreground">Loading access requests…</p>
            ) : (
              <>
                <div className="text-2xl font-serif font-semibold text-foreground">
                  {pending?.length ?? 0}
                </div>
                {oldestPending !== null && (
                  <p className="text-xs text-muted-foreground">
                    oldest requested {new Date(oldestPending).toLocaleString()}
                  </p>
                )}
              </>
            )}
            <Button asChild variant="outline" size="sm" className="text-xs">
              <Link to={`/admin/${userId}/requests`}>Review access requests</Link>
            </Button>
          </CardContent>
        </Card>

        <Card className="border-border shadow-xs">
          <CardHeader>
            <CardTitle className="font-serif text-base">Log error hotspot</CardTitle>
            <CardDescription className="text-xs">
              Derived: actions whose name matches error, fail or revoke.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <div className="text-2xl font-serif font-semibold text-foreground">
              {audits.kind === "ready" ? errorEntries.length : "—"}
            </div>
            {errorEntries.slice(0, 3).map((entry) => (
              <div key={entry.id} className="text-xs text-muted-foreground truncate">
                {entry.action} · {entry.entityType}:{entry.entityId}
              </div>
            ))}
            <Button asChild variant="outline" size="sm" className="text-xs">
              <Link to={`/admin/${userId}/logs`}>Open the log viewer</Link>
            </Button>
          </CardContent>
        </Card>
      </div>

      {/* V10 — agent activity. "No runs recorded yet" is not "not instrumented". */}
      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Agent activity</CardTitle>
          <CardDescription className="text-xs">
            The distinction between an empty history and an uninstrumented one is the point.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {agents.kind === "error" ? (
            <p className="text-xs text-muted-foreground">
              Agent statistics could not be loaded: {agents.message}
            </p>
          ) : agents.kind === "loading" ? (
            <p className="text-xs text-muted-foreground">Loading agent statistics…</p>
          ) : (
            <>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <KpiTile label="Total runs" value={agents.value.totalRuns} source="agents/overview" />
                <KpiTile label="Running" value={agents.value.running} source="agents/overview" />
                <KpiTile
                  label="Paused for approval"
                  value={agents.value.pausedForApproval}
                  source="agents/overview"
                />
                <KpiTile
                  label="Success rate"
                  value={agents.value.successRate}
                  unit="percent"
                  source="agents/overview"
                  action={<GrafanaTileLink dashboard="business" />}
                />
              </div>
              <DataQualityNotice
                report={agentDataQuality({
                  totalRuns: agents.value.totalRuns,
                  ...agents.value.dataQuality,
                })}
              />
            </>
          )}
        </CardContent>
      </Card>

      {/* V4 — recent business actions. */}
      <Card className="border-border shadow-xs overflow-hidden">
        <CardHeader>
          <CardTitle className="font-serif text-base">Recent business actions</CardTitle>
          <CardDescription className="text-xs">
            Business actions only; reads and failed requests are not recorded.
          </CardDescription>
        </CardHeader>
        {audits.kind === "error" ? (
          <CardContent className="text-xs text-muted-foreground">
            Recent actions could not be loaded: {audits.message}
          </CardContent>
        ) : audits.kind === "loading" ? (
          <CardContent className="text-xs text-muted-foreground">
            Loading recent actions…
          </CardContent>
        ) : recentActions.length === 0 ? (
          <CardContent className="text-xs text-muted-foreground">
            No business actions recorded.
          </CardContent>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Action</TableHead>
                <TableHead>Entity</TableHead>
                <TableHead>Actor</TableHead>
                <TableHead>When</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {recentActions.map((entry) => (
                <TableRow key={entry.id}>
                  <TableCell className="text-xs font-medium text-foreground">
                    {entry.action}
                  </TableCell>
                  <TableCell className="text-xs font-mono text-muted-foreground">
                    {entry.entityType}:{entry.entityId}
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">{entry.actorKind}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {new Date(entry.occurredAt).toLocaleString()}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      {/* V11 — the Grafana entry point. */}
      <GrafanaCard />
    </div>
  )
}
