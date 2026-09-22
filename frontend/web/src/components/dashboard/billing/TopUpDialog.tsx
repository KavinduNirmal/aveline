import { useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
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
import { formatCount, formatMoney } from '@/lib/format-money'
import { purchaseTopUp, type TopUpPack } from '@/lib/billing-api'

interface TopUpDialogProps {
  organizationId: string
  packs: TopUpPack[]
  onPurchased: () => void
}

/**
 * A new idempotency key for one purchase attempt. The server requires the header, so a retried
 * click cannot grant the pack twice; a fresh key per attempt is what lets a genuinely repeated
 * purchase still succeed.
 */
function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `topup-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

/**
 * The top-up dialog (E-11).
 *
 * The packs come from the server's own price book, so the dialog cannot offer a SKU the purchase
 * would reject (B-4). The copy states the product's real position: **no payment provider is
 * connected**, so this records a grant, not a charge (D8).
 */
export function TopUpDialog({ organizationId, packs, onPurchased }: TopUpDialogProps) {
  const [open, setOpen] = useState(false)
  const [skuCode, setSkuCode] = useState('')
  const [paymentReference, setPaymentReference] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const selected = packs.find((pack) => pack.skuCode === skuCode) ?? null

  const submit = async () => {
    if (!selected) {
      setError('Choose a pack first.')
      return
    }

    setIsSubmitting(true)
    setError(null)
    try {
      await purchaseTopUp(
        organizationId,
        selected.skuCode,
        newIdempotencyKey(),
        paymentReference || undefined,
      )
      setOpen(false)
      setSkuCode('')
      setPaymentReference('')
      onPurchased()
    } catch {
      setError('The top-up could not be recorded. Nothing was granted.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button" className="gap-1.5 rounded-full">
          Top up Blossoms
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Top up Blossoms</DialogTitle>
          <DialogDescription>
            No payment provider is connected. A top-up is recorded as a Blossom grant, not a charge,
            and the reference below is the only evidence of payment the platform stores.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          {packs.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No top-up packs are configured in the price book, so nothing can be purchased.
            </p>
          ) : (
            <>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="top-up-pack">Pack</Label>
                <Select value={skuCode} onValueChange={setSkuCode}>
                  <SelectTrigger id="top-up-pack" aria-label="Choose a top-up pack">
                    <SelectValue placeholder="Choose a pack…" />
                  </SelectTrigger>
                  <SelectContent>
                    {packs.map((pack) => (
                      <SelectItem key={pack.skuCode} value={pack.skuCode}>
                        {formatCount(pack.blossomQuantity)} Blossoms ·{' '}
                        {formatMoney(pack.priceLkr, pack.currency)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="payment-reference">Payment reference (optional)</Label>
                <Input
                  id="payment-reference"
                  value={paymentReference}
                  onChange={(event) => setPaymentReference(event.target.value)}
                  placeholder="Provider session or transfer reference"
                />
                <p className="text-xs text-muted-foreground">
                  Without a reference the grant writes no revenue row, because a free grant is not
                  revenue.
                </p>
              </div>
            </>
          )}

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button
            type="button"
            disabled={isSubmitting || !selected}
            onClick={() => void submit()}
          >
            {isSubmitting ? 'Recording…' : 'Record the top-up'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
