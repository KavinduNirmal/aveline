import { useState, useEffect } from 'react'
import {
  Shirt,
  Layers,
  Clock,
  Building2,
  Sparkles,
  Package,
  AlertTriangle,
  RefreshCw,
  TrendingUp,
} from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import type { OrganizationProfileDto } from '@/types/organization'

import type {
  InventoryItemMock,
  CustomerMatchMock,
  OutfitCompositionMock,
  SourcingRequestMock,
  SupplierMock,
} from './mockData'

import {
  fetchCatalogItems,
  fetchLookbooks,
  fetchSourcingRequests,
  fetchSuppliers,
  createCatalogItem,
  updateCatalogItem,
  updateSourcingRequestStatus,
  createSourcingRequest,
} from '@/lib/catalog-api'

import { InventoryTab } from './InventoryTab'
import { LookbooksTab } from './LookbooksTab'
import { SourcingTab } from './SourcingTab'
import { SuppliersTab } from './SuppliersTab'
import { AddProductModal } from './AddProductModal'
import { CustomerMatchesDrawer } from './CustomerMatchesDrawer'
import { ComposeOutfitModal } from './ComposeOutfitModal'

export type CatalogSubTab = 'inventory' | 'lookbooks' | 'sourcing' | 'suppliers'

interface CatalogPanelProps {
  organization?: OrganizationProfileDto | null
  role?: string | null
  onOpenSalonForCustomer?: (customerId: string, clientName: string) => void
}

