import { Users, Layers, MoreHorizontal, Pencil, QrCode, Trash2, Eye, SlidersHorizontal, PackageX } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { formatMoney } from '@/lib/format-money'

import { VisualAttributesBadge } from './VisualAttributesBadge'
import type { InventoryItemMock } from './mockData'

interface ProductCardProps {
  item: InventoryItemMock
  matchCount?: number
  /** Opens the piece's information page. Absent means the card is a plain, non-navigating tile. */
  onOpen?: (item: InventoryItemMock) => void
  onViewMatches: (item: InventoryItemMock) => void
  onComposeOutfit: (item: InventoryItemMock) => void
  onEditItem?: (item: InventoryItemMock) => void
  onViewQr?: (item: InventoryItemMock) => void
  onDeleteItem?: (item: InventoryItemMock) => void
  /** Opens the reduce-stock dialog. Absent means the action is not offered. */
  onReduceStock?: (item: InventoryItemMock) => void
  /** Opens the mark-out-of-stock confirmation. Absent means the action is not offered. */
  onMarkOutOfStock?: (item: InventoryItemMock) => void
}

/** The stock state as a word and a tone. A measured zero is its own state, not "low". */
function stockState(item: InventoryItemMock): { label: string; tone: string } {
  switch (item.status) {
    case 'available':
      return { label: `In stock · ${item.stockQuantity}`, tone: 'bg-success/90 hover:bg-success' }
    case 'low_stock':
      return { label: `Low stock · ${item.stockQuantity}`, tone: 'bg-warning/90 hover:bg-warning' }
    case 'reserved':
      return { label: 'Reserved', tone: 'bg-destructive/90 hover:bg-destructive' }
    case 'archived':
      return { label: 'Archived', tone: '' }
    default:
      return { label: item.status, tone: '' }
  }
}

/**
 * One catalogue piece.
 *
 * The card leads with the garment, then its identity, then what Aveline read from the photograph.
 * Price is money and goes through the dashboard's one formatter, so a piece cannot be quoted in a
 * currency the rest of the surface does not use.
 *
 * There is deliberately **no confidence badge**. The list payload (`InventoryItemDto`) carries no
 * confidence score; the normaliser used to substitute 0.95, which made every card claim "95% Vision
 * AI" no matter what the analysis produced. A number nobody measured is worse than no number.
 */
