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
  UpdateLookbookPayload,
  RecordCatalogSalePayload,
  CatalogSaleReceipt,
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
    // No invented swatch. The API carries the colour the analysis measured; when it carries none,
    // the field is absent and the card renders no dot rather than a flat grey nobody measured.
    colorHex: raw.colorHex || undefined,
    fabric: raw.fabric || 'Fabric',
    style: raw.style || 'Contemporary',
    pattern: raw.pattern || undefined,
    sizes: Array.isArray(raw.sizes) ? raw.sizes : (raw.sizes ? [raw.sizes] : ['One Size']),
    price: typeof raw.price === 'number' ? raw.price : Number(raw.price || 0),
    cost: typeof raw.cost === 'number' ? raw.cost : Number(raw.cost || 0),
    stockQuantity: typeof raw.quantity === 'number' ? raw.quantity : Number(raw.stockQuantity || raw.quantity || 0),
    status: (raw.status || 'available') as InventoryItem['status'],
    imageUrl: raw.imageUrl || '',
    // No invented confidence. `InventoryItemDto` has no such field, so a list row only carries one
    // when the caller actually supplied it; substituting 0.95 made every card claim "95% Vision AI".
    confidenceScore: typeof raw.confidenceScore === 'number' ? raw.confidenceScore : undefined,
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
 * Normalizes a lookbook payload into the frontend `OutfitComposition` model.
 *
 * Two wire shapes describe the same look. The stored row (`OutfitCompositionDto`, the lookbooks
 * list and the update response) carries `name` and an `items` array whose pieces name their slot
 * `role`. The composer (`ComposedOutfitDto`, the compose response) instead names the ensemble
 * `lookName` and splits its pieces into `primaryItem`/`complementaryItems`. Reading only one shape
 * silently produced an empty ensemble under the default name, so both are handled here — it
 * observes, it does not invent.
 */
