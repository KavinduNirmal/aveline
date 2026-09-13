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
import { ProductCard } from './ProductCard'
import type { CustomerMatchMock, InventoryItemMock } from './mockData'

interface InventoryTabProps {
  inventory: InventoryItemMock[]
  matches: CustomerMatchMock[]
  onAddNewPiece: () => void
  onViewMatches: (item: InventoryItemMock) => void
  onComposeOutfit: (item: InventoryItemMock) => void
  onEditItem: (item: InventoryItemMock) => void
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
  matches,
  onAddNewPiece,
  onViewMatches,
  onComposeOutfit,
  onEditItem,
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
    <div className="space-y-6">
      {/* Low Stock Warning Alert Banner if any */}
      {lowStockItems.length > 0 && (
        <div className="flex items-center justify-between rounded-xl border border-amber-500/30 bg-amber-500/10 p-3.5 text-xs text-amber-900 dark:text-amber-200">
          <div className="flex items-center gap-2.5">
            <AlertTriangle className="size-4 shrink-0 text-amber-600 dark:text-amber-400" />
            <span>
              <strong>{lowStockItems.length} pieces</strong> have low stock levels (2 units or fewer).
              Consider placing an atelier re-order.
            </span>
          </div>
          <Button
            size="sm"
            variant="outline"
            className="h-7 text-xs border-amber-500/40 bg-background/60 hover:bg-background text-amber-900 dark:text-amber-100"
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
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as any)}
            className="h-9 rounded-xl border border-input bg-background px-3 text-xs text-foreground"
          >
            <option value="all">All Availability</option>
            <option value="available">In Stock Only</option>
            <option value="low_stock">Low Stock (≤2)</option>
            <option value="reserved">Reserved</option>
          </select>

          <Button
            size="sm"
            onClick={onAddNewPiece}
            className="gap-1.5 rounded-xl text-xs h-9 px-4 shadow-sm"
          >
            <Plus className="size-4" />
            <span>Add Piece</span>
          </Button>
        </div>
      </div>

      {/* Category Filter Pills */}
      <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs">
        {CATEGORY_PILLS.map((cat) => (
          <button
            key={cat}
            type="button"
            onClick={() => setSelectedCategory(cat)}
            className={`rounded-full px-3.5 py-1.5 text-xs font-medium transition-colors shrink-0 ${
              selectedCategory === cat
                ? 'bg-primary text-white shadow-xs'
                : 'bg-muted/60 text-muted-foreground hover:bg-muted hover:text-foreground'
            }`}
          >
            {cat}
          </button>
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
            const matchCount = matches.filter((m) => m.itemId === item.id).length
            return (
              <ProductCard
                key={item.id}
                item={item}
                matchCount={matchCount}
                onViewMatches={onViewMatches}
                onComposeOutfit={onComposeOutfit}
                onEditItem={onEditItem}
              />
            )
          })}
        </div>
      )}
    </div>
  )
}
