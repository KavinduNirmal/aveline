import { useState, useEffect } from 'react'
import {
  Shirt,
  Layers,
  Clock,
  Building2,
  Package,
  AlertTriangle,
  RefreshCw,
  TrendingUp,
  Trash2,
  X,
  Loader2,
  Plus,
} from 'lucide-react'
import { toast } from 'sonner'
import { Card } from '@/components/ui/card'

import { Button } from '@/components/ui/button'
import type { OrganizationProfileDto } from '@/types/organization'
import { formatCount, formatMoney } from '@/lib/format-money'
import { cn } from '@/lib/utils'

import type {
  InventoryItemMock,
  OutfitCompositionMock,
  SourcingRequestMock,
  SupplierMock,
} from './mockData'

import {
  fetchCatalogItems,
  fetchCatalogItem,
  fetchLookbooks,
  fetchSourcingRequests,
  fetchSuppliers,
  createCatalogItem,
  updateCatalogItem,
  deleteCatalogItem,
  updateSourcingRequestStatus,
  createSourcingRequest,
  updateLookbook,
  deleteLookbook,
} from '@/lib/catalog-api'
import type { CatalogSaleReceipt, UpdateLookbookPayload } from '@/types/catalog'

import { InventoryTab } from './InventoryTab'
import { LookbooksTab } from './LookbooksTab'
import { SourcingTab } from './SourcingTab'
import { SuppliersTab } from './SuppliersTab'
import { AddProductModal } from './AddProductModal'
import { CatalogItemDetail } from './CatalogItemDetail'
import { CustomerMatchesDrawer } from './CustomerMatchesDrawer'
import { ComposeOutfitModal } from './ComposeOutfitModal'
import { ItemQrModal } from './ItemQrModal'
import { RecordSaleModal } from './RecordSaleModal'
import { AdjustStockModal, type StockAdjustmentMode } from './AdjustStockModal'

export type CatalogSubTab = 'inventory' | 'lookbooks' | 'sourcing' | 'suppliers'

/**
 * One count on the catalog header. `value` is `null` when the server did not return the list, and
 * the tile says "not measured" rather than showing a zero nobody established.
 */
function CatalogStat({
  icon: Icon,
  label,
  value,
  hint,
  tone,
}: {
  icon: typeof Package
  label: string
  value: string | null
  hint?: string
  tone?: 'warning'
}) {
  return (
    <Card
      className={cn(
        // Centred: the label, the figure and its hint are one column, so the four cards read as a
        // row of equal tiles instead of four left-aligned blocks of different widths.
        'flex flex-col items-center justify-center gap-2 p-4 text-center',
        tone === 'warning' && 'border-warning/40 bg-warning/[0.06]',
      )}
    >
      <span
        className={cn(
          'flex size-9 shrink-0 items-center justify-center rounded-xl',
          tone === 'warning' ? 'bg-warning/15 text-warning' : 'bg-primary/10 text-primary',
        )}
      >
        <Icon className="size-4" aria-hidden />
      </span>
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </p>
        <p
          className={cn(
            'truncate text-lg font-semibold tabular-nums',
            value === null && 'text-sm font-normal italic text-muted-foreground',
          )}
        >
          {value ?? 'not measured'}
        </p>
        {hint ? <p className="truncate text-xs text-muted-foreground">{hint}</p> : null}
      </div>
    </Card>
  )
}

interface CatalogPanelProps {
  organization?: OrganizationProfileDto | null
  role?: string | null
  /**
   * The piece named by the URL, when the catalog is showing one piece's information page. The
   * shell owns the route so this panel stays router-free and testable on its own.
   */
  openItemId?: string | null
  onOpenItem?: (item: InventoryItemMock) => void
  onCloseItem?: () => void
  onOpenSalonForCustomer?: (customerId: string, clientName: string) => void
}

