import { useState, useMemo } from 'react'
import {
  Search,
  AlertTriangle,
  Plus,
  PackageCheck,
} from 'lucide-react'

import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ProductCard } from './ProductCard'
import type { InventoryItemMock } from './mockData'

interface InventoryTabProps {
  inventory: InventoryItemMock[]
  onAddNewPiece: () => void
  /** Absent means the grid is a plain list of tiles with no piece page to open. */
  onOpenItem?: (item: InventoryItemMock) => void
  onViewMatches: (item: InventoryItemMock) => void
  onComposeOutfit: (item: InventoryItemMock) => void
  onEditItem: (item: InventoryItemMock) => void
  onViewQr: (item: InventoryItemMock) => void
  onDeleteItem: (item: InventoryItemMock) => void
  onReduceStock?: (item: InventoryItemMock) => void
  onMarkOutOfStock?: (item: InventoryItemMock) => void
}

const CATEGORY_PILLS = [
  'All',
  'Sarees',
  'Lehengas',
  'Gowns',
  'Kurtas & Tunics',
  'Outerwear',
  'Drapes & Shawls',
]

export function InventoryTab({
  inventory,
  onAddNewPiece,
  onOpenItem,
  onViewMatches,
  onComposeOutfit,
  onEditItem,
  onViewQr,
  onDeleteItem,
  onReduceStock,
  onMarkOutOfStock,
}: InventoryTabProps) {
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedCategory, setSelectedCategory] = useState('All')
  const [statusFilter, setStatusFilter] = useState<'all' | 'available' | 'low_stock' | 'reserved'>('all')

  // Low stock counter
  const lowStockItems = useMemo(
    () => inventory.filter((item) => item.status === 'low_stock' || (item.stockQuantity > 0 && item.stockQuantity <= 2)),
    [inventory],
  )

  // Filtered items
  const filteredItems = useMemo(() => {
    return inventory.filter((item) => {
      // Category match
      if (selectedCategory !== 'All' && item.category !== selectedCategory) {
        return false
      }

      // Status match
      if (statusFilter !== 'all' && item.status !== statusFilter) {
        return false
      }

      // Search match (name, sku, color, fabric, style)
      if (searchQuery.trim()) {
        const query = searchQuery.toLowerCase()
        const matchName = item.name.toLowerCase().includes(query)
        const matchSku = item.sku.toLowerCase().includes(query)
        const matchColor = item.color.toLowerCase().includes(query)
        const matchFabric = item.fabric.toLowerCase().includes(query)
        const matchStyle = item.style.toLowerCase().includes(query)
        if (!matchName && !matchSku && !matchColor && !matchFabric && !matchStyle) {
          return false
        }
      }

      return true
    })
  }, [inventory, selectedCategory, statusFilter, searchQuery])

  return (
    <div className="flex flex-col gap-6">
      {/* Low Stock Warning Alert Banner if any */}
      {lowStockItems.length > 0 && (
        <div className="flex items-center justify-between rounded-xl border border-warning/30 bg-warning/10 p-3.5 text-xs text-warning-foreground dark:text-warning">
          <div className="flex items-center gap-2.5">
            <AlertTriangle className="size-4 shrink-0 text-warning dark:text-warning" />
            <span>
              <strong>{lowStockItems.length} pieces</strong> have low stock levels (2 units or fewer).
              Consider placing an atelier re-order.
            </span>
          </div>
          <Button
            size="sm"
            variant="outline"
            className="h-7 text-xs border-warning/40 bg-background/60 hover:bg-background text-warning-foreground dark:text-warning"
            onClick={() => setStatusFilter('low_stock')}
          >
            Filter Low Stock
          </Button>
        </div>
      )}

      {/* Search & Filters Bar */}
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        {/* Search Box */}
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search by piece name, SKU, color, or fabric..."
            className="pl-9 text-xs rounded-xl"
          />
        </div>

        {/* Status Filter & Add Button */}
        <div className="flex items-center gap-2.5">
          <Select
            value={statusFilter}
            onValueChange={(value) =>
              setStatusFilter(value as 'all' | 'available' | 'low_stock' | 'reserved')
            }
          >
            <SelectTrigger
              aria-label="Filter by availability"
              className="h-9 w-[11rem] rounded-xl text-xs"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Availability</SelectItem>
              <SelectItem value="available">In Stock Only</SelectItem>
              <SelectItem value="low_stock">Low Stock (≤2)</SelectItem>
              <SelectItem value="reserved">Reserved</SelectItem>
            </SelectContent>
          </Select>

          <Button
            type="button"
            size="sm"
            onClick={onAddNewPiece}
            className="gap-1.5 rounded-xl text-xs h-9 px-4 shadow-sm cursor-pointer"
          >
            <Plus className="size-4" />
            <span>Add Piece</span>
          </Button>
        </div>
      </div>

      {/* Category Filter Pills */}
      <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs">
        {CATEGORY_PILLS.map((cat) => (
          <Button
            key={cat}
            type="button"
            variant={selectedCategory === cat ? 'default' : 'secondary'}
            size="sm"
            onClick={() => setSelectedCategory(cat)}
            className="h-auto shrink-0 rounded-full px-3.5 py-1.5 text-xs font-medium"
          >
            {cat}
          </Button>
        ))}
      </div>

      {/* Products Grid */}
      {filteredItems.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center border-dashed border-border/80 bg-card/50">
          <PackageCheck className="size-12 text-muted-foreground/40 mb-3" />
          <h4 className="font-serif text-base font-medium">No pieces found</h4>
          <p className="text-xs text-muted-foreground max-w-sm mt-1 mb-4">
            No catalogue pieces match your selected category, search query, or status filter.
          </p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              setSearchQuery('')
              setSelectedCategory('All')
              setStatusFilter('all')
            }}
          >
            Clear Filters
          </Button>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {filteredItems.map((item) => {
            // The drawer generates matches on demand; the tab has no match source of its own, so
            // this badge used to read a hard zero from a state array nobody ever populated.
            const matchCount = 0
            return (
              <ProductCard
                key={item.id}
                item={item}
                matchCount={matchCount}
                onOpen={onOpenItem}
                onViewMatches={onViewMatches}
                onComposeOutfit={onComposeOutfit}
                onEditItem={onEditItem}
                onViewQr={onViewQr}
                onDeleteItem={onDeleteItem}
                onReduceStock={onReduceStock}
                onMarkOutOfStock={onMarkOutOfStock}
              />
            )
          })}
        </div>
      )}
    </div>
  )
}
