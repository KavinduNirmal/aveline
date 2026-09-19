import { useState } from "react"
import { useAuditTrail } from "@/hooks/useAuditTrail"
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import { ChevronDown, ChevronRight, Copy, Check, History, X } from "lucide-react"

interface AuditTrailPanelProps {
  actorUserId?: string
  organizationId?: string
  title?: string
  open: boolean
  onClose: () => void
}

export function AuditTrailPanel({
  actorUserId,
  organizationId,
  title = "Audit History",
  open,
  onClose,
}: AuditTrailPanelProps) {
  const { entries, isLoading, error, hasMore, loadMore, refresh } = useAuditTrail({
    actorUserId,
    organizationId,
    enabled: open,
    pageSize: 20,
  })

  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [copiedId, setCopiedId] = useState<string | null>(null)

  const copyToClipboard = (text: string, id: string) => {
    void navigator.clipboard.writeText(text)
    setCopiedId(id)
    setTimeout(() => setCopiedId(null), 2000)
  }

  return (
    <Sheet open={open} onOpenChange={(o) => !o && onClose()}>
      <SheetContent className="sm:max-w-xl w-full flex flex-col p-0 bg-card border-l border-border">
        <SheetHeader className="p-5 border-b border-border bg-muted/20">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <History className="size-5 text-primary" />
              <SheetTitle className="font-serif text-lg">{title}</SheetTitle>
            </div>
            <Button variant="ghost" size="icon" onClick={onClose} className="h-8 w-8">
              <X className="size-4" />
            </Button>
          </div>
          <SheetDescription className="text-xs text-muted-foreground">
            Auditable business actions recorded by the system.
          </SheetDescription>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {error && (
            <div className="p-3 text-xs bg-destructive/10 text-destructive rounded-lg border border-destructive/20 flex justify-between items-center">
              <span>{error}</span>
              <Button size="sm" variant="outline" onClick={() => void refresh()} className="h-7 text-xs">
                Retry
              </Button>
            </div>
          )}

          {isLoading && entries.length === 0 ? (
            <div className="space-y-3">
              {[...Array(5)].map((_, i) => (
                <div key={i} className="p-3 border border-border rounded-lg space-y-2">
                  <Skeleton className="h-4 w-3/4" />
                  <Skeleton className="h-3 w-1/2" />
                </div>
              ))}
            </div>
          ) : entries.length === 0 ? (
            <div className="text-center py-12 text-muted-foreground text-sm">
              No audit records found for this context.
            </div>
          ) : (
            entries.map((entry) => {
              const isExpanded = expandedId === entry.id
              return (
                <div
                  key={entry.id}
                  className="border border-border/80 rounded-lg p-3 bg-background hover:bg-muted/10 transition-all text-xs"
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="space-y-1">
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <Badge variant="outline" className="font-mono text-[11px] bg-muted/40">
                          {entry.action}
                        </Badge>
                        <span className="text-muted-foreground">
                          {new Date(entry.occurredAt).toLocaleTimeString()}
                        </span>
                      </div>
                      <div className="text-muted-foreground">
                        <span className="font-medium text-foreground">{entry.entityType}</span>: {entry.entityId}
                      </div>
                      {entry.reason && (
                        <div className="text-muted-foreground italic">
                          "{entry.reason}"
                        </div>
                      )}
                    </div>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-7 px-2 text-[11px]"
                      onClick={() => setExpandedId(isExpanded ? null : entry.id)}
                    >
                      {isExpanded ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                      Snapshots
                    </Button>
                  </div>

                  {isExpanded && (
                    <div className="mt-3 pt-3 border-t border-border/60 space-y-2">
                      <div className="grid grid-cols-2 gap-2 font-mono text-[10px]">
                        <div className="p-2 bg-muted/30 rounded border border-border">
                          <div className="font-semibold text-muted-foreground mb-1">BEFORE</div>
                          <pre className="overflow-x-auto whitespace-pre-wrap">
                            {entry.before ? JSON.stringify(entry.before, null, 2) : "null (or redacted)"}
                          </pre>
                        </div>
                        <div className="p-2 bg-muted/30 rounded border border-border">
                          <div className="font-semibold text-muted-foreground mb-1">AFTER</div>
                          <pre className="overflow-x-auto whitespace-pre-wrap">
                            {entry.after ? JSON.stringify(entry.after, null, 2) : "null (or redacted)"}
                          </pre>
                        </div>
                      </div>
                      {entry.requestId && (
                        <div className="flex items-center justify-between text-[11px] text-muted-foreground pt-1">
                          <span>Request ID: {entry.requestId}</span>
                          <Button
                            variant="ghost"
                            size="icon"
                            className="h-6 w-6"
                            onClick={() => copyToClipboard(entry.requestId!, entry.id)}
                          >
                            {copiedId === entry.id ? <Check className="size-3 text-green-600" /> : <Copy className="size-3" />}
                          </Button>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })
          )}

          {hasMore && (
            <div className="pt-2 flex justify-center">
              <Button
                variant="outline"
                size="sm"
                onClick={() => void loadMore()}
                disabled={isLoading}
                className="w-full text-xs"
              >
                {isLoading ? "Loading..." : "Load Older Records"}
              </Button>
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}
