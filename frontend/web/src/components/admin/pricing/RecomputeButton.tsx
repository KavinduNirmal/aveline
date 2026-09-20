import { useState } from "react"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { Button } from "@/components/ui/button"
import { canRecompute, RECOMPUTE_DISABLED_REASON } from "@/lib/admin/pricing"
import type { PricingRecomputeResult } from "@/types/admin"

/**
 * Recompute, gated on the **capability**.
 *
 * `admin` holds the whole catalog except the named denials (`pricing:backdate` and
 * `revenue:refund`), so an `admin` looking at a page whose permission is `pricing:manage` is
 * still `403` here. Rather than render an enabled button that fails, the control is **disabled
 * with the stated reason** and issues no request at all.
 */
export function RecomputeButton({
  ruleId,
  recompute,
}: {
  ruleId: string
  recompute: (ruleId: string) => Promise<PricingRecomputeResult>
}) {
  const { can } = useAdminSession()
  const allowed = canRecompute(can)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<PricingRecomputeResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  if (!allowed) {
    return (
      <Button
        variant="outline"
        size="sm"
        className="text-xs"
        disabled
        title={RECOMPUTE_DISABLED_REASON}
      >
        Recompute ({RECOMPUTE_DISABLED_REASON})
      </Button>
    )
  }

  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      setResult(await recompute(ruleId))
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Recompute failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <Button
        variant="outline"
        size="sm"
        className="text-xs"
        disabled={busy}
        onClick={() => void run()}
      >
        {busy ? 'Recomputing…' : 'Recompute'}
      </Button>
      {result !== null && (
        <p role="status" className="text-[11px] text-muted-foreground">
          {result.processedRecords} records, {result.affectedOrganizations} organizations, delta{' '}
          {result.totalDelta}. A zero-effect run is shown rather than hidden.
        </p>
      )}
      {error !== null && (
        <p role="status" className="text-[11px] text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}
