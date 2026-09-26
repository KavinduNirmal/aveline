import {
  ArrowLeft,
  Pencil,
  ShoppingBag,
  QrCode,
  Trash2,
  Users,
  Layers,
  AlertTriangle,
  Package,
  PackageX,
  SlidersHorizontal,
  Calendar,
} from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { formatMoney } from '@/lib/format-money'
import { cn } from '@/lib/utils'

import { VisualAttributesBadge } from './VisualAttributesBadge'
import type { InventoryItemMock } from './mockData'

interface CatalogItemDetailProps {
  /** The piece, or `null` when the id in the URL names nothing the list carries. */
  item: InventoryItemMock | null
  isLoading: boolean
  onBack: () => void
  onEdit: (item: InventoryItemMock) => void
  onRecordSale: (item: InventoryItemMock) => void
  onDelete: (item: InventoryItemMock) => void
  onViewMatches: (item: InventoryItemMock) => void
  onComposeOutfit: (item: InventoryItemMock) => void
  onViewQr: (item: InventoryItemMock) => void
  onReduceStock: (item: InventoryItemMock) => void
  onMarkOutOfStock: (item: InventoryItemMock) => void
}

/** The stock state as a word and a tone, mirroring the card so the two cannot describe it differently. */
function stockState(item: InventoryItemMock): { label: string; variant: 'default' | 'secondary' | 'destructive' | 'outline' } {
  switch (item.status) {
    case 'available':
      return { label: `In stock · ${item.stockQuantity}`, variant: 'default' }
    case 'low_stock':
      return { label: `Low stock · ${item.stockQuantity}`, variant: 'secondary' }
    case 'reserved':
      return { label: `Reserved · ${item.stockQuantity}`, variant: 'destructive' }
    case 'archived':
      return { label: 'Archived', variant: 'outline' }
    default:
      return { label: item.status, variant: 'outline' }
  }
}

function formatDate(value?: string): string {
  if (!value) return 'Not recorded'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Not recorded' : date.toLocaleDateString()
}

/**
 * A catalog piece's information page.
 *
 * One piece, its money, what Aveline read from its photograph, and the actions the counter takes on
 * it: sell, edit, remove, print a floor tag, match against VIPs, or style a look around it. It is
 * reached by clicking the piece, and it is a real URL so a link to a piece survives a refresh.
 */
