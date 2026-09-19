import { useCallback, useEffect, useState } from "react"
import { Link, useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { DataQualityNotice } from "@/components/admin/charts/DataQualityNotice"
import { GrafanaCard } from "@/components/admin/charts/GrafanaCard"
import { KpiTile } from "@/components/admin/charts/KpiTile"
import { ErrorState } from "@/components/admin/data/states/ErrorState"
import {
  agentDataQuality,
  formatMetricValue,
  systemDataQuality,
} from "@/lib/admin/data-quality"
import { grafanaLink, type GrafanaDashboard } from "@/lib/admin/grafana"
import {
  fetchAgentOverview,
  fetchSystemAlerts,
  fetchSystemOverview,
  listAdminRequests,
  queryAuditEntries,
} from "@/lib/admin/api"
import type {
  AdminApprovalRequestSummary,
  AgentOverviewDto,
  AuditLogEntry,
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
  const { email } = useAdminSession()
  const displayName = email ? email.split("@")[0] : "administrator"

  const [overview, setOverview] = useState<Async<SystemOverview>>({ kind: "loading" })
  const [alerts, setAlerts] = useState<Async<SystemAlertDto[]>>({ kind: "loading" })
  const [audits, setAudits] = useState<Async<AuditLogEntry[]>>({ kind: "loading" })
  const [requests, setRequests] = useState<Async<AdminApprovalRequestSummary[]>>({
    kind: "loading",
  })
  const [agents, setAgents] = useState<Async<AgentOverviewDto>>({ kind: "loading" })

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
      const page = await queryAuditEntries({ pageSize: 8 })
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

  useEffect(() => {
    void load()
  }, [load])

  const pending =
    requests.kind === "ready"
      ? requests.value.filter((request) => request.status === "Pending")
      : null

  const auditEntries = audits.kind === "ready" ? audits.value : []
  const errorEntries = auditEntries.filter((entry) => /error|fail|revoke/i.test(entry.action))
  const oldestPending =
    pending !== null && pending.length > 0
      ? pending.reduce((oldest, item) =>
          item.requestedAt < oldest.requestedAt ? item : oldest,
        ).requestedAt
      : null

  return (
    <div className="space-y-6 pb-12">
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
        <Card className="border-border shadow-xs">
          <CardContent className="p-4 text-xs text-muted-foreground">
            Loading system pulse…
          </CardContent>
        </Card>
      ) : (
        <>
          <Card className="border-border shadow-xs">
            <CardContent className="p-4 flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <span className="inline-block size-3 rounded-full bg-success" aria-hidden="true" />
                <div>
                  <div className="text-sm font-medium text-foreground">
                    Readiness: {overview.value.readiness.status}
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    {overview.value.readiness.checks.length} dependency probes reported · build{" "}
                    {overview.value.version.gitSha} · {overview.value.version.environment}
                  </div>
                </div>
              </div>
              <Badge variant="secondary" className="text-[10px] font-mono">
                uptime {formatMetricValue(overview.value.uptimeSeconds, "seconds")}
              </Badge>
            </CardContent>
          </Card>

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
          <CardContent className="space-y-2">
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
          <CardContent className="space-y-2">
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
        <CardContent className="space-y-3">
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
        ) : audits.value.length === 0 ? (
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
              {audits.value.map((entry) => (
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
