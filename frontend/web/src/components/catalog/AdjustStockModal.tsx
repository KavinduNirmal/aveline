import { useEffect, useState } from 'react'
import { AlertTriangle, Check, Loader2, PackageX, SlidersHorizontal, X } from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { updateCatalogItem } from '@/lib/catalog-api'

import type { InventoryItemMock } from './mockData'

export type StockAdjustmentMode = 'reduce' | 'out-of-stock'

interface AdjustStockModalProps {
  open: boolean
  item: InventoryItemMock | null
  organizationId?: string
  mode: StockAdjustmentMode
  onClose: () => void
  /** Called with the server's new stock and status once the adjustment is saved. */
  onAdjusted: (result: { itemId: string; stockQuantity: number; status: InventoryItemMock['status'] }) => void
}

/** The stock-derived status the drawer and the counter sale already use, in one expression. */
function statusForStock(quantity: number): InventoryItemMock['status'] {
  return quantity <= 0 ? 'reserved' : quantity <= 2 ? 'low_stock' : 'available'
}

/**
 * A manual stock adjustment, in one of two shapes.
 *
 * `out-of-stock` is the blunt one: the piece is gone, so the quantity becomes zero and the row is
 * reserved. `reduce` is the measured one: shrink the count by a number the operator states — a
 * damaged piece, a return to the atelier, a miscount.
 *
 * Neither is a sale. No money moves and nothing is written to the takings journal; a sale is the
 * separate action that records both. This only edits the catalog row, through the same item update
 * the edit drawer uses.
 */
export function AdjustStockModal({
  open,
  item,
  organizationId,
  mode,
  onClose,
  onAdjusted,
}: AdjustStockModalProps) {
  const [reduceBy, setReduceBy] = useState('1')
  const [isSaving, setIsSaving] = useState(false)

  useEffect(() => {
    if (open) {
      setReduceBy('1')
      setIsSaving(false)
    }
  }, [open, item, mode])

  if (!open || !item) return null

  const isOutOfStock = mode === 'out-of-stock'
  const parsedReduceBy = Number.parseInt(reduceBy, 10)
  const reduceValid =
    Number.isInteger(parsedReduceBy) && parsedReduceBy > 0 && parsedReduceBy <= item.stockQuantity
  const nextQuantity = isOutOfStock ? 0 : item.stockQuantity - (reduceValid ? parsedReduceBy : 0)
  const canSubmit = isOutOfStock ? item.stockQuantity > 0 && !isSaving : reduceValid && !isSaving

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!organizationId) {
      toast.error('Cannot adjust stock: this dashboard has no organisation id', {
        description: 'Reload the page. No request was sent.',
      })
      return
    }
    if (item.status === 'archived') {
      toast.error('An archived piece cannot be adjusted', {
        description: 'Restore it to the catalog first.',
      })
      return
    }
    if (!canSubmit) {
      toast.error(
        isOutOfStock
          ? 'This piece is already out of stock'
          : `Enter a whole number from 1 to ${item.stockQuantity}`,
      )
      return
    }

    const status = statusForStock(nextQuantity)
    setIsSaving(true)
    try {
      const updated = await updateCatalogItem(organizationId, item.id, {
        quantity: nextQuantity,
        status,
      })
      toast.success(isOutOfStock ? 'Marked out of stock' : 'Stock reduced', {
        description: `${item.name} · ${updated.stockQuantity ?? nextQuantity} in stock`,
      })
      onAdjusted({
        itemId: item.id,
        stockQuantity: updated.stockQuantity ?? nextQuantity,
        status: (updated.status as InventoryItemMock['status']) ?? status,
      })
      onClose()
    } catch (err: any) {
      const errorMsg =
        err?.response?.data?.error || err?.response?.data?.message || err?.message || 'Server error'
      toast.error('Could not adjust the stock. Nothing was changed.', { description: errorMsg })
    } finally {
      setIsSaving(false)
    }
  }

  const Icon = isOutOfStock ? PackageX : SlidersHorizontal

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={isOutOfStock ? `Mark ${item.name} out of stock` : `Reduce stock for ${item.name}`}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <Card className="w-full max-w-md overflow-hidden border-border bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-2.5">
            <div className="flex size-8 items-center justify-center rounded-lg border border-primary/30 bg-primary/10 text-primary">
              <Icon className="size-4" />
            </div>
            <div>
              <h3 className="font-serif text-base font-semibold text-foreground">
                {isOutOfStock ? 'Mark out of stock' : 'Reduce stock'}
              </h3>
              <p className="text-xs text-muted-foreground">
                Edits the catalog count only. No sale is recorded.
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
            className="size-8 rounded-full p-0 hover:bg-muted"
          >
            <X className="size-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
          <div className="flex items-center gap-3.5 rounded-xl border border-border bg-muted/20 p-3">
            {item.imageUrl ? (
              <img
                loading="lazy"
                decoding="async"
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
                {item.stockQuantity} in stock
              </p>
            </div>
          </div>

          {isOutOfStock ? (
            <div className="flex items-start gap-2 rounded-lg border border-warning/30 bg-warning/10 p-3 text-xs">
              <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />
              <span>
                The count becomes 0 and the piece is marked reserved, so it stops being offered.
                Set a new count from the edit drawer when it returns.
              </span>
            </div>
          ) : (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="reduce-quantity" className="text-xs font-medium">
                Pieces to remove
              </Label>
              <Input
                id="reduce-quantity"
                type="number"
                min={1}
                max={item.stockQuantity}
                step={1}
                value={reduceBy}
                onChange={(e) => setReduceBy(e.target.value)}
                required
              />
              <p className="text-[11px] text-muted-foreground">
                Damaged, lost, returned to the atelier, or a corrected count.
              </p>
            </div>
          )}

          <div className="flex items-center justify-between rounded-xl border border-border bg-muted/20 px-3.5 py-3">
            <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
              Stock before
            </span>
            <span className="text-sm font-semibold tabular-nums">{item.stockQuantity}</span>
            <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
              Stock after
            </span>
            <span className="text-sm font-semibold tabular-nums">{nextQuantity}</span>
          </div>

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
            <Button
              type="submit"
              size="sm"
              disabled={!canSubmit}
              variant={isOutOfStock ? 'destructive' : 'default'}
              className="cursor-pointer gap-1.5"
            >
              {isSaving ? (
                <Loader2 className="size-3.5 animate-spin" />
              ) : (
                <Check className="size-3.5" />
              )}
              <span>
                {isSaving
                  ? 'Saving...'
                  : isOutOfStock
                    ? 'Mark out of stock'
                    : 'Reduce stock'}
              </span>
            </Button>
          </div>
        </form>
      </Card>
    </div>
  )
}
