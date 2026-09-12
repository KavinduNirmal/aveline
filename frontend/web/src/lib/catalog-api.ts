import { apiClient } from '@/lib/api'
import type {
  InventoryItem,
  CustomerMatch,
  OutfitComposition,
  SourcingRequest,
  Supplier,
  SupplierCatalogItem,
  VisionAnalysisResult,
  SearchInventoryParams,
  CreateInventoryItemPayload,
  UpdateInventoryItemPayload,
  CreateSourcingRequestPayload,
  ComposeOutfitPayload,
} from '@/types/catalog'

const catalogBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/catalog`

/**
 * Normalizes backend InventoryItemDto to frontend InventoryItem model.
 */
export function normalizeInventoryItem(raw: any): InventoryItem {
  const name = raw.itemName || raw.name || 'Untitled Piece'
  return {
    id: raw.id,
    name,
    itemName: name,
    sku: raw.sku || '',
    category: raw.category || 'General',
    color: raw.color || 'Unspecified',
    colorHex: raw.colorHex || '#4B5563',
    fabric: raw.fabric || 'Fabric',
    style: raw.style || 'Contemporary',
    pattern: raw.pattern || undefined,
    sizes: Array.isArray(raw.sizes) ? raw.sizes : (raw.sizes ? [raw.sizes] : ['One Size']),
    price: typeof raw.price === 'number' ? raw.price : Number(raw.price || 0),
    cost: typeof raw.cost === 'number' ? raw.cost : Number(raw.cost || 0),
    stockQuantity: typeof raw.quantity === 'number' ? raw.quantity : Number(raw.stockQuantity || raw.quantity || 0),
    status: (raw.status || 'available') as InventoryItem['status'],
    imageUrl: raw.imageUrl || '',
    confidenceScore: raw.confidenceScore ?? 0.95,
    description: raw.description || '',
    createdAt: raw.createdAt || new Date().toISOString(),
    updatedAt: raw.updatedAt,
    organizationId: raw.organizationId || raw.orgId,
  }
}

/**
 * Normalizes backend CustomerMatchDto to frontend CustomerMatch model.
 */
export function normalizeCustomerMatch(raw: any): CustomerMatch {
  return {
    id: raw.id,
    customerId: raw.customerId,
    customerName: raw.customerName || 'VIP Client',
    customerEmail: raw.customerEmail,
    customerAvatar: raw.customerAvatar,
    itemId: raw.inventoryItemId || raw.itemId,
    inventoryItemId: raw.inventoryItemId || raw.itemId,
    itemName: raw.itemName || 'Matched Piece',
    matchConfidence: raw.matchConfidence ?? raw.matchScore ?? 0.85,
    matchScore: raw.matchScore ?? raw.matchConfidence ?? 0.85,
    matchReason: raw.matchReason || 'Matches customer style profile and past purchases.',
    preferredColor: raw.preferredColor,
    preferredFabric: raw.preferredFabric,
    preferredSize: raw.preferredSize,
    employeeActed: Boolean(raw.employeeActed),
    createdAt: raw.createdAt || new Date().toISOString(),
  }
}

/**
 * Normalizes backend SourcingRequestDto to frontend SourcingRequest model.
 */
export function normalizeSourcingRequest(raw: any): SourcingRequest {
  const desc = raw.description || raw.itemDescription || ''
  return {
    id: raw.id,
    clientName: raw.clientName || 'VIP Client',
    clientEmail: raw.clientEmail,
    category: raw.category || 'Apparel',
    color: raw.color || 'Unspecified',
    itemDescription: desc,
    description: desc,
    referenceImageUrl: raw.referenceImageUrl || '',
    supplierId: raw.supplierId || '',
    supplierName: raw.supplierName || 'Partner Atelier',
    estimatedCost: raw.estimatedCost ? Number(raw.estimatedCost) : undefined,
    proposedMarkup: raw.proposedMarkup ? Number(raw.proposedMarkup) : undefined,
    targetPrice: raw.targetPrice ? Number(raw.targetPrice) : undefined,
    quantityNeeded: raw.quantityNeeded ? Number(raw.quantityNeeded) : 1,
    urgency: raw.urgency || 'medium',
    status: (raw.status || 'pending') as SourcingRequest['status'],
    notes: raw.notes,
    createdAt: raw.createdAt || new Date().toISOString(),
    organizationId: raw.organizationId || raw.orgId,
  }
}

/**
 * Normalizes backend OutfitCompositionDto to frontend OutfitComposition model.
 */
export function normalizeOutfitComposition(raw: any): OutfitComposition {
  return {
    id: raw.id,
    name: raw.name || 'Curated Ensemble',
    occasion: raw.occasion || 'Evening / Gala',
    totalPrice: typeof raw.totalPrice === 'number' ? raw.totalPrice : Number(raw.totalPrice || 0),
    styleNotes: raw.styleNotes || raw.notes || 'Curated by Elle Stylist AI',
    heroImageUrl: raw.heroImageUrl || '',
    createdAt: raw.createdAt || new Date().toISOString(),
    organizationId: raw.organizationId,
    items: Array.isArray(raw.items)
      ? raw.items.map((it: any) => ({
          id: it.id || it.itemId,
          itemId: it.itemId || it.id,
          name: it.name || it.itemName || 'Ensemble Item',
          category: it.category || 'Accessory',
          price: typeof it.price === 'number' ? it.price : Number(it.price || 0),
          imageUrl: it.imageUrl || '',
          position: it.position || 'top',
          notes: it.notes,
        }))
      : [],
  }
}

/**
 * Lists catalog inventory items with optional search & filtering.
 */
export async function fetchCatalogItems(
  organizationId: string,
  params: SearchInventoryParams = {},
): Promise<InventoryItem[]> {
  const response = await apiClient.get<any[]>(`${catalogBase(organizationId)}/items`, {
    params: {
      category: params.category,
      color: params.color,
      size: params.size,
      minPrice: params.minPrice,
      maxPrice: params.maxPrice,
      inStockOnly: params.inStockOnly,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 50,
    },
  })
  return (response.data || []).map(normalizeInventoryItem)
}

/**
 * Fetches a single catalog item by ID.
 */
export async function fetchCatalogItem(
  organizationId: string,
  itemId: string,
): Promise<InventoryItem> {
  const response = await apiClient.get<any>(
    `${catalogBase(organizationId)}/items/${itemId}`,
  )
  return normalizeInventoryItem(response.data)
}

/**
 * Creates a new catalog item.
 */
export async function createCatalogItem(
  organizationId: string,
  payload: CreateInventoryItemPayload,
): Promise<InventoryItem> {
  const response = await apiClient.post<any>(
    `${catalogBase(organizationId)}/items`,
    payload,
  )
  return normalizeInventoryItem(response.data)
}

/**
 * Updates an existing catalog item.
 */
export async function updateCatalogItem(
  organizationId: string,
  itemId: string,
  payload: UpdateInventoryItemPayload,
): Promise<InventoryItem> {
  const response = await apiClient.put<any>(
    `${catalogBase(organizationId)}/items/${itemId}`,
    payload,
  )
  return normalizeInventoryItem(response.data)
}

/**
 * Updates the status of a catalog item.
 */
export async function updateCatalogItemStatus(
  organizationId: string,
  itemId: string,
  status: string,
): Promise<InventoryItem> {
  const response = await apiClient.patch<any>(
    `${catalogBase(organizationId)}/items/${itemId}/status`,
    { status },
  )
  return normalizeInventoryItem(response.data)
}

/**
 * Fetches low stock inventory items.
 */
export async function fetchLowStockItems(
  organizationId: string,
  threshold = 5,
): Promise<InventoryItem[]> {
  const response = await apiClient.get<any[]>(
    `${catalogBase(organizationId)}/low-stock`,
    { params: { threshold } },
  )
  return (response.data || []).map(normalizeInventoryItem)
}

const COLOR_HEX_MAP: Record<string, string> = {
  emerald: '#0f5132',
  'emerald green': '#0f5132',
  green: '#16a34a',
  burgundy: '#800020',
  'imperial burgundy': '#800020',
  maroon: '#800000',
  'ruby red': '#9b111e',
  red: '#dc2626',
  crimson: '#990000',
  navy: '#000080',
  'navy blue': '#1e3a8a',
  'midnight blue': '#1e293b',
  blue: '#2563eb',
  royal: '#4169e1',
  'royal blue': '#4169e1',
  gold: '#d4af37',
  'rose gold': '#b76e79',
  champagne: '#f7e7ce',
  black: '#000000',
  white: '#ffffff',
  ivory: '#fffff0',
  pink: '#ec4899',
  'dusty rose': '#dcae96',
  purple: '#9333ea',
  plum: '#8e4585',
  violet: '#7c3aed',
  teal: '#0d9488',
  olive: '#808000',
  mustard: '#eab308',
  yellow: '#eab308',
  orange: '#ea580c',
  rust: '#b7410e',
  silver: '#c0c0c0',
  grey: '#6b7280',
  gray: '#6b7280',
  beige: '#f5f5dc',
  brown: '#78350f',
}

export function getColorHex(colorName?: string, fallback = '#0f5132'): string {
  if (!colorName) return fallback
  const trimmed = colorName.trim().toLowerCase()
  if (COLOR_HEX_MAP[trimmed]) return COLOR_HEX_MAP[trimmed]
  for (const [name, hex] of Object.entries(COLOR_HEX_MAP)) {
    if (trimmed.includes(name) || name.includes(trimmed)) return hex
  }
  return fallback
}

/**
 * Normalizes backend ImageAnalysisResultDto to frontend VisionAnalysisResult model.
 */
export function normalizeVisionAnalysis(raw: any): VisionAnalysisResult {
  const color = raw.detectedColor || raw.primaryColor || raw.color || 'Emerald Green'
  const hex = raw.colorHex || raw.color_hex || getColorHex(color)
  const fabric = raw.fabric || 'Mulberry Silk'
  const category = raw.category || 'Sarees'
  const pattern = raw.pattern || 'Handcrafted Embellishment'
  const style = raw.style || 'Contemporary Luxe'

  const formattedColor = color.charAt(0).toUpperCase() + color.slice(1)
  const defaultDesc = `Exquisite ${formattedColor} ${category.toLowerCase()} crafted from premium ${fabric.toLowerCase()} featuring an elegant ${pattern.toLowerCase()} aesthetic with fluid drape. Styling: Pair with fine jewelry, tonal evening accessories, and structured footwear for a polished boutique statement.`

  const desc = raw.description || raw.summary || defaultDesc

  return {
    category,
    detectedColor: color,
    colorHex: hex,
    fabric,
    style,
    pattern: raw.pattern || undefined,
    confidenceScore: typeof raw.confidenceScore === 'number' ? raw.confidenceScore : 0.95,
    visualAttributes: raw.visualAttributes || raw.suggestedKeywords || [color, fabric, pattern],
    summary: desc,
    description: desc,
    stylingNotes: raw.stylingNotes || raw.styling_notes || 'Pair with fine jewelry and minimalist evening accessories.',
  }
}

/**
 * Analyzes a product image using Elle Vision AI.
 */
export async function analyzeProductImage(
  organizationId: string,
  imageUrl: string,
): Promise<VisionAnalysisResult> {
  const response = await apiClient.post<any>(
    `${catalogBase(organizationId)}/analyze-image`,
    { imageUrl, organizationId },
  )
  return normalizeVisionAnalysis(response.data)
}

/**
 * Fetches customer matches for a catalog item.
 */
export async function fetchCustomerMatches(
  organizationId: string,
  itemId: string,
  minScore = 0.7,
): Promise<CustomerMatch[]> {
  const response = await apiClient.get<any[]>(
    `${catalogBase(organizationId)}/items/${itemId}/matches`,
    { params: { minScore } },
  )
  return (response.data || []).map(normalizeCustomerMatch)
}

/**
 * Generates customer matches for a catalog item.
 */
export async function generateCustomerMatches(
  organizationId: string,
  itemId: string,
): Promise<CustomerMatch[]> {
  const response = await apiClient.post<any[]>(
    `${catalogBase(organizationId)}/items/${itemId}/matches/generate`,
    { organizationId },
  )
  return (response.data || []).map(normalizeCustomerMatch)
}

/**
 * Lists lookbooks and outfit capsules.
 */
export async function fetchLookbooks(
  organizationId: string,
): Promise<OutfitComposition[]> {
  const response = await apiClient.get<any[]>(
    `${catalogBase(organizationId)}/lookbooks`,
  )
  return (response.data || []).map(normalizeOutfitComposition)
}

/**
 * Composes an outfit look around a primary item.
 */
export async function composeLookbook(
  organizationId: string,
  payload: ComposeOutfitPayload,
): Promise<OutfitComposition> {
  const response = await apiClient.post<any>(
    `${catalogBase(organizationId)}/lookbooks/compose`,
    {
      organizationId,
      name: payload.name,
      primaryItemId: payload.primaryItemId,
      customerProfileId: payload.customerProfileId,
      notes: payload.notes,
    },
  )
  return normalizeOutfitComposition(response.data)
}

/**
 * Lists sourcing requests for the boutique.
 */
export async function fetchSourcingRequests(
  organizationId: string,
  status?: string,
): Promise<SourcingRequest[]> {
  const response = await apiClient.get<any[]>(
    `${catalogBase(organizationId)}/sourcing`,
    { params: { status } },
  )
  return (response.data || []).map(normalizeSourcingRequest)
}

/**
 * Creates a new sourcing request.
 */
export async function createSourcingRequest(
  organizationId: string,
  payload: CreateSourcingRequestPayload,
): Promise<SourcingRequest> {
  const response = await apiClient.post<any>(
    `${catalogBase(organizationId)}/sourcing`,
    {
      organizationId,
      category: payload.category,
      color: payload.color,
      description: payload.description,
      targetPrice: payload.targetPrice,
      quantityNeeded: payload.quantityNeeded ?? 1,
      urgency: payload.urgency ?? 'medium',
      customerId: payload.customerId,
    },
  )
  return normalizeSourcingRequest(response.data)
}

/**
 * Updates the status of a sourcing request.
 */
export async function updateSourcingRequestStatus(
  organizationId: string,
  id: string,
  status: string,
  notes?: string,
): Promise<SourcingRequest> {
  const response = await apiClient.patch<any>(
    `${catalogBase(organizationId)}/sourcing/${id}/status`,
    { status, notes },
  )
  return normalizeSourcingRequest(response.data)
}

/**
 * Lists integrated suppliers.
 */
export async function fetchSuppliers(
  organizationId: string,
): Promise<Supplier[]> {
  const response = await apiClient.get<Supplier[]>(
    `${catalogBase(organizationId)}/suppliers`,
  )
  return response.data || []
}

/**
 * Queries an external supplier catalog.
 */
export async function fetchSupplierCatalog(
  organizationId: string,
  supplierId: string,
  params: { category?: string; color?: string; maxPrice?: number } = {},
): Promise<SupplierCatalogItem[]> {
  const response = await apiClient.get<SupplierCatalogItem[]>(
    `${catalogBase(organizationId)}/suppliers/${supplierId}/catalog`,
    { params },
  )
  return response.data || []
}
