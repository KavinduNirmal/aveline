export interface InventoryItemMock {
  id: string
  name: string
  sku: string
  category: string
  color: string
  colorHex: string
  fabric: string
  style: string
  pattern?: string
  sizes: string[]
  price: number
  cost: number
  stockQuantity: number
  status: 'available' | 'reserved' | 'low_stock' | 'archived'
  imageUrl: string
  confidenceScore?: number
  description?: string
  createdAt: string
}

export interface CustomerMatchMock {
  id: string
  customerId: string
  customerName: string
  customerEmail: string
  customerAvatar?: string
  itemId: string
  itemName: string
  matchConfidence: number
  matchReason: string
  preferredColor?: string
  preferredFabric?: string
  preferredSize?: string
  employeeActed: boolean
  createdAt: string
}

export interface OutfitItemMock {
  id: string
  itemId: string
  name: string
  category: string
  price: number
  imageUrl: string
  position: 'top' | 'bottom' | 'drape' | 'accessory' | 'footwear'
  notes?: string
}

export interface OutfitCompositionMock {
  id: string
  name: string
  occasion: string
  totalPrice: number
  styleNotes: string
  heroImageUrl: string
  createdAt: string
  items: OutfitItemMock[]
}

export interface SourcingRequestMock {
  id: string
  clientName: string
  clientEmail?: string
  category: string
  color: string
  itemDescription: string
  referenceImageUrl: string
  supplierId: string
  supplierName: string
  estimatedCost: number
  proposedMarkup: number
  targetPrice: number
  status: 'pending' | 'quoted' | 'approved' | 'ordered' | 'fulfilled'
  createdAt: string
}

export interface SupplierMock {
  id: string
  name: string
  specialty: string
  contactEmail: string
  contactPhone: string
  location: string
  minimumOrder: number
  deliveryTimeDays: number
  isActive: boolean
  sampleCatalogCount: number
  catalogItems?: {
    id: string
    name: string
    category: string
    fabric: string
    wholesalePrice: number
    imageUrl: string
    inStock: boolean
  }[]
}


// The five `MOCK_*` seed arrays that used to live here are deleted (F-10, T0b). Nothing
// imported them: every consumer imports these *types* with `import type`, so the data was
// dead weight in the bundle while the type names are the live contracts. Keeping only the
// types is also why the file no longer contributes conformance violations (raw palette
// utilities, bare hex) to the tenant gate.