export function CatalogPanel({
  organization,
  role: _role,
  openItemId = null,
  onOpenItem,
  onCloseItem,
  onOpenSalonForCustomer,
}: CatalogPanelProps) {
  const [activeTab, setActiveTab] = useState<CatalogSubTab>('inventory')
  const [isLoading, setIsLoading] = useState<boolean>(false)
  // Whether the server actually returned each list. `null` renders "not measured"; a measured but
  // empty catalog is a real zero, and conflating the two is how a failed read becomes "0 pieces".
  const [inventoryMeasured, setInventoryMeasured] = useState(false)
  const [outfitsMeasured, setOutfitsMeasured] = useState(false)
  const [sourcingMeasured, setSourcingMeasured] = useState(false)
  const [suppliersMeasured, setSuppliersMeasured] = useState(false)

  // Reactive state initialized with real datasets from backend API
  const [inventory, setInventory] = useState<InventoryItemMock[]>([])
  const [outfits, setOutfits] = useState<OutfitCompositionMock[]>([])
  const [sourcingRequests, setSourcingRequests] = useState<SourcingRequestMock[]>([])
  const [suppliers, setSuppliers] = useState<SupplierMock[]>([])

  // Dialog & Drawer States
  const [addModalOpen, setAddModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<InventoryItemMock | null>(null)
  const [selectedMatchItem, setSelectedMatchItem] = useState<InventoryItemMock | null>(null)
  const [composeHeroItem, setComposeHeroItem] = useState<InventoryItemMock | null>(null)
  const [composeModalOpen, setComposeModalOpen] = useState(false)
  const [selectedQrItem, setSelectedQrItem] = useState<InventoryItemMock | null>(null)
  const [itemToDelete, setItemToDelete] = useState<InventoryItemMock | null>(null)
  const [isDeleting, setIsDeleting] = useState(false)
  const [saleItem, setSaleItem] = useState<InventoryItemMock | null>(null)
  const [stockAdjustment, setStockAdjustment] = useState<{
    item: InventoryItemMock
    mode: StockAdjustmentMode
  } | null>(null)

  const orgId = organization?.id

  // The piece the URL names, resolved against what the server returned. `undefined` while the list
  // is still loading is different from `null` after it arrived, which is why the detail view is
  // handed both the resolved item and the loading flag.
  const openItem = openItemId ? inventory.find((item) => item.id === openItemId) ?? null : null

  // Load catalog data from backend API when organization is mounted
  const loadCatalogData = async () => {
    if (!orgId) return
    setIsLoading(true)
    try {
      const [apiItems, apiLookbooks, apiSourcing, apiSuppliers] = await Promise.allSettled([
        fetchCatalogItems(orgId),
        fetchLookbooks(orgId),
        fetchSourcingRequests(orgId),
        fetchSuppliers(orgId),
      ])

      if (apiItems.status === 'fulfilled') {
        setInventory(apiItems.value as unknown as InventoryItemMock[])
        setInventoryMeasured(true)
      }

      if (apiLookbooks.status === 'fulfilled') {
        setOutfits(apiLookbooks.value as unknown as OutfitCompositionMock[])
        setOutfitsMeasured(true)
      }

      if (apiSourcing.status === 'fulfilled') {
        setSourcingRequests(apiSourcing.value as unknown as SourcingRequestMock[])
        setSourcingMeasured(true)
      }

      if (apiSuppliers.status === 'fulfilled') {
        setSuppliers(apiSuppliers.value as unknown as SupplierMock[])
        setSuppliersMeasured(true)
      }
    } catch {
      // Handled cleanly
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    if (orgId) {
      void loadCatalogData()
    }
  }, [orgId])

  // A deep link names one piece, which the list may not carry (the list is paged). Fetch it by id
  // so a link to a piece beyond the loaded page still opens it, rather than claiming it is missing.
  useEffect(() => {
    if (!openItemId || !orgId) return
    if (inventory.some((item) => item.id === openItemId)) return
    let cancelled = false
    fetchCatalogItem(orgId, openItemId)
      .then((item) => {
        if (cancelled) return
        setInventory((prev) =>
          prev.some((existing) => existing.id === item.id)
            ? prev
            : [item as unknown as InventoryItemMock, ...prev],
        )
      })
      .catch(() => {
        // The page already renders its own "not in the catalog" state; a failed lookup is that
        // state, not a second error surface.
      })
    return () => {
      cancelled = true
    }
  }, [openItemId, orgId, inventory])

  // Metrics Calculations
  const totalStockCount = inventory.reduce((sum, item) => sum + item.stockQuantity, 0)
  const totalValuation = inventory.reduce((sum, item) => sum + item.price * item.stockQuantity, 0)
  const lowStockCount = inventory.filter(
    (item) => item.status === 'low_stock' || (item.stockQuantity > 0 && item.stockQuantity <= 2),
  ).length
  const activeSourcingCount = sourcingRequests.filter(
    (req) => req.status !== 'fulfilled',
  ).length

  // Handlers
  const handleConfirmDelete = async () => {
    if (!itemToDelete) return
    if (!orgId) {
      toast.error('Cannot delete: this dashboard has no organisation id', {
        description: 'Reload the page. No request was sent.',
      })
      return
    }
    setIsDeleting(true)

    try {
      if (itemToDelete.id) {
        await deleteCatalogItem(orgId, itemToDelete.id)
      }
      setInventory((prev) => prev.filter((i) => i.id !== itemToDelete.id))
      toast.success('Piece deleted from catalog', {
        description: `${itemToDelete.sku} · ${itemToDelete.name}`,
      })
      const deletedId = itemToDelete.id
      setItemToDelete(null)
      if (editingItem?.id === deletedId) {
        setAddModalOpen(false)
        setEditingItem(null)
      }
      // Removing the piece that the information page is showing leaves that URL pointing at
      // nothing, so the reader is returned to the catalog rather than left on a dead page.
      if (openItemId === deletedId) {
        onCloseItem?.()
      }
    } catch {
      toast.error('Failed to delete catalog piece')
    } finally {
      setIsDeleting(false)
    }
  }

  /**
   * Applies a sale's receipt to the list. The server's remaining stock is the truth, so the row is
   * replaced from the receipt rather than decremented locally, and the status is the one the server
   * derived (a sale that empties a shelf leaves the piece reserved).
   */
  const handleSaleRecorded = (receipt: CatalogSaleReceipt) => {
    setInventory((prev) =>
      prev.map((item) =>
        item.id === receipt.itemId
          ? {
              ...item,
              stockQuantity: receipt.remainingStock,
              status: receipt.status as InventoryItemMock['status'],
            }
          : item,
      ),
    )
  }

  /**
   * Applies a manual stock adjustment. Like a sale, the server's count is what the row now holds,
   * so the list is replaced from the response rather than recomputed locally.
   */
  const handleStockAdjusted = (result: {
    itemId: string
    stockQuantity: number
    status: InventoryItemMock['status']
  }) => {
    setInventory((prev) =>
      prev.map((item) =>
        item.id === result.itemId
          ? { ...item, stockQuantity: result.stockQuantity, status: result.status }
          : item,
      ),
    )
  }

  const openStockAdjustment = (item: InventoryItemMock, mode: StockAdjustmentMode) =>
    setStockAdjustment({ item, mode })

  const handleSaveProduct = async (item: InventoryItemMock) => {
    // A missing organisation id is a hard error state, not a reason to write to a placeholder
    // tenant. The previous fallback wrote a real record into organisation
    // `00000000-…-0001`, which is worse than refusing.
    if (!orgId) {
      toast.error('Cannot save: this dashboard has no organisation id', {
        description: 'Reload the page. No request was sent.',
      })
      return
    }

    let savedItem = item

    try {
      if (editingItem) {
        const updated = await updateCatalogItem(orgId, editingItem.id, {
          itemName: item.name,
          category: item.category,
          color: item.color,
          // Only a measured hex is sent. An unmeasured colour is an omitted field, never an empty
          // string or a default the server would store as if it had been observed.
          colorHex: item.colorHex || undefined,
          fabric: item.fabric,
          style: item.style,
          sizes: item.sizes,
          price: item.price,
          cost: item.cost,
          quantity: item.stockQuantity,
          imageUrl: item.imageUrl,
          sku: item.sku,
          description: item.description,
        })
        if (updated) {
          savedItem = {
            ...item,
            id: updated.id,
            name: updated.name,
            sku: updated.sku || item.sku,
            category: updated.category,
            color: updated.color,
            // The server's own answer, taken as-is. Merging the previous value back in
            // (`updated.colorHex || item.colorHex`) resurrected a hex the row no longer stores, so a
            // colour could never be cleared and the card kept painting a stale swatch.
            colorHex: updated.colorHex,
            fabric: updated.fabric || item.fabric,
            style: updated.style || item.style,
            pattern: updated.pattern || item.pattern,
            price: updated.price,
            cost: updated.cost ?? item.cost,
            stockQuantity: updated.stockQuantity,
            imageUrl: updated.imageUrl || item.imageUrl,
            description: updated.description || item.description,
          }
        }
      } else {
        const created = await createCatalogItem(orgId, {
          itemName: item.name,
          category: item.category,
          color: item.color,
          // Only a measured hex is sent. An unmeasured colour is an omitted field, never an empty
          // string or a default the server would store as if it had been observed.
          colorHex: item.colorHex || undefined,
          fabric: item.fabric,
          style: item.style,
          sizes: item.sizes,
          price: item.price,
          cost: item.cost,
          quantity: item.stockQuantity,
          imageUrl: item.imageUrl,
          sku: item.sku,
          description: item.description,
        })
        if (created) {
          savedItem = {
            ...item,
            id: created.id,
            name: created.name,
            sku: created.sku || item.sku,
            category: created.category,
            color: created.color,
            // The server normalises the hex, so its answer is the truth — including `null` for a
            // value it refused. Do not fall back to what the form happened to hold.
            colorHex: created.colorHex,
            fabric: created.fabric || item.fabric,
            style: created.style || item.style,
            pattern: created.pattern || item.pattern,
            price: created.price,
            cost: created.cost ?? item.cost,
            stockQuantity: created.stockQuantity,
            imageUrl: created.imageUrl || item.imageUrl,
            description: created.description || item.description,
          }
        }
      }
      setInventory((prev) => {
        const exists = prev.some(
          (i) => i.id === savedItem.id || (editingItem && i.id === editingItem.id),
        )
        if (exists) {
          return prev.map((i) =>
            i.id === savedItem.id || (editingItem && i.id === editingItem.id) ? savedItem : i,
          )
        }
        return [savedItem, ...prev]
      })
      toast.success(editingItem ? 'Piece updated in database' : 'New piece saved to database', {
        description: `${savedItem.name} (${savedItem.sku || 'AVL'})`,
      })
    } catch (err: any) {
      console.error('Failed to save catalog item to backend:', err)
      const errorMsg =
        err?.response?.data?.message ||
        err?.response?.data?.error ||
        err?.message ||
        'Server error'
      toast.error('Could not save to database. Nothing was changed.', {
        description: `${errorMsg} The list still shows the server's state.`,
      })
      return
    }

    setEditingItem(null)
  }

  const handleEditProduct = (item: InventoryItemMock) => {
    setEditingItem(item)
    setAddModalOpen(true)
  }

  const handleViewMatches = (item: InventoryItemMock) => {
    setSelectedMatchItem(item)
  }

  const handleComposeOutfit = (item: InventoryItemMock) => {
    setComposeHeroItem(item)
    setComposeModalOpen(true)
  }

  const handleSaveOutfit = (newOutfit: OutfitCompositionMock) => {
    setOutfits((prev) => [newOutfit, ...prev])
    // The composer persists the look server-side before this callback runs, so a reload of the
    // list is what gives the local entry the row's real id and item detail. The optimistic add
    // above keeps it on screen when the server is unreachable.
    void loadCatalogData()
  }

  const handleUpdateOutfit = async (
    id: string,
    payload: UpdateLookbookPayload,
  ): Promise<boolean> => {
    if (!orgId) {
      toast.error('Cannot update: this dashboard has no organisation id', {
        description: 'Reload the page. No request was sent.',
      })
      return false
    }

    try {
      const updated = await updateLookbook(orgId, id, payload)
      setOutfits((prev) =>
        prev.map((outfit) => (outfit.id === id ? { ...outfit, ...updated } : outfit)),
      )
      toast.success('Lookbook updated', { description: updated.name })
      return true
    } catch (err: any) {
      const errorMsg =
        err?.response?.data?.error || err?.response?.data?.message || err?.message || 'Server error'
      toast.error('Could not update the lookbook. Nothing was changed.', {
        description: errorMsg,
      })
      return false
    }
  }

  const handleDeleteOutfit = async (id: string): Promise<boolean> => {
    if (!orgId) {
      toast.error('Cannot delete: this dashboard has no organisation id', {
        description: 'Reload the page. No request was sent.',
      })
      return false
    }

    try {
      await deleteLookbook(orgId, id)
      setOutfits((prev) => prev.filter((outfit) => outfit.id !== id))
      toast.success('Lookbook removed')
      return true
    } catch (err: any) {
      const errorMsg =
        err?.response?.data?.error || err?.response?.data?.message || err?.message || 'Server error'
      toast.error('Could not remove the lookbook. Nothing was changed.', {
        description: errorMsg,
      })
      return false
    }
  }

  const handleUpdateSourcingStatus = async (
    id: string,
    newStatus: SourcingRequestMock['status'],
  ) => {
    if (orgId) {
      try {
        await updateSourcingRequestStatus(orgId, id, newStatus)
      } catch {
        // Fallback to local optimistic update
      }
    }

    setSourcingRequests((prev) =>
      prev.map((req) => (req.id === id ? { ...req, status: newStatus } : req)),
    )
  }

  const handleAddSourcingRequest = async (newRequest: SourcingRequestMock) => {
    if (orgId) {
      try {
        await createSourcingRequest(orgId, {
          category: newRequest.category,
          color: newRequest.color,
          description: newRequest.itemDescription,
          targetPrice: newRequest.targetPrice,
          quantityNeeded: 1,
          urgency: 'medium',
        })
      } catch {
        // Fallback to local optimistic update
      }
    }

    setSourcingRequests((prev) => [newRequest, ...prev])
  }

  return (
    <div className="flex flex-col gap-6">
      {openItemId ? (
        <CatalogItemDetail
          item={openItem}
          isLoading={isLoading}
          onBack={() => onCloseItem?.()}
          onEdit={handleEditProduct}
          onRecordSale={(item) => setSaleItem(item)}
          onDelete={(item) => setItemToDelete(item)}
          onViewMatches={handleViewMatches}
          onComposeOutfit={handleComposeOutfit}
          onViewQr={(item) => setSelectedQrItem(item)}
          onReduceStock={(item) => openStockAdjustment(item, 'reduce')}
          onMarkOutOfStock={(item) => openStockAdjustment(item, 'out-of-stock')}
        />
      ) : (
        <>
      {/* Page header: what this section is, and one way to add a piece. */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization?.name ?? 'Catalog'}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Catalog</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            The boutique&apos;s pieces and what Aveline read from each photograph. A figure the
            server did not return reads &quot;not measured&quot; rather than zero.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            className="gap-1.5"
            onClick={() => {
              void loadCatalogData()
              toast.success('Catalog refreshed')
            }}
            disabled={isLoading || !orgId}
          >
            <RefreshCw className={cn('size-4', isLoading && 'animate-spin')} aria-hidden />
            Refresh
          </Button>
          <Button
            type="button"
            className="gap-1.5"
            onClick={() => {
              setEditingItem(null)
              setAddModalOpen(true)
            }}
          >
            <Plus className="size-4" aria-hidden />
            Add piece
          </Button>
        </div>
      </div>

      {/* Counts. `null` means the server did not return the list; a measured empty catalog is 0. */}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <CatalogStat
          icon={Package}
          label="Pieces"
          value={inventoryMeasured ? formatCount(inventory.length) : null}
          hint={inventoryMeasured ? `${formatCount(totalStockCount)} units in stock` : undefined}
        />
        <CatalogStat
          icon={TrendingUp}
          label="Stock value"
          value={inventoryMeasured ? formatMoney(totalValuation) : null}
          hint={inventoryMeasured ? 'Price at the recorded quantity' : undefined}
        />
        <CatalogStat
          icon={AlertTriangle}
          label="Low stock"
          value={inventoryMeasured ? formatCount(lowStockCount) : null}
          hint={lowStockCount > 0 ? 'Two or fewer pieces left' : undefined}
          tone={lowStockCount > 0 ? 'warning' : undefined}
        />
        <CatalogStat
          icon={Clock}
          label="Sourcing"
          value={sourcingMeasured ? formatCount(activeSourcingCount) : null}
          hint={sourcingMeasured ? `${formatCount(sourcingRequests.length)} requests on file` : undefined}
        />
      </div>

      {/* One section switcher, one selected state. */}
      <div className="flex gap-1 overflow-x-auto border-b">
        {(
          [
            { id: 'inventory', label: 'Pieces', icon: Shirt, count: inventory.length, measured: inventoryMeasured },
            { id: 'lookbooks', label: 'Lookbooks', icon: Layers, count: outfits.length, measured: outfitsMeasured },
            { id: 'sourcing', label: 'Sourcing', icon: Clock, count: sourcingRequests.length, measured: sourcingMeasured },
            { id: 'suppliers', label: 'Ateliers', icon: Building2, count: suppliers.length, measured: suppliersMeasured },
          ] as const
        ).map((tab) => {
          const active = activeTab === tab.id
          return (
            <Button
              key={tab.id}
              type="button"
              variant="ghost"
              aria-current={active ? 'page' : undefined}
              onClick={() => setActiveTab(tab.id)}
              className={cn(
                '-mb-px h-auto shrink-0 gap-2 rounded-none border-b-2 bg-transparent px-4 py-2.5 text-sm font-medium hover:bg-transparent',
                active
                  ? 'border-primary font-semibold text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground',
              )}
            >
              <tab.icon className="size-4" aria-hidden />
              {tab.label}
              <span className="text-xs tabular-nums text-muted-foreground">
                {tab.measured ? tab.count : '—'}
              </span>
            </Button>
          )
        })}
      </div>


      {/* Active Tab View */}
      <div className="pt-2">
        {activeTab === 'inventory' && (
          <InventoryTab
            inventory={inventory}
            onAddNewPiece={() => {
              setEditingItem(null)
              setAddModalOpen(true)
            }}
            onOpenItem={onOpenItem}
            onViewMatches={handleViewMatches}
            onComposeOutfit={handleComposeOutfit}
            onEditItem={handleEditProduct}
            onViewQr={(item) => setSelectedQrItem(item)}
            onDeleteItem={(item) => setItemToDelete(item)}
            onReduceStock={(item) => openStockAdjustment(item, 'reduce')}
            onMarkOutOfStock={(item) => openStockAdjustment(item, 'out-of-stock')}
          />
        )}

        {activeTab === 'lookbooks' && (
          <LookbooksTab
            outfits={outfits}
            onComposeLook={() => {
              setComposeHeroItem(inventory[0] ?? null)
              setComposeModalOpen(true)
            }}
            onUpdateLookbook={handleUpdateOutfit}
            onDeleteLookbook={handleDeleteOutfit}
          />
        )}

        {activeTab === 'sourcing' && (
          <SourcingTab
            sourcingRequests={sourcingRequests}
            suppliers={suppliers}
            onUpdateStatus={handleUpdateSourcingStatus}
            onAddRequest={handleAddSourcingRequest}
          />
        )}

        {activeTab === 'suppliers' && <SuppliersTab suppliers={suppliers} />}
      </div>
        </>
      )}

      {/* Add / Edit Piece Modal */}
      <AddProductModal
        open={addModalOpen}
        organizationId={orgId}
        organizationSlug={organization?.slug}
        onClose={() => {
          setAddModalOpen(false)
          setEditingItem(null)
        }}
        onSave={handleSaveProduct}
        onDelete={(item) => setItemToDelete(item)}
        editingItem={editingItem}
      />

      {/* Customer Matches Drawer */}
      <CustomerMatchesDrawer
        item={selectedMatchItem}
        organizationId={orgId}
        open={selectedMatchItem !== null}
        onClose={() => setSelectedMatchItem(null)}
        onOpenSalon={(customerId, clientName) => {
          if (onOpenSalonForCustomer) {
            onOpenSalonForCustomer(customerId, clientName)
          }
        }}
      />

      {/* Compose Outfit Modal */}
      <ComposeOutfitModal
        open={composeModalOpen}
        heroItem={composeHeroItem}
        inventory={inventory}
        organizationId={orgId}
        onClose={() => {
          setComposeModalOpen(false)
          setComposeHeroItem(null)
        }}
        onSaveOutfit={handleSaveOutfit}
      />

      {/* Item QR Floor Tag Modal */}
      <ItemQrModal
        item={selectedQrItem}
        open={selectedQrItem !== null}
        organizationId={orgId}
        organizationSlug={organization?.slug}
        onClose={() => setSelectedQrItem(null)}
      />

      {/* Counter Sale Modal */}
      <RecordSaleModal
        open={saleItem !== null}
        item={saleItem}
        organizationId={orgId}
        onClose={() => setSaleItem(null)}
        onRecorded={handleSaleRecorded}
      />

      {/* Manual Stock Adjustment Modal */}
      <AdjustStockModal
        open={stockAdjustment !== null}
        item={stockAdjustment?.item ?? null}
        mode={stockAdjustment?.mode ?? 'reduce'}
        organizationId={orgId}
        onClose={() => setStockAdjustment(null)}
        onAdjusted={handleStockAdjusted}
      />

      {/* Delete Confirmation Modal */}
      {itemToDelete && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label="Delete catalog piece"
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4 animate-in fade-in duration-200"
        >
          <Card className="w-full max-w-md overflow-hidden border-border bg-card shadow-2xl">
            <div className="flex items-center justify-between px-6 py-4 border-b border-border bg-destructive/10 shrink-0">
              <div className="flex items-center gap-2.5">
                <div className="flex size-8 items-center justify-center rounded-lg bg-destructive/20 text-destructive border border-destructive/30">
                  <Trash2 className="size-4" />
                </div>
                <div>
                  <h3 className="font-serif text-base font-semibold text-foreground">
                    Delete Catalog Piece
                  </h3>
                  <p className="text-xs text-muted-foreground">
                    This action cannot be undone
                  </p>
                </div>
              </div>
              <Button
                size="sm"
                variant="ghost"
                disabled={isDeleting}
                onClick={() => setItemToDelete(null)}
                className="size-8 p-0 rounded-full hover:bg-muted text-muted-foreground hover:text-foreground cursor-pointer"
              >
                <X className="size-4" />
              </Button>
            </div>

            <div className="flex flex-col p-6 gap-4">
              <div className="flex items-center gap-3.5 p-3 rounded-xl border border-border/80 bg-muted/20">
                {itemToDelete.imageUrl && (
                  <img
                    src={itemToDelete.imageUrl}
                    alt={itemToDelete.name}
                    className="size-16 rounded-lg object-cover border border-border shrink-0 shadow-2xs"
                  />
                )}
                <div className="min-w-0 flex-1">
                  <p className="text-xs font-semibold text-foreground truncate">
                    {itemToDelete.name}
                  </p>
                  <div className="flex items-center gap-2 mt-1">
                    <span className="text-[11px] font-mono text-muted-foreground bg-muted/60 px-1.5 py-0.5 rounded">
                      {itemToDelete.sku}
                    </span>
                    <span className="text-xs font-semibold text-foreground">
                      {formatMoney(itemToDelete.price)}
                    </span>
                  </div>
                  <span className="text-[10px] text-muted-foreground mt-0.5 block">
                    {itemToDelete.category} · Stock: {itemToDelete.stockQuantity}
                  </span>
                </div>
              </div>

              <div className="rounded-lg bg-destructive/10 border border-destructive/20 p-3 text-xs text-destructive flex items-start gap-2">
                <AlertTriangle className="size-4 shrink-0 mt-0.5" />
                <p>
                  Are you sure you want to delete this piece? It will be removed from active inventory, lookbook selections, and floor scans.
                </p>
              </div>
            </div>

            <div className="flex items-center justify-end gap-2.5 px-6 py-3.5 border-t border-border bg-card shrink-0">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isDeleting}
                onClick={() => setItemToDelete(null)}
                className="cursor-pointer"
              >
                Cancel
              </Button>
              <Button
                type="button"
                variant="destructive"
                size="sm"
                disabled={isDeleting}
                onClick={handleConfirmDelete}
                className="gap-1.5 cursor-pointer"
              >
                {isDeleting ? <Loader2 className="size-3.5 animate-spin" /> : <Trash2 className="size-3.5" />}
                <span>{isDeleting ? 'Deleting...' : 'Delete Piece'}</span>
              </Button>
            </div>
          </Card>
        </div>
      )}
    </div>
  )
}