export function CatalogPanel({
  organization,
  role: _role,
  onOpenSalonForCustomer,
}: CatalogPanelProps) {
  const [activeTab, setActiveTab] = useState<CatalogSubTab>('inventory')
  const [isLoading, setIsLoading] = useState<boolean>(false)

  // Reactive state initialized with real datasets from backend API
  const [inventory, setInventory] = useState<InventoryItemMock[]>([])
  const [matches] = useState<CustomerMatchMock[]>([])
  const [outfits, setOutfits] = useState<OutfitCompositionMock[]>([])
  const [sourcingRequests, setSourcingRequests] = useState<SourcingRequestMock[]>([])
  const [suppliers, setSuppliers] = useState<SupplierMock[]>([])

  // Dialog & Drawer States
  const [addModalOpen, setAddModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<InventoryItemMock | null>(null)
  const [selectedMatchItem, setSelectedMatchItem] = useState<InventoryItemMock | null>(null)
  const [composeHeroItem, setComposeHeroItem] = useState<InventoryItemMock | null>(null)
  const [composeModalOpen, setComposeModalOpen] = useState(false)

  const orgId = organization?.id

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
      }

      if (apiLookbooks.status === 'fulfilled') {
        setOutfits(apiLookbooks.value as unknown as OutfitCompositionMock[])
      }

      if (apiSourcing.status === 'fulfilled') {
        setSourcingRequests(apiSourcing.value as unknown as SourcingRequestMock[])
      }

      if (apiSuppliers.status === 'fulfilled') {
        setSuppliers(apiSuppliers.value as unknown as SupplierMock[])
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
  const handleSaveProduct = async (item: InventoryItemMock) => {
    if (orgId) {
      try {
        if (editingItem) {
          await updateCatalogItem(orgId, editingItem.id, {
            itemName: item.name,
            category: item.category,
            color: item.color,
            sizes: item.sizes,
            price: item.price,
            quantity: item.stockQuantity,
            imageUrl: item.imageUrl,
            sku: item.sku,
            description: item.description,
          })
        } else {
          await createCatalogItem(orgId, {
            itemName: item.name,
            category: item.category,
            color: item.color,
            sizes: item.sizes,
            price: item.price,
            quantity: item.stockQuantity,
            imageUrl: item.imageUrl,
            sku: item.sku,
            description: item.description,
          })
        }
      } catch {
        // Continue with local optimistic update
      }
    }

    setInventory((prev) => {
      const exists = prev.some((i) => i.id === item.id)
      if (exists) {
        return prev.map((i) => (i.id === item.id ? item : i))
      }
      return [item, ...prev]
    })
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
    <div className="space-y-6">
      {/* Header & Metrics Strip */}
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="font-serif text-2xl font-bold tracking-tight text-foreground">
              Catalog & Visual Intelligence
            </h2>
            <Badge
              variant="outline"
              className="border-primary/20 bg-primary/5 text-primary text-[10px] font-medium gap-1"
            >
              <Sparkles className="size-3" />
              Vision AI Active
            </Badge>
            {orgId && (
              <Button
                variant="ghost"
                size="sm"
                className="size-7 p-0 text-muted-foreground hover:text-foreground"
                onClick={() => {
                  void loadCatalogData()
                  toast.success('Catalog refreshed')
                }}
                title="Refresh catalog from server"
              >
                <RefreshCw className={`size-3.5 ${isLoading ? 'animate-spin' : ''}`} />
              </Button>
            )}
          </div>
          <p className="text-xs text-muted-foreground mt-0.5">
            Manage boutique inventory, extract visual fabric tags, match VIP clients, and track atelier sourcing
          </p>
        </div>

        {/* Quick Metric Chips */}
        <div className="flex flex-wrap items-center gap-2.5">
          <div className="flex items-center gap-2 rounded-xl border border-border/70 bg-card px-3 py-1.5 shadow-2xs">
            <Package className="size-4 text-primary" />
            <div className="text-left">
              <span className="text-[10px] text-muted-foreground block leading-none">In Stock</span>
              <span className="text-xs font-semibold text-foreground font-mono">
                {totalStockCount} units
              </span>
            </div>
          </div>

          <div className="flex items-center gap-2 rounded-xl border border-border/70 bg-card px-3 py-1.5 shadow-2xs">
            <TrendingUp className="size-4 text-emerald-600" />
            <div className="text-left">
              <span className="text-[10px] text-muted-foreground block leading-none">Valuation</span>
              <span className="text-xs font-semibold text-foreground font-mono">
                ${totalValuation.toLocaleString()}
              </span>
            </div>
          </div>

          {lowStockCount > 0 && (
            <div className="flex items-center gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 py-1.5 shadow-2xs">
              <AlertTriangle className="size-4 text-amber-600" />
              <div className="text-left">
                <span className="text-[10px] text-amber-800 dark:text-amber-300 block leading-none">
                  Low Stock
                </span>
                <span className="text-xs font-semibold text-amber-900 dark:text-amber-200 font-mono">
                  {lowStockCount} items
                </span>
              </div>
            </div>
          )}

          <div className="flex items-center gap-2 rounded-xl border border-border/70 bg-card px-3 py-1.5 shadow-2xs">
            <Clock className="size-4 text-primary" />
            <div className="text-left">
              <span className="text-[10px] text-muted-foreground block leading-none">Sourcing</span>
              <span className="text-xs font-semibold text-foreground font-mono">
                {activeSourcingCount} active
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Sub-Tabs Navigation */}
      <div className="flex border-b border-border gap-1 overflow-x-auto">
        <button
          type="button"
          onClick={() => setActiveTab('inventory')}
          className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-xs font-medium transition-colors shrink-0 ${
            activeTab === 'inventory'
              ? 'border-primary text-primary font-semibold'
              : 'border-transparent text-muted-foreground hover:text-foreground'
          }`}
        >
          <Shirt className="size-4" />
          <span>Inventory & Visual Pieces ({inventory.length})</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('lookbooks')}
          className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-xs font-medium transition-colors shrink-0 ${
            activeTab === 'lookbooks'
              ? 'border-primary text-primary font-semibold'
              : 'border-transparent text-muted-foreground hover:text-foreground'
          }`}
        >
          <Layers className="size-4" />
          <span>Lookbooks & Outfits ({outfits.length})</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('sourcing')}
          className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-xs font-medium transition-colors shrink-0 ${
            activeTab === 'sourcing'
              ? 'border-primary text-primary font-semibold'
              : 'border-transparent text-muted-foreground hover:text-foreground'
          }`}
        >
          <Clock className="size-4" />
          <span>Sourcing Requests ({sourcingRequests.length})</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('suppliers')}
          className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-xs font-medium transition-colors shrink-0 ${
            activeTab === 'suppliers'
              ? 'border-primary text-primary font-semibold'
              : 'border-transparent text-muted-foreground hover:text-foreground'
          }`}
        >
          <Building2 className="size-4" />
          <span>Partner Ateliers ({suppliers.length})</span>
        </button>
      </div>

      {/* Active Tab View */}
      <div className="pt-2">
        {activeTab === 'inventory' && (
          <InventoryTab
            inventory={inventory}
            matches={matches}
            onAddNewPiece={() => {
              setEditingItem(null)
              setAddModalOpen(true)
            }}
            onViewMatches={handleViewMatches}
            onComposeOutfit={handleComposeOutfit}
            onEditItem={handleEditProduct}
          />
        )}

        {activeTab === 'lookbooks' && (
          <LookbooksTab
            outfits={outfits}
            onComposeLook={() => {
              setComposeHeroItem(inventory[0] ?? null)
              setComposeModalOpen(true)
            }}
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

      {/* Add / Edit Piece Modal */}
      <AddProductModal
        open={addModalOpen}
        organizationId={orgId}
        onClose={() => {
          setAddModalOpen(false)
          setEditingItem(null)
        }}
        onSave={handleSaveProduct}
        editingItem={editingItem}
      />

      {/* Customer Matches Drawer */}
      <CustomerMatchesDrawer
        item={selectedMatchItem}
        matches={matches}
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
    </div>
  )
}
