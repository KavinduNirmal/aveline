import { useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'

import type { SystemAlertDto, SystemAlertAckResponse } from '@/types/admin'

/**
 * The acknowledge dialog.
 *
 * The response is the **entity** (`SystemAlertAckResponse`): no `ruleName`, six extra mutable
 * fields. It is deliberately **not** merged into the row wholesale — only `id`, `status` and
 * `acknowledgedAt` are read, so a type that disagrees with the list row cannot blank a label.
 */
export function AlertAcknowledgeDialog({
  alert,
  open,
  onOpenChange,
  onAcknowledged,
  acknowledge = async () => {
    throw new Error('acknowledgeAlert is not wired')
  },
}: {
  alert: SystemAlertDto | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onAcknowledged: (response: SystemAlertAckResponse) => void
  acknowledge?: (alertId: string, note?: string) => Promise<SystemAlertAckResponse>
}) {
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    if (alert === null) return
    setBusy(true)
    setError(null)
    try {
      const response = await acknowledge(alert.id, note.trim().length > 0 ? note.trim() : undefined)
      onAcknowledged(response)
      setNote('')
      onOpenChange(false)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Acknowledgement failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Acknowledge alert</DialogTitle>
          <DialogDescription>
            {alert === null
              ? 'No alert selected.'
              : `Acknowledge "${alert.title}". An already-resolved alert is rejected by the server.`}
          </DialogDescription>
        </DialogHeader>

        {alert !== null && (
          <div className="flex flex-col gap-2 text-xs text-muted-foreground">
            <div>
              Rule: <span className="text-foreground">{alert.ruleName ?? alert.metricName}</span>
            </div>
            <Input
              aria-label="Acknowledgement note"
              placeholder="Note (optional)"
              value={note}
              onChange={(event) => setNote(event.target.value)}
              className="h-8 text-xs"
            />
            {error !== null && <p className="text-destructive">{error}</p>}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" size="sm" className="text-xs" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            size="sm"
            className="text-xs"
            disabled={busy || alert === null}
            onClick={() => void submit()}
          >
            {busy ? 'Acknowledging…' : 'Acknowledge'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