export function normalizeOutfitComposition(raw: any): OutfitComposition {
  const rawItems: any[] = Array.isArray(raw.items)
    ? raw.items
    : [
        ...(raw.primaryItem ? [raw.primaryItem] : []),
        ...(Array.isArray(raw.complementaryItems) ? raw.complementaryItems : []),
      ]

  const totalPrice = raw.totalPrice ?? raw.totalLookPrice
  const heroImageUrl = raw.heroImageUrl || raw.primaryItem?.imageUrl || ''

  return {
    id: raw.id || raw.outfitId || '',
    name: raw.name || raw.lookName || 'Curated Ensemble',
    occasion: raw.occasion || 'Evening / Gala',
    totalPrice: typeof totalPrice === 'number' ? totalPrice : Number(totalPrice || 0),
    styleNotes: raw.styleNotes || raw.stylingNotes || raw.notes || 'Curated by Elle Stylist AI',
    heroImageUrl,
    createdAt: raw.createdAt || raw.createdAtUtc || new Date().toISOString(),
    organizationId: raw.organizationId || raw.orgId,
    items: rawItems.map((it: any, index: number) => ({
      id: it.id || it.itemId || `outfit-item-${index}`,
      itemId: it.inventoryItemId || it.itemId || it.id || '',
      name: it.itemName || it.name || 'Ensemble Item',
      category: it.category || 'Accessory',
      price: typeof it.price === 'number' ? it.price : Number(it.price || 0),
      imageUrl: it.imageUrl || '',
      // The stored row names the slot `role`; older payloads and the composer use `position`.
      position: it.role || it.position || 'primary',
      notes: it.notes,
    })),
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
 * Deletes (soft-deletes) an inventory item from the catalog.
 */
export async function deleteCatalogItem(
  organizationId: string,
  itemId: string,
): Promise<void> {
  await apiClient.delete(`${catalogBase(organizationId)}/items/${itemId}`)
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

export const CATALOG_CATEGORIES = [
  'Sarees',
  'Lehengas',
  'Gowns',
  'Kurtas & Tunics',
  'Outerwear',
  'Drapes & Shawls',
  'Jewelry & Accessories',
] as const

export type CatalogCategory = (typeof CATALOG_CATEGORIES)[number]

/**
 * Normalizes raw category string (or singular variants/synonyms) into canonical catalog categories.
 */
export function normalizeCategory(rawCategory?: string): CatalogCategory {
  if (!rawCategory) return 'Sarees'
  const trimmed = rawCategory.trim()
  const matchedExact = CATALOG_CATEGORIES.find(
    (c) => c.toLowerCase() === trimmed.toLowerCase(),
  )
  if (matchedExact) return matchedExact

  const lower = trimmed.toLowerCase()
  if (lower.includes('saree') || lower.includes('sari')) return 'Sarees'
  if (lower.includes('lehenga') || lower.includes('ghagra') || lower.includes('choli')) return 'Lehengas'
  if (
    lower.includes('gown') ||
    lower.includes('dress') ||
    lower.includes('maxi') ||
    lower.includes('midi') ||
    lower.includes('frock') ||
    lower.includes('jumpsuit') ||
    lower.includes('romper')
  ) {
    return 'Gowns'
  }
  if (
    lower.includes('kurta') ||
    lower.includes('kurti') ||
    lower.includes('kurtis') ||
    lower.includes('tunic') ||
    lower.includes('top') ||
    lower.includes('shirt') ||
    lower.includes('blouse') ||
    lower.includes('anarkali') ||
    lower.includes('sherwani')
  ) {
    return 'Kurtas & Tunics'
  }
  if (
    lower.includes('outerwear') ||
    lower.includes('blazer') ||
    lower.includes('jacket') ||
    lower.includes('coat') ||
    lower.includes('shrug') ||
    lower.includes('cardigan') ||
    lower.includes('suit') ||
    lower.includes('trench')
  ) {
    return 'Outerwear'
  }
  if (
    lower.includes('drape') ||
    lower.includes('shawl') ||
    lower.includes('dupatta') ||
    lower.includes('stole') ||
    lower.includes('scarf') ||
    lower.includes('wrap') ||
    lower.includes('pallu')
  ) {
    return 'Drapes & Shawls'
  }
  if (
    lower.includes('jewel') ||
    lower.includes('accessor') ||
    lower.includes('necklace') ||
    lower.includes('earring') ||
    lower.includes('bangle') ||
    lower.includes('bag') ||
    lower.includes('clutch') ||
    lower.includes('footwear') ||
    lower.includes('heel') ||
    lower.includes('shoe')
  ) {
    return 'Jewelry & Accessories'
  }

  return 'Sarees'
}

/**
 * The colour a piece falls back to when neither the analysis nor the operator named one. It lives
 * here, beside the colour table, so a component never spells a hex literal of its own: the tenant
 * conformance gate forbids bare hex anywhere in the dashboard tree.
 */
export const DEFAULT_COLOR_HEX = '#0f5132'

export function getColorHex(colorName?: string, fallback = DEFAULT_COLOR_HEX): string {
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
 *
 * `ImageAnalysisResultDto` pins the colour property to the snake_case wire name `primary_color`
 * (and `secondary_colors`), while every other property travels camelCase. Reading only
 * `detectedColor`/`primaryColor` therefore always missed the model's answer, and the old hardcoded
 * fallback literal disguised the miss. A value the provider did not return is left absent here;
 * this function observes, it does not invent.
 */
export function normalizeVisionAnalysis(raw: any): VisionAnalysisResult {
  const namedColor: string | undefined =
    raw.detectedColor || raw.primaryColor || raw.primary_color || raw.color || undefined
  // `VisionService` writes the literal `"unknown"` when the model named no colour. That is a
  // placeholder, not a colour: left as-is it would render a chip reading "unknown" and, worse,
  // `getColorHex('unknown')` resolved it to the palette's first entry, an emerald swatch.
  const color: string | undefined =
    namedColor && namedColor.trim().toLowerCase() !== 'unknown' ? namedColor : undefined
  // The provider's own hex, and nothing else. `getColorHex` maps a colour *name* through a fixed
  // palette and returns `DEFAULT_COLOR_HEX` for anything outside it, so deriving a hex from the
  // name painted an emerald swatch for "Taupe", "Fuchsia Pink" and every other unlisted name. A
  // swatch must come from a measurement, never from a lookup table with a fallback.
  const hex: string | undefined = raw.colorHex || raw.color_hex || undefined
  const category = normalizeCategory(raw.category)
  const garmentType = raw.garmentType || raw.garment_type || undefined
  const suggestedItemName = raw.suggestedItemName || raw.suggested_item_name || undefined
  const fabric: string | undefined = raw.fabric || undefined
  const pattern: string | undefined = raw.pattern || undefined
  const style: string | undefined = raw.style || undefined

  // No synthesized couture sentence: with neither description nor summary the field stays absent.
  const desc = raw.description || raw.summary || undefined

  // Prefer the provider's own keyword list; when it supplied none, fall back only to the values
  // that are genuinely present, never to `undefined` placeholders.
  const visualAttributes: string[] = Array.isArray(raw.suggestedKeywords)
    ? raw.suggestedKeywords
    : Array.isArray(raw.visual_attributes)
      ? raw.visual_attributes
      : Array.isArray(raw.visualAttributes)
        ? raw.visualAttributes
        : [color, fabric, pattern].filter((value): value is string => Boolean(value))

  return {
    category,
    detectedColor: color,
    colorHex: hex,
    fabric,
    style,
    pattern,
    garmentType,
    suggestedItemName,
    // Only a real analysis result has a confidence; the fallback used to invent one here too.
    confidenceScore: typeof raw.confidenceScore === 'number' ? raw.confidenceScore : undefined,
    isFallback: Boolean(raw.isFallback || raw.is_fallback || false),
    visualAttributes,
    summary: desc ?? '',
    description: desc || undefined,
    stylingNotes: raw.stylingNotes || raw.styling_notes || undefined,
  }
}

/**
 * Analyzes a product image using Elle Vision AI.
 *
 * The image can be addressed two ways. Passing a `data:` URL (or an absolute `http(s)` URL) sends
 * that URL. Passing `imageRefId` instead names a stored inventory image row: the backend resolves
 * the row and hands the provider the bytes inline, so the analysis never depends on the provider
 * being able to fetch anything. A reference request must not also carry an `imageUrl`.
 */
export async function analyzeProductImage(
  organizationId: string,
  imageUrl: string,
  fileName?: string,
  contextHint?: string,
  imageRefId?: string,
): Promise<VisionAnalysisResult> {
  const trimmedRefId = imageRefId?.trim()
  const body = trimmedRefId
    ? {
        organizationId,
        imageRefKind: 'inventoryImage' as const,
        imageRefId: trimmedRefId,
        fileName,
        contextHint,
      }
    : { imageUrl, organizationId, fileName, contextHint }
  const response = await apiClient.post<any>(
    `${catalogBase(organizationId)}/analyze-image`,
    body,
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
 * Renames or re-occasions a composed lookbook. An omitted field keeps the stored value.
 */
export async function updateLookbook(
  organizationId: string,
  lookbookId: string,
  payload: UpdateLookbookPayload,
): Promise<OutfitComposition> {
  const response = await apiClient.put<any>(
    `${catalogBase(organizationId)}/lookbooks/${lookbookId}`,
    {
      name: payload.name,
      occasion: payload.occasion,
      styleNotes: payload.styleNotes,
    },
  )
  return normalizeOutfitComposition(response.data)
}

/**
 * Removes a composed lookbook from the boutique.
 */
export async function deleteLookbook(
  organizationId: string,
  lookbookId: string,
): Promise<void> {
  await apiClient.delete(`${catalogBase(organizationId)}/lookbooks/${lookbookId}`)
}

/**
 * Sells a catalog piece over the counter. The server decrements the row's stock and appends the
 * money to the boutique's takings journal in one call, so the catalog and the register cannot
 * disagree about whether a sale happened.
 */
export async function recordCatalogSale(
  organizationId: string,
  itemId: string,
  payload: RecordCatalogSalePayload,
): Promise<CatalogSaleReceipt> {
  const response = await apiClient.post<CatalogSaleReceipt>(
    `${catalogBase(organizationId)}/items/${itemId}/sales`,
    {
      quantity: payload.quantity,
      unitPrice: payload.unitPrice,
      customerId: payload.customerId,
      note: payload.note,
    },
  )
  return response.data
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

export interface UploadImageResult {
  id: string
  url: string
  fileName?: string
  size?: number
  contentType?: string
}

/**
 * Uploads a physical image file to PostgreSQL database storage.
 */
export async function uploadCatalogImage(
  organizationId: string,
  file: File | Blob,
  fileName?: string,
): Promise<UploadImageResult> {
  const formData = new FormData()
  formData.append('file', file, fileName || (file instanceof File ? file.name : 'upload.jpg'))

  const response = await apiClient.post<UploadImageResult>(
    `${catalogBase(organizationId)}/images/upload`,
    formData,
    {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    },
  )
  return response.data
}

/**
 * Uploads a Base64 Data URL to PostgreSQL database storage.
 */
export async function uploadBase64Image(
  organizationId: string,
  dataUrl: string,
  fileName?: string,
): Promise<UploadImageResult> {
  const response = await apiClient.post<UploadImageResult>(
    `${catalogBase(organizationId)}/images/upload`,
    {
      imageData: dataUrl,
      fileName: fileName || 'garment.jpg',
    },
  )
  return response.data
}

export interface GenerateQrResponse {
  payload: string
  format: string
  dataUrl?: string
  base64?: string
  svg?: string
  size: number
  eccLevel: string
  createdAtUtc: string
}

/**
 * Generates a dynamic QR code from the backend API.
 */
export async function generateQrCode(
  organizationId: string,
  payload: {
    payload: string
    format?: 'png' | 'svg' | 'json' | 'base64'
    size?: number
    eccLevel?: 'L' | 'M' | 'Q' | 'H'
    quietZone?: number
  },
): Promise<GenerateQrResponse> {
  const response = await apiClient.post<GenerateQrResponse>(
    `${catalogBase(organizationId)}/qr/generate`,
    {
      payload: payload.payload,
      format: payload.format || 'json',
      size: payload.size || 300,
      eccLevel: payload.eccLevel || 'M',
      quietZone: payload.quietZone ?? 2,
    },
  )
  return response.data
}

/**
 * Fetches the official QR code for a specific catalog item.
 */
export async function fetchItemQr(
  organizationId: string,
  itemId: string,
  params?: { format?: 'png' | 'svg' | 'json'; size?: number },
): Promise<GenerateQrResponse> {
  const response = await apiClient.get<GenerateQrResponse>(
    `${catalogBase(organizationId)}/items/${itemId}/qr`,
    {
      params: {
        format: params?.format || 'json',
        size: params?.size || 300,
      },
    },
  )
  return response.data
}


