import { useState, useId } from 'react'
import { Percent, ShieldAlert, ShieldCheck, Tag, Trash2 } from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { formatMoney } from '@/lib/format-money'
import {
  type PieceDiscountResponseDto,
  deletePieceDiscount,
  setPieceDiscount,
} from '@/lib/piece-discount-api'

interface PieceDiscountModalProps {
  isOpen: boolean
  onClose: () => void
  organizationId: string
  itemId: string
  itemName: string
  retailPrice: number
  wholesaleCost: number
  currentDiscount: PieceDiscountResponseDto | null
  onDiscountSaved: (discount: PieceDiscountResponseDto | null) => void
}

const PRESET_PERCENTAGES = [5, 10, 15, 20]
const MINIMUM_MARGIN_FLOOR = 0.25

export function PieceDiscountModal({
  isOpen,
  onClose,
  organizationId,
  itemId,
  itemName,
  retailPrice,
  wholesaleCost,
  currentDiscount,
  onDiscountSaved,
}: PieceDiscountModalProps) {
  const percentageInputId = useId()
  const initialPct = currentDiscount?.isActive
    ? Math.round(currentDiscount.discountPercentage * 100)
    : 10

  const [percentageStr, setPercentageStr] = useState<string>(String(initialPct))
  const [description, setDescription] = useState<string>(currentDiscount?.description || '')
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const parsedPct = Number.parseFloat(percentageStr)
  const discountRate = Number.isFinite(parsedPct) && parsedPct >= 0 ? parsedPct / 100 : 0
  const discountAmount = Math.round(retailPrice * discountRate * 100) / 100
  const netPromotionalPrice = Math.max(0, retailPrice - discountAmount)
  const grossProfit = Math.round((netPromotionalPrice - wholesaleCost) * 100) / 100
  const projectedMargin = netPromotionalPrice > 0 ? grossProfit / netPromotionalPrice : 0
  const isMarginSafe = projectedMargin >= MINIMUM_MARGIN_FLOOR

  const handleSave = async () => {
    if (!Number.isFinite(parsedPct) || parsedPct < 0 || parsedPct > 100) {
      setError('Please specify a valid discount percentage between 0% and 100%.')
      return
    }

    try {
      setIsSubmitting(true)
      setError(null)

      const saved = await setPieceDiscount(organizationId, {
        itemId,
        itemName,
        discountPercentage: discountRate,
        description: description.trim() || undefined,
      })

      onDiscountSaved(saved)
      onClose()
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Failed to save piece discount.')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleRemove = async () => {
    try {
      setIsSubmitting(true)
      setError(null)
      await deletePieceDiscount(organizationId, itemId)
      onDiscountSaved(null)
      onClose()
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Failed to remove piece discount.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-md sm:max-w-lg">
        <DialogHeader>
          <div className="flex items-center gap-2">
            <span className="flex size-8 items-center justify-center rounded-lg bg-primary/10 text-primary">
              <Tag className="size-4" aria-hidden />
            </span>
            <div>
              <DialogTitle className="font-serif text-xl">Allocate Piece Discount</DialogTitle>
              <DialogDescription className="text-xs text-muted-foreground">
                Set promotional discount for {itemName}. Lina will combine this with customer loyalty tiers.
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="flex flex-col gap-4 py-2">
          {error ? (
            <div className="rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-xs text-destructive">
              {error}
            </div>
          ) : null}

          {/* Percentage Input & Quick Presets */}
          <div className="flex flex-col gap-2">
            <Label htmlFor={percentageInputId} className="text-xs font-medium">
              Promotional Discount Percentage (%)
            </Label>
            <div className="flex items-center gap-2">
              <div className="relative flex-1">
                <Input
                  id={percentageInputId}
                  type="number"
                  min="0"
                  max="100"
                  step="1"
                  value={percentageStr}
                  onChange={(e) => {
                    setPercentageStr(e.target.value)
                    setError(null)
                  }}
                  placeholder="10"
                  className="pr-8 text-base font-semibold"
                />
                <Percent className="absolute right-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              </div>
              <div className="flex items-center gap-1">
                {PRESET_PERCENTAGES.map((pct) => (
                  <Button
                    key={pct}
                    type="button"
                    variant={parsedPct === pct ? 'default' : 'outline'}
                    size="sm"
                    className="h-9 px-2.5 text-xs"
                    onClick={() => {
                      setPercentageStr(String(pct))
                      setError(null)
                    }}
                  >
                    {pct}%
                  </Button>
                ))}
              </div>
            </div>
          </div>

          {/* Optional Note */}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="discount-note" className="text-xs font-medium text-muted-foreground">
              Promotion Note (optional)
            </Label>
            <Input
              id="discount-note"
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="e.g. Mid-season bridal showcase discount"
              className="text-xs"
            />
          </div>

          <Separator className="my-1" />

          {/* Live Commercial Impact Breakdown */}
          <Card className="flex flex-col gap-3 border-border/70 bg-muted/30 p-4">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Commercial Impact Preview
              </span>
              <Badge
                variant={isMarginSafe ? 'secondary' : 'destructive'}
                className="gap-1 text-[11px]"
              >
                {isMarginSafe ? (
                  <>
                    <ShieldCheck className="size-3 text-primary" />
                    <span>Margin Safe ({(projectedMargin * 100).toFixed(1)}%)</span>
                  </>
                ) : (
                  <>
                    <ShieldAlert className="size-3" />
                    <span>Below 25% Floor ({(projectedMargin * 100).toFixed(1)}%)</span>
                  </>
                )}
              </Badge>
            </div>

            <div className="grid grid-cols-2 gap-2 text-xs">
              <div className="flex justify-between border-b border-border/40 py-1">
                <span className="text-muted-foreground">Retail Price:</span>
                <span className="font-mono font-medium">{formatMoney(retailPrice)}</span>
              </div>
              <div className="flex justify-between border-b border-border/40 py-1">
                <span className="text-muted-foreground">Atelier Cost:</span>
                <span className="font-mono text-muted-foreground">{formatMoney(wholesaleCost)}</span>
              </div>
              <div className="flex justify-between border-b border-border/40 py-1">
                <span className="text-muted-foreground">Promotional Deduction:</span>
                <span className="font-mono font-medium text-destructive">
                  - {formatMoney(discountAmount)} ({discountRate > 0 ? `${Math.round(discountRate * 100)}%` : '0%'})
                </span>
              </div>
              <div className="flex justify-between border-b border-border/40 py-1">
                <span className="text-muted-foreground">Net Promotional Price:</span>
                <span className="font-mono font-semibold text-foreground">{formatMoney(netPromotionalPrice)}</span>
              </div>
            </div>

            <p className="text-[11px] leading-relaxed text-muted-foreground">
              {isMarginSafe
                ? `The net price preserves a ${(projectedMargin * 100).toFixed(1)}% profit margin (LKR ${grossProfit.toLocaleString('en-US', { minimumFractionDigits: 2 })} profit), clearing the boutique's 25% margin floor without requiring owner sign-off.`
                : `A ${(projectedMargin * 100).toFixed(1)}% profit margin falls below the boutique's 25% floor. Orders placed at this rate will require manager approval before confirmation.`}
            </p>
          </Card>
        </div>

        <DialogFooter className="flex flex-wrap items-center justify-between gap-2 sm:justify-between">
          <div>
            {currentDiscount?.isActive ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="gap-1.5 text-destructive hover:bg-destructive/10 hover:text-destructive"
                disabled={isSubmitting}
                onClick={handleRemove}
              >
                <Trash2 className="size-3.5" aria-hidden />
                Remove Discount
              </Button>
            ) : null}
          </div>
          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" size="sm" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button
              type="button"
              size="sm"
              className="gap-1.5"
              onClick={handleSave}
              disabled={isSubmitting}
            >
              <Tag className="size-3.5" aria-hidden />
              {isSubmitting ? 'Saving...' : 'Save Discount'}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
