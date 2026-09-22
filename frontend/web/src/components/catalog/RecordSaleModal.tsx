import { useEffect, useState } from 'react'
import { AlertTriangle, Check, Loader2, Receipt, X } from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { recordCatalogSale } from '@/lib/catalog-api'
import { formatMoney } from '@/lib/format-money'
import type { CatalogSaleReceipt } from '@/types/catalog'

import type { InventoryItemMock } from './mockData'

interface RecordSaleModalProps {
  open: boolean
  item: InventoryItemMock | null
  organizationId?: string
  onClose: () => void
  /** Called with the server's receipt once the sale is recorded, so the caller can update its stock. */
  onRecorded: (receipt: CatalogSaleReceipt) => void
}

/**
 * Records one counter sale of a catalog piece.
 *
 * The price defaults to the catalog price but stays editable, because a counter haggles and an
 * end-of-season piece sells below the tag. The server is the one that decrements stock and writes
 * the takings journal; this dialog only refuses the two things the server would refuse anyway
 * (selling more than is on hand, and selling for nothing), so the operator hears about it before a
 * round trip.
 */
export function RecordSaleModal({
  open,
  item,
  organizationId,
  onClose,
  onRecorded,
}: RecordSaleModalProps) {
  const [quantity, setQuantity] = useState('1')
  const [unitPrice, setUnitPrice] = useState('')
  const [note, setNote] = useState('')
  const [isSaving, setIsSaving] = useState(false)

  useEffect(() => {
    if (open) {
      setQuantity('1')
      setUnitPrice(item ? String(item.price) : '')
      setNote('')
      setIsSaving(false)
    }
  }, [open, item])

  if (!open || !item) return null

  const parsedQuantity = Number.parseInt(quantity, 10)
  const parsedUnitPrice = Number.parseFloat(unitPrice)
  const quantityValid = Number.isInteger(parsedQuantity) && parsedQuantity > 0
  const priceValid = Number.isFinite(parsedUnitPrice) && parsedUnitPrice > 0
  const inStock = quantityValid ? parsedQuantity <= item.stockQuantity : false
  const total = quantityValid && priceValid ? parsedUnitPrice * parsedQuantity : 0
  const remaining = quantityValid ? Math.max(0, item.stockQuantity - parsedQuantity) : item.stockQuantity
  const canSubmit = quantityValid && priceValid && inStock && !isSaving

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!organizationId) {
      toast.error('Cannot record a sale: this dashboard has no organisation id', {
        description: 'Reload the page. No request was sent.',
      })
      return
    }
    if (!canSubmit) {
      if (!priceValid) toast.error('A sale must have a price greater than zero')
      else if (!inStock) toast.error(`Only ${item.stockQuantity} piece(s) are in stock`)
      else toast.error('Enter a whole number of pieces')
      return
    }

    setIsSaving(true)
    try {
      const receipt = await recordCatalogSale(organizationId, item.id, {
        quantity: parsedQuantity,
        unitPrice: parsedUnitPrice,
        note: note.trim() || undefined,
      })
      toast.success('Sale recorded', {
        description: `${receipt.quantitySold} × ${receipt.itemName} · ${formatMoney(receipt.totalAmount)} · ${receipt.remainingStock} left`,
      })
      onRecorded(receipt)
      onClose()
    } catch (err: any) {
      const errorMsg =
        err?.response?.data?.error ||
        err?.response?.data?.message ||
        err?.message ||
        'Server error'
      toast.error('Could not record the sale. Nothing was changed.', { description: errorMsg })
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Record a sale of ${item.name}`}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <Card className="w-full max-w-md overflow-hidden border-border bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-2.5">
            <div className="flex size-8 items-center justify-center rounded-lg border border-primary/30 bg-primary/10 text-primary">
              <Receipt className="size-4" />
            </div>
            <div>
              <h3 className="font-serif text-base font-semibold text-foreground">Record a sale</h3>
              <p className="text-xs text-muted-foreground">
                Stock is decremented and the takings journal is written.
              </p>
            </div>
          </div>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={isSaving}
            onClick={onClose}
            aria-label="Close"
            className="size-8 cursor-pointer rounded-full p-0 hover:bg-muted"
          >
            <X className="size-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
          <div className="flex items-center gap-3.5 rounded-xl border border-border bg-muted/20 p-3">
            {item.imageUrl ? (
              <img
                src={item.imageUrl}
                alt=""
                className="size-14 shrink-0 rounded-lg border border-border object-cover"
              />
            ) : null}
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-foreground">{item.name}</p>
              <p className="mt-0.5 font-mono text-[11px] uppercase tracking-wider text-muted-foreground">
                {item.sku || 'No SKU'} · {item.category}
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                {item.stockQuantity} in stock at {formatMoney(item.price)}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="sale-quantity" className="text-xs font-medium">
                Pieces sold
              </Label>
              <Input
                id="sale-quantity"
                type="number"
                min={1}
                max={item.stockQuantity}
                step={1}
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
                required
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="sale-price" className="text-xs font-medium">
                Unit price (LKR)
              </Label>
              <Input
                id="sale-price"
                type="number"
                min={0}
                step="0.01"
                value={unitPrice}
                onChange={(e) => setUnitPrice(e.target.value)}
                required
              />
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="sale-note" className="text-xs font-medium">
              Note (optional)
            </Label>
            <Textarea
              id="sale-note"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Client, alteration, or anything the register should show"
              rows={2}
              className="text-[13px]"
            />
          </div>

          <div className="flex items-center justify-between rounded-xl border border-border bg-muted/20 px-3.5 py-3">
            <div>
              <p className="text-[11px] uppercase tracking-wider text-muted-foreground">
                Sale total
              </p>
              <p className="font-serif text-lg font-semibold text-primary">{formatMoney(total)}</p>
            </div>
            <div className="text-right">
              <p className="text-[11px] uppercase tracking-wider text-muted-foreground">
                Stock after
              </p>
              <p className="text-sm font-semibold tabular-nums">{remaining}</p>
            </div>
          </div>

          {!inStock && quantityValid ? (
            <div className="flex items-start gap-2 rounded-lg border border-destructive/20 bg-destructive/10 p-3 text-xs text-destructive">
              <AlertTriangle className="mt-0.5 size-4 shrink-0" />
              <span>
                Only {item.stockQuantity} piece(s) are on hand. Lower the quantity or restock the
                piece first.
              </span>
            </div>
          ) : null}

          <div className="flex items-center justify-end gap-2.5 border-t border-border pt-4">
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isSaving}
              onClick={onClose}
              className="cursor-pointer"
            >
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={!canSubmit} className="cursor-pointer gap-1.5">
              {isSaving ? (
                <Loader2 className="size-3.5 animate-spin" />
              ) : (
                <Check className="size-3.5" />
              )}
              <span>{isSaving ? 'Recording...' : 'Record sale'}</span>
            </Button>
          </div>
        </form>
      </Card>
    </div>
  )
}
