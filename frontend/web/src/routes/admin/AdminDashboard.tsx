import { useCallback, useEffect, useState } from "react"
import { Link, useParams } from "react-router-dom"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { fetchSystemOverview, queryAuditEntries, listAdminRequests } from "@/lib/admin/api"
import type { AuditLogEntry, SystemOverview, AdminApprovalRequestSummary } from "@/types/admin"

type Async<T> =
  | { kind: "loading" }
  | { kind: "ready"; value: T }
  | { kind: "error"; message: string }

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : "Unknown error"
}

/**
 * The dashboard, truthfully.
 *
 * Slice A1 removes the fiction only; it adds no feature. Every number below comes from a
 * response, and a failed call renders its failure. The two hand-rolled inline vector charts are gone
 * rather than left in place — they were pure literal data, and slice A4 rebuilds this surface
 * as the V1–V11 triage-and-action dashboard over the shared chart layer.
 */
export function AdminDashboardView() {
  const { userId } = useParams<{ userId: string }>()
  const { email } = useAdminSession()
  const displayName = email ? email.split("@")[0] : "administrator"

  const [overview, setOverview] = useState<Async<SystemOverview>>({ kind: "loading" })
  const [audits, setAudits] = useState<Async<AuditLogEntry[]>>({ kind: "loading" })
  const [requests, setRequests] = useState<Async<AdminApprovalRequestSummary[]>>({
    kind: "loading",
  })

  const load = useCallback(async () => {
    setOverview({ kind: "loading" })
    setAudits({ kind: "loading" })
    setRequests({ kind: "loading" })

    try {
      setOverview({ kind: "ready", value: await fetchSystemOverview() })
    } catch (err: unknown) {
      setOverview({ kind: "error", message: messageOf(err) })
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
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const pendingCount =
    requests.kind === "ready"
      ? requests.value.filter((request) => request.status === "Pending").length
      : null

  return (
    <div className="space-y-6 max-w-7xl mx-auto pb-12">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="font-serif text-3xl font-medium tracking-tight text-foreground">
            Welcome, {displayName}
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Operational pulse, platform traffic, and administrative governance.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => void load()} className="text-xs">
          Refresh
        </Button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <KpiTile
          label="Readiness"
          value={overview.kind === "ready" ? overview.value.readiness.status : null}
          state={overview}
        />
        <KpiTile
          label="Requests / sec"
          value={
            overview.kind === "ready" && overview.value.throughput.requestsPerSecond !== null
              ? overview.value.throughput.requestsPerSecond.toFixed(2)
              : null
          }
          state={overview}
        />
        <KpiTile
          label="Error rate"
          value={
            overview.kind === "ready" && overview.value.errors.errorRate !== null
              ? `${(overview.value.errors.errorRate * 100).toFixed(2)}%`
              : null
          }
          state={overview}
        />
        <KpiTile
          label="Pending approvals"
          value={pendingCount === null ? null : String(pendingCount)}
          state={requests}
        />
      </div>

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
          <CardContent className="text-xs text-muted-foreground">Loading recent actions…</CardContent>
        ) : audits.value.length === 0 ? (
          <CardContent className="text-xs text-muted-foreground">No business actions recorded.</CardContent>
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

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Access requests</CardTitle>
          <CardDescription className="text-xs">
            Pending administrator access requests awaiting review.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-2">
          {requests.kind === "error" ? (
            <p className="text-xs text-muted-foreground">
              Access requests could not be loaded: {requests.message}
            </p>
          ) : requests.kind === "loading" ? (
            <p className="text-xs text-muted-foreground">Loading access requests…</p>
          ) : requests.value.filter((request) => request.status === "Pending").length === 0 ? (
            <p className="text-xs text-muted-foreground">No pending access requests.</p>
          ) : (
            requests.value
              .filter((request) => request.status === "Pending")
              .map((request) => (
                <div key={request.id} className="flex items-center justify-between text-xs">
                  <span className="text-foreground">{request.email}</span>
                  <Badge variant="secondary" className="text-[10px]">
                    {request.status}
                  </Badge>
                </div>
              ))
          )}
          <Button asChild variant="outline" size="sm" className="text-xs mt-2">
            <Link to={`/admin/${userId}/requests`}>Review access requests</Link>
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

function KpiTile({
  label,
  value,
  state,
}: {
  label: string
  value: string | null
  state: Async<unknown>
}) {
  return (
    <Card className="border-border shadow-xs">
      <CardContent className="p-4">
        <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
          {label}
        </span>
        <div className="text-xl font-serif font-semibold mt-2 text-foreground">
          {state.kind === "error" ? (
            <span className="text-destructive text-sm">unavailable</span>
          ) : state.kind === "loading" ? (
            <span className="text-muted-foreground text-sm">loading…</span>
          ) : value === null ? (
            // A metric whose value cannot be determined is omitted, never recorded as 0.
            <span className="text-muted-foreground text-sm">not measured</span>
          ) : (
            value
          )}
        </div>
      </CardContent>
    </Card>
  )
}
