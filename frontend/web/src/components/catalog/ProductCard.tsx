import { Sparkles, Users, Layers, Edit2 } from 'lucide-react'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { VisualAttributesBadge } from './VisualAttributesBadge'
import type { InventoryItemMock } from './mockData'

interface ProductCardProps {
  item: InventoryItemMock
  matchCount?: number
  onViewMatches: (item: InventoryItemMock) => void
  onComposeOutfit: (item: InventoryItemMock) => void
  onEditItem?: (item: InventoryItemMock) => void
}

export function ProductCard({
  item,
  matchCount = 0,
  onViewMatches,
  onComposeOutfit,
  onEditItem,
}: ProductCardProps) {
  const getStatusBadge = () => {
    switch (item.status) {
      case 'available':
        return (
          <Badge className="bg-emerald-600/90 hover:bg-emerald-600 text-white text-[10px] backdrop-blur-xs">
            In Stock ({item.stockQuantity})
          </Badge>
        )
      case 'low_stock':
        return (
          <Badge className="bg-amber-600/90 hover:bg-amber-600 text-white text-[10px] backdrop-blur-xs">
            Low Stock ({item.stockQuantity})
          </Badge>
        )
      case 'reserved':
        return (
          <Badge className="bg-rose-700/90 hover:bg-rose-700 text-white text-[10px] backdrop-blur-xs">
            Reserved
          </Badge>
        )
      case 'archived':
        return (
          <Badge variant="secondary" className="text-[10px]">
            Archived
          </Badge>
        )
      default:
        return null
    }
  }

  return (
    <Card className="group flex flex-col overflow-hidden border-border/80 bg-card transition-all duration-200 hover:border-border hover:shadow-md">
      {/* Product Image & Badges */}
      <div className="relative aspect-4/3 w-full overflow-hidden bg-muted">
        <img
          src={item.imageUrl}
          alt={item.name}
          className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-105"
        />

        {/* Top Status & Category Badges */}
        <div className="absolute top-2.5 left-2.5 flex flex-wrap gap-1.5">
          {getStatusBadge()}
          <Badge
            variant="outline"
            className="border-white/40 bg-black/40 text-white text-[10px] backdrop-blur-xs font-normal"
          >
            {item.category}
          </Badge>
        </div>

        {/* Top Right Quick Edit Button */}
        {onEditItem && (
          <Button
            size="sm"
            variant="secondary"
            onClick={() => onEditItem(item)}
            className="absolute top-2.5 right-2.5 size-7 rounded-full bg-background/80 p-0 text-muted-foreground opacity-0 backdrop-blur-xs transition-opacity group-hover:opacity-100 hover:text-foreground"
            title="Edit piece details"
          >
            <Edit2 className="size-3.5" />
          </Button>
        )}

        {/* Bottom AI Confidence Tag */}
        {item.confidenceScore && (
          <div className="absolute bottom-2 right-2 flex items-center gap-1 rounded-md bg-black/60 px-2 py-0.5 text-[10px] font-medium text-white backdrop-blur-xs">
            <Sparkles className="size-2.5 text-primary" />
            <span>{Math.round(item.confidenceScore * 100)}% Vision AI</span>
          </div>
        )}
      </div>

      {/* Card Content */}
      <div className="flex flex-1 flex-col p-4">
        {/* SKU & Price */}
        <div className="flex items-center justify-between text-xs text-muted-foreground mb-1">
          <span className="font-mono tracking-wider">{item.sku}</span>
          <span className="text-base font-semibold text-foreground">
            ${item.price.toLocaleString()}
          </span>
        </div>

        {/* Item Title */}
        <h3 className="font-serif text-sm font-medium text-foreground line-clamp-1 leading-snug">
          {item.name}
        </h3>

        {/* Description snippet if any */}
        {item.description && (
          <p className="mt-1 line-clamp-2 text-xs text-muted-foreground leading-relaxed">
            {item.description}
          </p>
        )}

        {/* Visual AI Attribute Strip */}
        <div className="mt-3 pt-2.5 border-t border-border/60">
          <VisualAttributesBadge
            color={item.color}
            colorHex={item.colorHex}
            fabric={item.fabric}
            pattern={item.pattern}
            style={item.style}
          />
        </div>

        {/* Sizes badges */}
        {item.sizes.length > 0 && (
          <div className="mt-2.5 flex flex-wrap items-center gap-1">
            <span className="text-[10px] text-muted-foreground mr-1">Sizes:</span>
            {item.sizes.map((sz) => (
              <span
                key={sz}
                className="rounded border border-border bg-muted/30 px-1.5 py-0.5 text-[10px] font-mono text-muted-foreground"
              >
                {sz}
              </span>
            ))}
          </div>
        )}

        {/* Action Buttons Row */}
        <div className="mt-auto pt-4 flex items-center gap-2">
          {/* VIP Matches Action */}
          <Button
            size="sm"
            variant="outline"
            className="flex-1 gap-1.5 text-xs h-8 rounded-lg border-border hover:bg-primary/5 hover:text-primary hover:border-primary/30"
            onClick={() => onViewMatches(item)}
          >
            <Users className="size-3.5" />
            <span>VIP Matches</span>
            {matchCount > 0 && (
              <span className="ml-0.5 flex size-4 items-center justify-center rounded-full bg-primary text-[10px] font-bold text-white">
                {matchCount}
              </span>
            )}
          </Button>

          {/* Compose Look Action */}
          <Button
            size="sm"
            variant="ghost"
            className="gap-1.5 text-xs h-8 rounded-lg text-muted-foreground hover:text-foreground"
            onClick={() => onComposeOutfit(item)}
            title="Style an outfit look with Elle around this hero piece"
          >
            <Layers className="size-3.5" />
            <span>Style Look</span>
          </Button>
        </div>
      </div>
    </Card>
  )
}
