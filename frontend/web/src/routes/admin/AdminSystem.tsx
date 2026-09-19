import { useCallback, useEffect, useState } from "react"
import { fetchSystemOverview } from "@/lib/admin/api"
import type { SystemOverview } from "@/types/admin"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Activity, AlertTriangle, Server, Clock, ShieldCheck } from "lucide-react"

type LoadState =
  | { kind: "loading" }
  | { kind: "ready"; overview: SystemOverview }
  | { kind: "error"; message: string }

function formatUptime(seconds: number): string {
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  return `${hours}h ${minutes}m`
}

/**
 * The system page. There is no fallback: a failed overview renders the failure.
 *
 * The delivered version substituted a fully fabricated healthy system on any error
 * (`readiness.status: "Healthy"`, three invented probe rows, `uptimeSeconds: 84200`,
 * `requestsPerSecond: 12.4`). That is the defect this slice exists to remove.
 */
export function AdminSystemView() {
  const [state, setState] = useState<LoadState>({ kind: "loading" })

  const loadData = useCallback(async () => {
    setState({ kind: "loading" })
    try {
      const overview = await fetchSystemOverview()
      setState({ kind: "ready", overview })
    } catch (err: unknown) {
      setState({
        kind: "error",
        message: err instanceof Error ? err.message : "Unknown error",
      })
    }
  }, [])

  useEffect(() => {
    void loadData()
  }, [loadData])

  if (state.kind === "loading") {
    return <p className="text-sm text-muted-foreground">Loading system telemetry…</p>
  }

  if (state.kind === "error") {
    return (
      <Card className="border-destructive/30 shadow-xs max-w-xl">
        <CardHeader>
          <CardTitle className="font-serif text-base">
            System overview could not be loaded
          </CardTitle>
          <CardDescription className="text-xs">
            {state.message}. Nothing is shown rather than a substitute system.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button variant="outline" size="sm" onClick={() => void loadData()} className="text-xs">
            Retry
          </Button>
        </CardContent>
      </Card>
    )
  }

  const { overview } = state
  const errorRate = overview.errors.errorRate
  const requestsPerSecond = overview.throughput.requestsPerSecond
  const omitted = Array.from(new Set([...overview.omitted, ...overview.errors.omitted]))

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            System Health &amp; Telemetry
          </h2>
          <p className="text-sm text-muted-foreground">
            Operational infrastructure telemetry, dependency readiness, and real-time alerts.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => void loadData()} className="text-xs">
          Refresh
        </Button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <Card className="border-border shadow-xs">
          <CardContent className="p-4">
            <div className="flex items-center justify-between text-muted-foreground">
              <span className="text-xs font-medium uppercase tracking-wider">Readiness</span>
              <Activity className="size-4 text-primary" />
            </div>
            <div className="text-xl font-serif font-semibold mt-2 text-foreground">
              {overview.readiness.status}
            </div>
            <div className="text-[11px] text-muted-foreground mt-1">
              {overview.readiness.checks.length} dependency probes reported
            </div>
          </CardContent>
        </Card>

        <Card className="border-border shadow-xs">
          <CardContent className="p-4">
            <div className="flex items-center justify-between text-muted-foreground">
              <span className="text-xs font-medium uppercase tracking-wider">Uptime</span>
              <Clock className="size-4 text-primary" />
            </div>
            <div className="text-xl font-serif font-semibold mt-2 text-foreground">
              {formatUptime(overview.uptimeSeconds)}
            </div>
            <div className="text-[11px] text-muted-foreground mt-1">
              Reported by the API process
            </div>
          </CardContent>
        </Card>

        <Card className="border-border shadow-xs">
          <CardContent className="p-4">
            <div className="flex items-center justify-between text-muted-foreground">
              <span className="text-xs font-medium uppercase tracking-wider">Error Rate</span>
              <AlertTriangle className="size-4 text-muted-foreground" />
            </div>
            <div className="text-xl font-serif font-semibold mt-2 text-foreground">
              {errorRate === null ? (
                <span className="text-muted-foreground">not measured</span>
              ) : (
                `${(errorRate * 100).toFixed(2)}%`
              )}
            </div>
            <div className="text-[11px] text-muted-foreground mt-1">
              Window: {overview.errors.windowSize}
            </div>
          </CardContent>
        </Card>

        <Card className="border-border shadow-xs">
          <CardContent className="p-4">
            <div className="flex items-center justify-between text-muted-foreground">
              <span className="text-xs font-medium uppercase tracking-wider">Requests / sec</span>
              <Server className="size-4 text-muted-foreground" />
            </div>
            <div className="text-xl font-serif font-semibold mt-2 text-foreground">
              {requestsPerSecond === null ? (
                <span className="text-muted-foreground">not measured</span>
              ) : (
                requestsPerSecond.toFixed(2)
              )}
            </div>
            <div className="text-[11px] text-muted-foreground mt-1 font-mono">
              {overview.version.gitSha} · {overview.version.environment}
            </div>
          </CardContent>
        </Card>
      </div>

      <Card className="border-border shadow-xs overflow-hidden">
        <CardHeader>
          <CardTitle className="font-serif text-base">Infrastructure Probes</CardTitle>
          <CardDescription className="text-xs">
            Subsystem health checks reported by ASP.NET Core
          </CardDescription>
        </CardHeader>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Subsystem Probe</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Latency</TableHead>
              <TableHead>Diagnostic Message</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {overview.readiness.checks.length === 0 ? (
              <TableRow>
                <TableCell colSpan={4} className="text-xs text-muted-foreground">
                  No probes reported.
                </TableCell>
              </TableRow>
            ) : (
              overview.readiness.checks.map((c) => (
                <TableRow key={c.name}>
                  <TableCell className="font-medium text-foreground">{c.name}</TableCell>
                  <TableCell>
                    <Badge
                      variant={c.status === "Healthy" ? "default" : "destructive"}
                      className="text-[10px]"
                    >
                      {c.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    {c.durationMs}ms
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {c.message ?? "—"}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </Card>

      {omitted.length > 0 && (
        <Card className="border-border/60 bg-muted/20 shadow-xs">
          <CardContent className="p-3 text-xs text-muted-foreground flex items-center gap-2">
            <ShieldCheck className="size-4 text-primary shrink-0" />
            <span>
              Metrics not measured on this host:{" "}
              <span className="font-mono text-foreground">{omitted.join(", ")}</span>. The server
              omits a metric it cannot determine rather than recording it as 0.
            </span>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
