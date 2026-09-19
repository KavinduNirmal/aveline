import { useState } from "react"

import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { describeError } from "@/lib/admin/errors"
import { AlertTriangle, Check, Copy } from "lucide-react"

/**
 * The error state every data-bearing view renders.
 *
 * A `500` renders the `traceId` **with a copy button**, because that string is the only handle a
 * support conversation has. A `403` is not rendered here — a denied read is a page-local
 * "requires a boutique membership", and letting it reach the global handler would navigate the
 * whole shell to `/forbidden`.
 */
export function ErrorState({
  error,
  title = "This could not be loaded",
  onRetry,
}: {
  error: unknown
  title?: string
  onRetry?: () => void
}) {
  const description = describeError(error)
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    if (description.traceId === null) return
    try {
      await navigator.clipboard.writeText(description.traceId)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Card className="border-destructive/30 shadow-xs">
      <CardContent className="p-4 flex items-start gap-3 text-xs">
        <AlertTriangle className="size-4 text-destructive shrink-0 mt-0.5" />
        <div className="space-y-1 min-w-0">
          <div className="font-medium text-foreground">{title}</div>
          <div className="text-muted-foreground">
            {description.message}
            {description.status !== null && (
              <span className="ml-1 font-mono">({description.status})</span>
            )}
          </div>
          {description.traceId !== null && (
            <div className="flex items-center gap-2 pt-1">
              <span className="font-mono text-[11px] text-muted-foreground truncate">
                traceId: {description.traceId}
              </span>
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-[11px] gap-1"
                onClick={() => void copy()}
              >
                {copied ? <Check className="size-3 text-success" /> : <Copy className="size-3" />}
                {copied ? 'Copied' : 'Copy'}
              </Button>
            </div>
          )}
          {onRetry !== undefined && (
            <div className="pt-1">
              <Button variant="outline" size="sm" className="text-xs" onClick={onRetry}>
                Retry
              </Button>
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