export function ProductCard({
  item,
  matchCount = 0,
  onOpen,
  onViewMatches,
  onComposeOutfit,
  onEditItem,
  onViewQr,
  onDeleteItem,
  onReduceStock,
  onMarkOutOfStock,
}: ProductCardProps) {
  const state = stockState(item)
  const hasRowActions = Boolean(
    onEditItem || onViewQr || onDeleteItem || onReduceStock || onMarkOutOfStock,
  )

  // The garment itself is the affordance for opening the piece, so the image is one control and
  // the title is another. They are buttons rather than a click handler on the whole card because
  // the card also carries its own buttons, and nesting them would be invalid markup.
  const garment = item.imageUrl ? (
    <img
      src={item.imageUrl}
      alt={item.name}
      className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.03]"
    />
  ) : (
    // An absent photograph is a state, not a broken image.
    <div className="flex h-full w-full items-center justify-center text-xs text-muted-foreground">
      No photograph
    </div>
  )

  return (
    <Card className="group flex flex-col gap-0 overflow-hidden border-border/80 bg-card p-0 transition-shadow duration-200 hover:shadow-md">
      <div className="relative aspect-4/3 w-full overflow-hidden bg-muted">
        {onOpen ? (
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpen(item)}
            aria-label={`View details for ${item.name}`}
            className="h-full w-full cursor-pointer rounded-none p-0 hover:bg-transparent focus-visible:ring-inset"
          >
            {garment}
          </Button>
        ) : (
          garment
        )}

        <div className="absolute left-2.5 top-2.5 flex flex-wrap gap-1.5">
          <Badge className={`${state.tone} text-[10px] text-white backdrop-blur-xs`}>
            {state.label}
          </Badge>
        </div>

        {hasRowActions ? (
          <div className="absolute right-2.5 top-2.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  aria-label={`Actions for ${item.name}`}
                  className="size-7 rounded-full bg-background/90 p-0 text-muted-foreground shadow-2xs hover:bg-background hover:text-foreground"
                >
                  <MoreHorizontal className="size-3.5" aria-hidden />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {onOpen ? (
                  <DropdownMenuItem onSelect={() => onOpen(item)}>
                    <Eye className="size-3.5" aria-hidden /> View details
                  </DropdownMenuItem>
                ) : null}
                {onEditItem ? (
                  <DropdownMenuItem onSelect={() => onEditItem(item)}>
                    <Pencil className="size-3.5" aria-hidden /> Edit piece
                  </DropdownMenuItem>
                ) : null}
                {onReduceStock ? (
                  <DropdownMenuItem
                    onSelect={() => onReduceStock(item)}
                    disabled={item.status === 'archived' || item.stockQuantity <= 0}
                  >
                    <SlidersHorizontal className="size-3.5" aria-hidden /> Reduce stock
                  </DropdownMenuItem>
                ) : null}
                {onMarkOutOfStock ? (
                  <DropdownMenuItem
                    onSelect={() => onMarkOutOfStock(item)}
                    disabled={item.status === 'archived' || item.stockQuantity <= 0}
                  >
                    <PackageX className="size-3.5" aria-hidden /> Mark out of stock
                  </DropdownMenuItem>
                ) : null}
                {onViewQr ? (
                  <DropdownMenuItem onSelect={() => onViewQr(item)}>
                    <QrCode className="size-3.5" aria-hidden /> Floor tag QR
                  </DropdownMenuItem>
                ) : null}
                {onDeleteItem ? (
                  <DropdownMenuItem variant="destructive" onSelect={() => onDeleteItem(item)}>
                    <Trash2 className="size-3.5" aria-hidden /> Delete piece
                  </DropdownMenuItem>
                ) : null}
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        ) : null}
      </div>

      <div className="flex flex-1 flex-col gap-3 p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="truncate font-serif text-[15px] font-medium leading-snug">
              {onOpen ? (
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => onOpen(item)}
                  className="h-auto w-full justify-start whitespace-normal p-0 text-left font-serif text-[15px] font-medium leading-snug hover:bg-transparent hover:text-primary"
                >
                  {item.name}
                </Button>
              ) : (
                item.name
              )}
            </h3>
            <p className="mt-0.5 font-mono text-[11px] uppercase tracking-wider text-muted-foreground">
              {item.sku || 'No SKU'} · {item.category}
            </p>
          </div>
          <span className="shrink-0 text-sm font-semibold tabular-nums">
            {formatMoney(item.price)}
          </span>
        </div>

        {item.description ? (
          <p className="line-clamp-2 text-xs leading-relaxed text-muted-foreground">
            {item.description}
          </p>
        ) : null}

        <VisualAttributesBadge
          color={item.color}
          colorHex={item.colorHex}
          fabric={item.fabric}
          pattern={item.pattern}
          style={item.style}
        />

        {item.sizes.length > 0 ? (
          <div className="flex flex-wrap items-center gap-1">
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

        <div className="mt-auto flex items-center gap-2 border-t pt-3">
          <Button
            type="button"
            size="sm"
            variant="outline"
            className="flex-1 gap-1.5"
            onClick={() => onViewMatches(item)}
          >
            <Users className="size-3.5" aria-hidden />
            VIP matches
            {matchCount > 0 ? (
              <span className="ml-0.5 flex size-4 items-center justify-center rounded-full bg-primary text-[10px] font-bold text-primary-foreground">
                {matchCount}
              </span>
            ) : null}
          </Button>

          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="gap-1.5 text-muted-foreground hover:text-foreground"
            onClick={() => onComposeOutfit(item)}
            title="Style a look around this piece"
          >
            <Layers className="size-3.5" aria-hidden />
            Style look
          </Button>
        </div>
      </div>
    </Card>
  )
}
