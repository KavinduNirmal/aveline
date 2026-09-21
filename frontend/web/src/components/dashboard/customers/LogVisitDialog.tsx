import { useEffect, useState } from 'react'
import { toast } from 'sonner'

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
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { toApiError } from '@/lib/api-error'
import { recordCustomerInteraction } from '@/lib/customers-api'
import { formatMoney } from '@/lib/format-money'

interface LogVisitDialogProps {
  organizationId: string
  customer: { customerId: string; fullName: string | null; nickname: string | null } | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onRecorded: () => void
}

const CHANNELS = [
  { value: 'in_person', label: 'In person' },
  { value: 'phone', label: 'Phone' },
  { value: 'whatsapp', label: 'WhatsApp' },
  { value: 'instagram', label: 'Instagram' },
] as const

/**
 * Records a counter interaction (E-9's write half, `POST …/interactions`).
 *
 * The amount field is labelled **"amount taken"** and the copy says what the server does with it:
 * a visit counts only for an **inbound, in-person** interaction, and the amount is added to the
 * client's lifetime spend. `blossomsCharged` is always zero, so the dialog never implies a charge.
 */
export function LogVisitDialog({
  organizationId,
  customer,
  open,
  onOpenChange,
  onRecorded,
}: LogVisitDialogProps) {
  const [channel, setChannel] = useState<string>('in_person')
  const [direction, setDirection] = useState<string>('inbound')
  const [note, setNote] = useState('')
  const [amount, setAmount] = useState('')
  const [occurredAt, setOccurredAt] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  // One key per operation, minted when the dialog opens. A retry of the same operation reuses it,
  // so a double-click cannot record two visits.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

  useEffect(() => {
    if (open) {
      setChannel('in_person')
      setDirection('inbound')
      setNote('')
      setAmount('')
      setOccurredAt(toLocalInputValue(new Date()))
      setIdempotencyKey(crypto.randomUUID())
      setIsSaving(false)
    }
  }, [open])

  const countedAsVisit = channel === 'in_person' && direction === 'inbound'
  const parsedAmount = amount.trim().length === 0 ? undefined : Number(amount)

  const submit = async () => {
    if (!customer) return
    if (parsedAmount !== undefined && (Number.isNaN(parsedAmount) || parsedAmount < 0)) {
      toast.error('The amount taken must be a number of zero or more.')
      return
    }

    setIsSaving(true)
    try {
      await recordCustomerInteraction(
        organizationId,
        customer.customerId,
        {
          occurredAtUtc: occurredAt ? new Date(occurredAt).toISOString() : new Date().toISOString(),
          channel,
          direction,
          note: note.trim().length > 0 ? note : undefined,
          purchaseTotal: parsedAmount,
        },
        idempotencyKey,
      )
      onRecorded()
      onOpenChange(false)
      toast.success(countedAsVisit ? 'Visit recorded' : 'Interaction recorded', {
        description: countedAsVisit
          ? 'This counts as a visit for the loyalty rule.'
          : 'Recorded, but only an inbound in-person interaction counts as a visit.',
      })
    } catch (caught) {
      // Keep the key: the operation may have applied. The user retries explicitly.
      const apiError = toApiError(caught)
      toast.error('Could not record this interaction', { description: apiError.message })
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="font-serif">
            Log a visit for {customer?.fullName ?? customer?.nickname ?? 'this client'}
          </DialogTitle>
          <DialogDescription>
            Only an inbound, in-person interaction counts as a visit. A visit older than 30 days is
            refused by the server.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="visit-channel">Channel</Label>
              <Select value={channel} onValueChange={setChannel}>
                <SelectTrigger id="visit-channel" aria-label="Channel">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CHANNELS.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="visit-direction">Direction</Label>
              <Select value={direction} onValueChange={setDirection}>
                <SelectTrigger id="visit-direction" aria-label="Direction">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="inbound">Inbound</SelectItem>
                  <SelectItem value="outbound">Outbound</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="visit-when">When</Label>
            <Input
              id="visit-when"
              type="datetime-local"
              value={occurredAt}
              onChange={(event) => setOccurredAt(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="visit-amount">Amount taken</Label>
            <Input
              id="visit-amount"
              inputMode="decimal"
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
              placeholder="Leave empty when nothing was purchased"
            />
            <p className="text-xs text-muted-foreground">
              {parsedAmount === undefined
                ? 'Nothing is added to this client’s lifetime spend.'
                : `Adds ${formatMoney(parsedAmount)} to this client’s lifetime spend.`}{' '}
              No Blossoms are charged for a visit.
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="visit-note">Note</Label>
            <Input
              id="visit-note"
              value={note}
              onChange={(event) => setNote(event.target.value)}
              placeholder="What happened"
            />
          </div>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={isSaving}>
            Cancel
          </Button>
          <Button type="button" onClick={() => void submit()} disabled={isSaving || !customer}>
            Record interaction
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** `datetime-local` wants `YYYY-MM-DDTHH:mm` in local time, not an ISO instant. */
function toLocalInputValue(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}
