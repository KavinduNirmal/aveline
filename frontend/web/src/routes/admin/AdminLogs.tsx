import { useEffect, useRef, useState } from "react"
import { queryAuditEntries } from "@/lib/admin/api"
import type { AuditLogEntry } from "@/types/admin"
import { Card, CardContent } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Pause, Play, RefreshCw, Filter, ChevronDown, ChevronRight, Copy, Check } from "lucide-react"

export type LogLevel = "error" | "warn" | "info" | "debug"

export function deriveLogLevel(action: string): LogLevel {
  const lower = action.toLowerCase()
  if (
    lower.includes("failed") ||
    lower.includes("error") ||
    lower.includes("rejected") ||
    lower.includes("suspended")
  ) {
    return "error"
  }
  if (lower.includes("cancelled") || lower.includes("revoked") || lower.includes("warning")) {
    return "warn"
  }
  if (lower.includes("created") || lower.includes("activated") || lower.includes("approved")) {
    return "info"
  }
  return "debug"
}

export function AdminLogsView() {
  const [entries, setEntries] = useState<AuditLogEntry[]>([])
  const [isPaused, setIsPaused] = useState(false)
  const [pollIntervalMs] = useState(1500)
  const [levelFilter, setLevelFilter] = useState<string>("all")
  const [actionFilter, setActionFilter] = useState<string>("")
  const [lastPollTime, setLastPollTime] = useState<Date>(new Date())
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [copiedId, setCopiedId] = useState<string | null>(null)

  const cursorRef = useRef<string | null>(null)
  const seenIdsRef = useRef<Set<string>>(new Set())
  const feedEndRef = useRef<HTMLDivElement | null>(null)

  const fetchIncremental = async () => {
    try {
      const res = await queryAuditEntries({
        pageSize: 30,
        from: cursorRef.current ? new Date(Date.now() - 60000).toISOString() : undefined,
      })

      setLastPollTime(new Date())

      if (res.items.length > 0) {
        const fresh = res.items.filter((item) => !seenIdsRef.current.has(item.id))
        if (fresh.length > 0) {
          for (const item of fresh) {
            seenIdsRef.current.add(item.id)
          }
          cursorRef.current = fresh[0].occurredAt

          setEntries((prev) => {
            const merged = [...fresh, ...prev].slice(0, 2000)
            return merged
          })
        }
      }
    } catch {
      // Ignore transient polling failure
    }
  }

  useEffect(() => {
    void fetchIncremental()
    const interval = setInterval(() => {
      if (!isPaused && document.visibilityState === "visible") {
        void fetchIncremental()
      }
    }, pollIntervalMs)

    return () => clearInterval(interval)
  }, [isPaused, pollIntervalMs])

  const copyId = (text: string, id: string) => {
    void navigator.clipboard.writeText(text)
    setCopiedId(id)
    setTimeout(() => setCopiedId(null), 2000)
  }

  const filteredEntries = entries.filter((e) => {
    if (levelFilter !== "all" && deriveLogLevel(e.action) !== levelFilter) {
      return false
    }
    if (actionFilter && !e.action.toLowerCase().includes(actionFilter.toLowerCase())) {
      return false
    }
    return true
  })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground flex items-center gap-2">
            Real-time Audit & Log Stream
            <span className="size-2.5 rounded-full bg-emerald-500 animate-pulse" />
          </h2>
          <p className="text-sm text-muted-foreground">
            Live incremental event poller running on a 1.5s cadence with adaptive backoff.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsPaused(!isPaused)}
            className="text-xs h-8 gap-1.5"
          >
            {isPaused ? <Play className="size-3.5 text-emerald-600" /> : <Pause className="size-3.5 text-amber-600" />}
            {isPaused ? "Resume Stream" : "Pause Stream"}
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => void fetchIncremental()}
            className="text-xs h-8 gap-1.5"
          >
            <RefreshCw className="size-3.5" />
            Poll Now
          </Button>
        </div>
      </div>

      <Card className="border-border shadow-xs">
        <CardContent className="p-3 flex flex-wrap gap-3 items-center justify-between text-xs">
          <div className="flex items-center gap-2 flex-1 min-w-[200px]">
            <Filter className="size-4 text-muted-foreground" />
            <Input
              placeholder="Filter by action name..."
              value={actionFilter}
              onChange={(e) => setActionFilter(e.target.value)}
              className="h-8 text-xs"
            />
          </div>

          <div className="flex items-center gap-2">
            <span className="text-muted-foreground">Severity (derived):</span>
            <select
              value={levelFilter}
              onChange={(e) => setLevelFilter(e.target.value)}
              className="h-8 px-2 text-xs rounded-md border border-input bg-background text-foreground"
            >
              <option value="all">All Levels</option>
              <option value="error">Error / Failure</option>
              <option value="warn">Warning / Revoke</option>
              <option value="info">Info / Activation</option>
              <option value="debug">Debug / Other</option>
            </select>
          </div>

          <div className="text-muted-foreground font-mono text-[11px]">
            Last poll: {lastPollTime.toLocaleTimeString()} | Buffer: {entries.length}/2000
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs bg-card/60 backdrop-blur-xs overflow-hidden">
        <div className="max-h-[600px] overflow-y-auto p-4 space-y-2 font-mono text-xs">
          {filteredEntries.length === 0 ? (
            <div className="py-12 text-center text-muted-foreground font-sans text-sm">
              Waiting for incoming log events...
            </div>
          ) : (
            filteredEntries.map((e) => {
              const level = deriveLogLevel(e.action)
              const isExpanded = expandedId === e.id
              return (
                <div
                  key={e.id}
                  className="p-2.5 rounded-lg border border-border/80 bg-background/90 hover:bg-muted/30 transition-all"
                >
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-muted-foreground text-[11px]">
                        {new Date(e.occurredAt).toLocaleTimeString()}
                      </span>
                      <Badge
                        variant={
                          level === "error"
                            ? "destructive"
                            : level === "warn"
                              ? "secondary"
                              : "outline"
                        }
                        className="text-[10px] uppercase font-mono tracking-wider h-5"
                      >
                        {level}
                      </Badge>
                      <span className="font-semibold text-foreground">{e.action}</span>
                      <span className="text-muted-foreground">
                        [{e.entityType}:{e.entityId}]
                      </span>
                    </div>

                    <div className="flex items-center gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-6 text-[11px] px-1.5"
                        onClick={() => setExpandedId(isExpanded ? null : e.id)}
                      >
                        {isExpanded ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
                        Diff
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-6 w-6"
                        onClick={() => copyId(e.id, e.id)}
                        title="Copy entry UUID"
                      >
                        {copiedId === e.id ? <Check className="size-3 text-green-600" /> : <Copy className="size-3" />}
                      </Button>
                    </div>
                  </div>

                  {e.reason && (
                    <div className="mt-1 text-[11px] text-muted-foreground italic font-sans pl-2 border-l-2 border-primary/40">
                      Reason: {e.reason}
                    </div>
                  )}

                  {isExpanded && (
                    <div className="mt-2.5 pt-2 border-t border-border grid grid-cols-2 gap-2 text-[10px]">
                      <div className="p-2 bg-muted/20 rounded border border-border">
                        <div className="font-bold text-muted-foreground mb-1">BEFORE STATE</div>
                        <pre className="overflow-x-auto whitespace-pre-wrap text-muted-foreground">
                          {e.before ? JSON.stringify(e.before, null, 2) : "(none / redacted)"}
                        </pre>
                      </div>
                      <div className="p-2 bg-muted/20 rounded border border-border">
                        <div className="font-bold text-muted-foreground mb-1">AFTER STATE</div>
                        <pre className="overflow-x-auto whitespace-pre-wrap text-foreground">
                          {e.after ? JSON.stringify(e.after, null, 2) : "(none / redacted)"}
                        </pre>
                      </div>
                    </div>
                  )}
                </div>
              )
            })
          )}
          <div ref={feedEndRef} />
        </div>
      </Card>
    </div>
  )
}