export function CatalogItemDetail({
  item,
  isLoading,
  onBack,
  onEdit,
  onRecordSale,
  onDelete,
  onViewMatches,
  onComposeOutfit,
  onViewQr,
  onReduceStock,
  onMarkOutOfStock,
}: CatalogItemDetailProps) {
  if (!item) {
    return (
      <div className="flex flex-col gap-4">
        <Button type="button" variant="ghost" size="sm" onClick={onBack} className="w-fit gap-1.5">
          <ArrowLeft className="size-4" aria-hidden />
          Back to catalog
        </Button>
        <Card className="flex flex-col items-center justify-center gap-2 border-dashed p-12 text-center">
          <Package className="size-10 text-muted-foreground/40" />
          <h2 className="font-serif text-lg font-medium">
            {isLoading ? 'Loading this piece…' : 'This piece is not in the catalog'}
          </h2>
          <p className="max-w-md text-xs text-muted-foreground">
            {isLoading
              ? 'Reading the catalog.'
              : 'It may have been removed, or the link may name a piece this boutique does not hold.'}
          </p>
        </Card>
      </div>
    )
  }

  const state = stockState(item)
  const margin = item.price - item.cost
  const marginPercent = item.price > 0 ? Math.round((margin / item.price) * 100) : null

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onBack}
          className="-ml-2 gap-1.5 text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" aria-hidden />
          Back to catalog
        </Button>

        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => onEdit(item)}>
            <Pencil className="size-3.5" aria-hidden />
            Edit piece
          </Button>
          <Button
            type="button"
            size="sm"
            className="gap-1.5"
            disabled={item.status === 'archived' || item.stockQuantity <= 0}
            onClick={() => onRecordSale(item)}
            title={
              item.stockQuantity <= 0
                ? 'Nothing is in stock to sell'
                : item.status === 'archived'
                  ? 'An archived piece cannot be sold'
                  : 'Sell this piece over the counter'
            }
          >
            <ShoppingBag className="size-3.5" aria-hidden />
            Record sale
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="gap-1.5 text-destructive hover:bg-destructive/10 hover:text-destructive"
            onClick={() => onDelete(item)}
          >
            <Trash2 className="size-3.5" aria-hidden />
            Remove
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,5fr)_minmax(0,6fr)]">
        {/* The garment */}
        <Card className="overflow-hidden border-border/80 bg-card p-0">
          <div className="relative aspect-4/3 w-full overflow-hidden bg-muted">
            {item.imageUrl ? (
              <img loading="lazy" decoding="async" src={item.imageUrl} alt={item.name} className="h-full w-full object-cover" />
            ) : (
              <div className="flex h-full w-full items-center justify-center text-xs text-muted-foreground">
                No photograph
              </div>
            )}
            <div className="absolute left-3 top-3">
              <Badge variant={state.variant} className="text-[10px]">
                {state.label}
              </Badge>
            </div>
          </div>

          <div className="flex flex-col gap-3 p-5">
            <VisualAttributesBadge
              color={item.color}
              colorHex={item.colorHex}
              fabric={item.fabric}
              pattern={item.pattern}
              style={item.style}
            />
            {item.sizes.length > 0 ? (
              <div className="flex flex-wrap items-center gap-1.5">
                <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
                  Sizes
                </span>
                {item.sizes.map((size) => (
                  <span
                    key={size}
                    className="rounded border border-border bg-muted/40 px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground"
                  >
                    {size}
                  </span>
                ))}
              </div>
            ) : null}

            <div className="flex flex-wrap items-center gap-2 border-t pt-3">
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="gap-1.5"
                onClick={() => onViewMatches(item)}
              >
                <Users className="size-3.5" aria-hidden />
                VIP matches
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                className="gap-1.5 text-muted-foreground hover:text-foreground"
                onClick={() => onComposeOutfit(item)}
              >
                <Layers className="size-3.5" aria-hidden />
                Style look
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                className="gap-1.5 text-muted-foreground hover:text-foreground"
                onClick={() => onViewQr(item)}
              >
                <QrCode className="size-3.5" aria-hidden />
                Floor tag
              </Button>
            </div>
          </div>
        </Card>

        {/* The record */}
        <div className="flex flex-col gap-4">
          <div>
            <p className="font-mono text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
              {item.sku || 'No SKU'} · {item.category}
            </p>
            <h1 className="mt-1.5 font-serif text-3xl font-medium tracking-tight">{item.name}</h1>
            {item.description ? (
              <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
                {item.description}
              </p>
            ) : (
              // A piece with no narrative is a state, not an empty paragraph.
              <p className="mt-3 text-sm italic text-muted-foreground">
                No description recorded for this piece.
              </p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <DetailStat label="Retail price" value={formatMoney(item.price)} />
            <DetailStat label="Atelier cost" value={formatMoney(item.cost)} />
            <DetailStat
              label="Margin"
              value={marginPercent === null ? formatMoney(margin) : `${formatMoney(margin)} · ${marginPercent}%`}
              tone={margin < 0 ? 'warning' : undefined}
            />
            <DetailStat
              label="In stock"
              value={String(item.stockQuantity)}
              tone={item.stockQuantity <= 2 ? 'warning' : undefined}
            />
          </div>

          {item.stockQuantity <= 2 ? (
            <div className="flex items-start gap-2 rounded-xl border border-warning/30 bg-warning/10 p-3.5 text-xs">
              <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />
              <span>
                {item.stockQuantity === 0
                  ? 'This piece is out of stock. Restock it or source another from an atelier.'
                  : `Only ${item.stockQuantity} piece(s) left. Consider reordering from the atelier.`}
              </span>
            </div>
          ) : null}

          {/* Stock edits. Neither is a sale: no money moves, so the takings journal is untouched. */}
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="gap-1.5"
              disabled={item.status === 'archived' || item.stockQuantity <= 0}
              onClick={() => onReduceStock(item)}
              title={
                item.stockQuantity <= 0
                  ? 'There is no stock to reduce'
                  : 'Remove pieces for damage, loss, or a corrected count'
              }
            >
              <SlidersHorizontal className="size-3.5" aria-hidden />
              Reduce stock
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="gap-1.5"
              disabled={item.status === 'archived' || item.stockQuantity <= 0}
              onClick={() => onMarkOutOfStock(item)}
              title={
                item.status === 'archived'
                  ? 'An archived piece cannot be adjusted'
                  : 'Set the count to zero and stop offering it'
              }
            >
              <PackageX className="size-3.5" aria-hidden />
              Mark out of stock
            </Button>
          </div>

          <Card className="flex flex-col gap-2 p-4">
            <h2 className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
              Record
            </h2>
            <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
              <div className="flex items-center justify-between gap-3 border-b border-border/60 py-1.5">
                <dt className="flex items-center gap-1.5 text-muted-foreground">
                  <Calendar className="size-3.5" aria-hidden />
                  Added
                </dt>
                <dd>{formatDate(item.createdAt)}</dd>
              </div>
              <div className="flex items-center justify-between gap-3 border-b border-border/60 py-1.5">
                <dt className="text-muted-foreground">Item id</dt>
                <dd className="truncate font-mono text-[11px]">{item.id}</dd>
              </div>
              <div className="flex items-center justify-between gap-3 border-b border-border/60 py-1.5">
                <dt className="text-muted-foreground">Colour</dt>
                <dd>{item.color || 'Not measured'}</dd>
              </div>
              <div className="flex items-center justify-between gap-3 border-b border-border/60 py-1.5">
                <dt className="text-muted-foreground">Fabric</dt>
                <dd>{item.fabric || 'Not measured'}</dd>
              </div>
            </dl>
          </Card>
        </div>
      </div>
    </div>
  )
}

/** One figure on the piece. `warning` is used only when the number itself is the warning. */
function DetailStat({
  label,
  value,
  tone,
}: {
  label: string
  value: string
  tone?: 'warning'
}) {
  return (
    <Card
      className={cn(
        'flex flex-col items-center gap-1 p-3 text-center',
        tone === 'warning' && 'border-warning/40 bg-warning/[0.06]',
      )}
    >
      <span className="text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </span>
      <span className="text-sm font-semibold tabular-nums">{value}</span>
    </Card>
  )
}
