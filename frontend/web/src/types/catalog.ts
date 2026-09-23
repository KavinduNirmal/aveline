export interface InventoryItem {
  id: string
  name: string
  itemName?: string
  sku?: string
  category: string
  color: string
  colorHex?: string
  fabric?: string
  style?: string
  pattern?: string
  sizes: string[]
  price: number
  cost?: number
  stockQuantity: number
  status: 'available' | 'reserved' | 'low_stock' | 'archived'
  imageUrl?: string
  confidenceScore?: number
  description?: string
  createdAt?: string
  updatedAt?: string
  organizationId?: string
}

export interface CustomerMatch {
  id: string
  customerId: string
  customerName: string
  customerEmail?: string
  customerAvatar?: string
  itemId: string
  inventoryItemId?: string
  itemName: string
  matchConfidence: number
  matchScore?: number
  matchReason: string
  preferredColor?: string
  preferredFabric?: string
  preferredSize?: string
  employeeActed: boolean
  createdAt: string
}

export interface OutfitItem {
  id: string
  itemId: string
  name: string
  category: string
  price: number
  imageUrl: string
  position: 'top' | 'bottom' | 'drape' | 'accessory' | 'footwear' | string
  notes?: string
}

export interface OutfitComposition {
  id: string
  name: string
  occasion: string
  totalPrice: number
  styleNotes: string
  heroImageUrl: string
  createdAt: string
  items: OutfitItem[]
  organizationId?: string
}

export interface SourcingRequest {
  id: string
  clientName?: string
  clientEmail?: string
  category: string
  color: string
  itemDescription: string
  description?: string
  referenceImageUrl?: string
  supplierId?: string
  supplierName?: string
  estimatedCost?: number
  proposedMarkup?: number
  targetPrice?: number
  quantityNeeded?: number
  urgency?: string
  status: 'pending' | 'quoted' | 'approved' | 'ordered' | 'fulfilled' | string
  notes?: string
  createdAt: string
  organizationId?: string
}

export interface SupplierCatalogItem {
  id: string
  name: string
  category: string
  fabric?: string
  wholesalePrice: number
  imageUrl: string
  inStock: boolean
}

export interface Supplier {
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
  catalogItems?: SupplierCatalogItem[]
  organizationId?: string
}

export interface VisionAnalysisResult {
  category: string
  // The vision provider may not identify a colour, fabric or style at all. Those fields are then
  // absent rather than replaced by a fabricated default, so every consumer must handle `undefined`.
  detectedColor?: string
  colorHex?: string
  fabric?: string
  style?: string
  pattern?: string
  garmentType?: string
  suggestedItemName?: string
  confidenceScore?: number
  isFallback?: boolean
  visualAttributes: string[]
  summary: string
  description?: string
  stylingNotes?: string
}

export interface SearchInventoryParams {
  category?: string
  color?: string
  size?: string
  minPrice?: number
  maxPrice?: number
  inStockOnly?: boolean
  page?: number
  pageSize?: number
}

export interface CreateInventoryItemPayload {
  itemName: string
  category: string
  color: string
  colorHex?: string
  fabric?: string
  style?: string
  sizes: string[]
  price: number
  cost?: number
  quantity: number
  status?: string
  imageUrl?: string
  sku?: string
  description?: string
}

export interface UpdateInventoryItemPayload {
  itemName?: string
  category?: string
  color?: string
  colorHex?: string
  fabric?: string
  style?: string
  sizes?: string[]
  price?: number
  cost?: number
  quantity?: number
  /** The stock-derived status. Sent alongside a manual stock adjustment. */
  status?: string
  imageUrl?: string
  sku?: string
  description?: string
}

export interface CreateSourcingRequestPayload {
  category: string
  color: string
  description: string
  targetPrice?: number
  quantityNeeded?: number
  urgency?: string
  customerId?: string
}

export interface ComposeOutfitPayload {
  name: string
  primaryItemId: string
  customerProfileId?: string
  notes?: string
}

/**
 * The editable metadata of a composed lookbook. Every field is optional: an omitted field keeps the
 * value the server already stores, and `styleNotes` set to an empty string is how a note is cleared.
 */
export interface UpdateLookbookPayload {
  name?: string
  occasion?: string
  styleNotes?: string
}

export interface RecordCatalogSalePayload {
  quantity: number
  /** Omitted means "the catalog price"; supplied means the counter agreed a different one. */
  unitPrice?: number
  customerId?: string
  note?: string
}

/**
 * What the counter gets back after a sale. `remainingStock` is a real measurement, including a
 * measured zero, and `ledgerEntryId` names the takings-journal row the register will show.
 */
export interface CatalogSaleReceipt {
  itemId: string
  itemName: string
  sku?: string | null
  quantitySold: number
  unitPrice: number
  totalAmount: number
  remainingStock: number
  status: string
  ledgerEntryId: string
  recordedAtUtc: string
}
