import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()
const putMock = vi.fn()
const patchMock = vi.fn()
const deleteMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
    put: (...args: unknown[]) => putMock(...args),
    patch: (...args: unknown[]) => patchMock(...args),
    delete: (...args: unknown[]) => deleteMock(...args),
  },
}))

import {
  fetchCatalogItems,
  fetchCatalogItem,
  createCatalogItem,
  updateCatalogItem,
  updateCatalogItemStatus,
  fetchLowStockItems,
  analyzeProductImage,
  fetchCustomerMatches,
  generateCustomerMatches,
  fetchLookbooks,
  composeLookbook,
  updateLookbook,
  deleteLookbook,
  recordCatalogSale,
  fetchSourcingRequests,
  createSourcingRequest,
  updateSourcingRequestStatus,
  fetchSuppliers,
  fetchSupplierCatalog,
  normalizeCategory,
  normalizeVisionAnalysis,
  normalizeOutfitComposition,
  normalizeInventoryItem,
  getColorHex,
  uploadCatalogImage,
  uploadBase64Image,
} from './catalog-api'

const ORG = 'org-123'
const ITEM = 'item-456'

describe('catalog API client', () => {
  beforeEach(() => {
    getMock.mockReset()
    postMock.mockReset()
    putMock.mockReset()
    patchMock.mockReset()
    deleteMock.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('fetchCatalogItems requests org-scoped items endpoint', async () => {
    getMock.mockResolvedValue({
      data: [
        { id: '1', itemName: 'Silk Saree', price: 500, stockQuantity: 3 },
      ],
    })

    const result = await fetchCatalogItems(ORG, { category: 'saree', inStockOnly: true })

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items`, {
      params: {
        category: 'saree',
        color: undefined,
        size: undefined,
        minPrice: undefined,
        maxPrice: undefined,
        inStockOnly: true,
        page: 1,
        pageSize: 50,
      },
    })
    expect(result).toHaveLength(1)
    expect(result[0].name).toBe('Silk Saree')
  })

  it('fetchCatalogItem requests single item', async () => {
    getMock.mockResolvedValue({
      data: { id: ITEM, itemName: 'Kanjeevaram Saree', price: 1200 },
    })

    const result = await fetchCatalogItem(ORG, ITEM)

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items/${ITEM}`)
    expect(result.name).toBe('Kanjeevaram Saree')
  })

  it('createCatalogItem posts new item payload', async () => {
    const payload = {
      itemName: 'Velvet Lehenga',
      category: 'lehenga',
      color: 'maroon',
      sizes: ['M', 'L'],
      price: 850,
      quantity: 4,
    }
    postMock.mockResolvedValue({
      data: { id: 'new-1', ...payload },
    })

    const result = await createCatalogItem(ORG, payload)

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items`, payload)
    expect(result.id).toBe('new-1')
  })

  it('updateCatalogItem puts updated item payload', async () => {
    const payload = { price: 900, quantity: 2 }
    putMock.mockResolvedValue({
      data: { id: ITEM, itemName: 'Velvet Lehenga', price: 900, stockQuantity: 2 },
    })

    const result = await updateCatalogItem(ORG, ITEM, payload)

    expect(putMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items/${ITEM}`, payload)
    expect(result.price).toBe(900)
  })

  it('updateCatalogItemStatus patches status', async () => {
    patchMock.mockResolvedValue({
      data: { id: ITEM, itemName: 'Velvet Lehenga', status: 'reserved' },
    })

    const result = await updateCatalogItemStatus(ORG, ITEM, 'reserved')

    expect(patchMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items/${ITEM}/status`, {
      status: 'reserved',
    })
    expect(result.status).toBe('reserved')
  })

  it('fetchLowStockItems requests low stock items', async () => {
    getMock.mockResolvedValue({ data: [] })

    await fetchLowStockItems(ORG, 3)

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/low-stock`, {
      params: { threshold: 3 },
    })
  })

  it('analyzeProductImage posts image url for vision analysis', async () => {
    const analysis = {
      category: 'saree',
      detectedColor: 'Emerald Green',
      colorHex: '#0f5132',
      fabric: 'Mulberry Silk',
      style: 'Traditional Heirloom',
      confidenceScore: 0.96,
      visualAttributes: ['Mulberry Silk', 'Zari Border'],
      summary: 'Pure Mulberry Silk saree in Emerald Green',
    }
    postMock.mockResolvedValue({ data: analysis })

    const result = await analyzeProductImage(ORG, 'https://example.com/saree.jpg', 'emerald_saree.jpg')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/analyze-image`, {
      imageUrl: 'https://example.com/saree.jpg',
      organizationId: ORG,
      fileName: 'emerald_saree.jpg',
      contextHint: undefined,
    })
    expect(result.fabric).toBe('Mulberry Silk')
  })

  it('analyzeProductImage addresses a stored upload by reference instead of by URL', async () => {
    postMock.mockResolvedValue({ data: { category: 'saree', detectedColor: 'Emerald Green' } })

    await analyzeProductImage(ORG, '', 'emerald_saree.jpg', undefined, 'img-1')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/analyze-image`, {
      organizationId: ORG,
      imageRefKind: 'inventoryImage',
      imageRefId: 'img-1',
      fileName: 'emerald_saree.jpg',
      contextHint: undefined,
    })
  })

  it('a reference request never also carries a stale imageUrl, even a relative one', async () => {
    postMock.mockResolvedValue({ data: { category: 'saree' } })

    await analyzeProductImage(
      ORG,
      `/api/v1/orgs/${ORG}/catalog/images/img-1`,
      'emerald_saree.jpg',
      undefined,
      'img-1',
    )

    const body = postMock.mock.calls[0][1] as Record<string, unknown>
    expect(body).not.toHaveProperty('imageUrl')
    expect(body.imageRefKind).toBe('inventoryImage')
    expect(body.imageRefId).toBe('img-1')
  })

  it('analyzeProductImage falls back to the URL arm for a blank reference id', async () => {
    postMock.mockResolvedValue({ data: { category: 'saree' } })

    await analyzeProductImage(ORG, 'https://example.com/saree.jpg', 'emerald_saree.jpg', undefined, '   ')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/analyze-image`, {
      imageUrl: 'https://example.com/saree.jpg',
      organizationId: ORG,
      fileName: 'emerald_saree.jpg',
      contextHint: undefined,
    })
  })

  it('fetchCustomerMatches requests matches for item', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          id: 'm-1',
          customerId: 'c-1',
          customerName: 'Ananya Sharma',
          itemName: 'Silk Saree',
          matchConfidence: 0.92,
          matchReason: 'Prefers emerald tones and heritage silk.',
          employeeActed: false,
        },
      ],
    })

    const result = await fetchCustomerMatches(ORG, ITEM, 0.8)

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items/${ITEM}/matches`, {
      params: { minScore: 0.8 },
    })
    expect(result).toHaveLength(1)
    expect(result[0].customerName).toBe('Ananya Sharma')
  })

  it('generateCustomerMatches triggers customer match computation', async () => {
    postMock.mockResolvedValue({
      data: [
        {
          id: 'm-2',
          customerId: 'c-2',
          customerName: 'Priya Patel',
          itemName: 'Silk Saree',
          matchConfidence: 0.88,
          matchReason: 'High affinity for festive sarees.',
          employeeActed: false,
        },
      ],
    })

    const result = await generateCustomerMatches(ORG, ITEM)

    expect(postMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/catalog/items/${ITEM}/matches/generate`,
      { organizationId: ORG },
    )
    expect(result).toHaveLength(1)
  })

  it('fetchLookbooks requests lookbooks list', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          id: 'look-1',
          name: 'Royal Heritage Gala Ensemble',
          occasion: 'Gala',
          totalPrice: 2200,
          items: [],
        },
      ],
    })

    const result = await fetchLookbooks(ORG)

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/lookbooks`)
    expect(result).toHaveLength(1)
    expect(result[0].name).toBe('Royal Heritage Gala Ensemble')
  })

  it('composeLookbook posts outfit composition request', async () => {
    postMock.mockResolvedValue({
      data: {
        id: 'look-2',
        name: 'Festive Look',
        totalPrice: 1500,
        items: [],
      },
    })

    const result = await composeLookbook(ORG, {
      name: 'Festive Look',
      primaryItemId: ITEM,
      notes: 'Style with gold jewelry',
    })

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/lookbooks/compose`, {
      organizationId: ORG,
      name: 'Festive Look',
      primaryItemId: ITEM,
      customerProfileId: undefined,
      notes: 'Style with gold jewelry',
    })
    expect(result.name).toBe('Festive Look')
  })

  it('updateLookbook puts only the supplied metadata fields', async () => {
    putMock.mockResolvedValue({
      data: {
        id: 'look-1',
        name: 'Royal Gala Ensemble',
        occasion: 'Cocktail Reception & Gala',
        totalPrice: 1500,
        styleNotes: 'Pair with a statement choker.',
        items: [],
      },
    })

    const result = await updateLookbook(ORG, 'look-1', {
      name: 'Royal Gala Ensemble',
      occasion: 'Cocktail Reception & Gala',
      styleNotes: 'Pair with a statement choker.',
    })

    expect(putMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/lookbooks/look-1`, {
      name: 'Royal Gala Ensemble',
      occasion: 'Cocktail Reception & Gala',
      styleNotes: 'Pair with a statement choker.',
    })
    expect(result.name).toBe('Royal Gala Ensemble')
    expect(result.occasion).toBe('Cocktail Reception & Gala')
  })

  it('deleteLookbook deletes the lookbook resource', async () => {
    deleteMock.mockResolvedValue({ status: 204 })

    await deleteLookbook(ORG, 'look-1')

    expect(deleteMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/lookbooks/look-1`)
  })

  it('recordCatalogSale posts the quantity, price and note and reads the receipt back', async () => {
    postMock.mockResolvedValue({
      data: {
        itemId: ITEM,
        itemName: 'Kanjeevaram Silk Saree',
        sku: 'AVL-001',
        quantitySold: 2,
        unitPrice: 450,
        totalAmount: 900,
        remainingStock: 3,
        status: 'available',
        ledgerEntryId: 'ledger-1',
        recordedAtUtc: '2026-09-22T00:00:00Z',
      },
    })

    const receipt = await recordCatalogSale(ORG, ITEM, { quantity: 2, unitPrice: 450, note: 'Gift wrap' })

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items/${ITEM}/sales`, {
      quantity: 2,
      unitPrice: 450,
      customerId: undefined,
      note: 'Gift wrap',
    })
    expect(receipt.remainingStock).toBe(3)
    expect(receipt.totalAmount).toBe(900)
    expect(receipt.ledgerEntryId).toBe('ledger-1')
  })

  it('normalizeOutfitComposition reads the composer shape as well as the stored-row shape', () => {
    // `ComposedOutfitDto` names the ensemble `lookName` and splits pieces into
    // `primaryItem`/`complementaryItems`; reading only `items` produced an empty ensemble under the
    // default name, which is what a composed look used to look like before this mapping existed.
    const composed = normalizeOutfitComposition({
      lookName: 'Curated Royal Luxury Ensemble',
      occasion: 'gala',
      stylingNotes: 'Harmonized outfit pairing.',
      totalLookPrice: 1500,
      primaryItem: { id: 'it-1', itemName: 'Silk Saree', price: 1000, imageUrl: '/a.jpg' },
      complementaryItems: [{ id: 'it-2', itemName: 'Gold Choker', price: 500, imageUrl: '/b.jpg' }],
    })

    expect(composed.name).toBe('Curated Royal Luxury Ensemble')
    expect(composed.totalPrice).toBe(1500)
    expect(composed.styleNotes).toBe('Harmonized outfit pairing.')
    expect(composed.heroImageUrl).toBe('/a.jpg')
    expect(composed.items).toHaveLength(2)
    expect(composed.items[1].name).toBe('Gold Choker')
  })

  it('reads the stored item role as the slot it fills', () => {
    const stored = normalizeOutfitComposition({
      id: 'look-9',
      name: 'Stored Look',
      items: [{ id: 'oi-1', inventoryItemId: 'it-1', itemName: 'Saree', role: 'primary', price: 10 }],
    })

    expect(stored.items[0].itemId).toBe('it-1')
    expect(stored.items[0].position).toBe('primary')
  })

  it('fetchSourcingRequests, createSourcingRequest, and updateSourcingRequestStatus work correctly', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          id: 'src-1',
          category: 'lehenga',
          color: 'rose gold',
          itemDescription: 'Rose Gold Lehenga',
          status: 'pending',
        },
      ],
    })

    const list = await fetchSourcingRequests(ORG, 'pending')
    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/sourcing`, {
      params: { status: 'pending' },
    })
    expect(list).toHaveLength(1)

    postMock.mockResolvedValue({
      data: {
        id: 'src-2',
        category: 'saree',
        color: 'emerald',
        itemDescription: 'Emerald Saree',
        status: 'pending',
      },
    })

    const created = await createSourcingRequest(ORG, {
      category: 'saree',
      color: 'emerald',
      description: 'Emerald Saree',
      targetPrice: 500,
    })
    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/sourcing`, {
      organizationId: ORG,
      category: 'saree',
      color: 'emerald',
      description: 'Emerald Saree',
      targetPrice: 500,
      quantityNeeded: 1,
      urgency: 'medium',
      customerId: undefined,
    })
    expect(created.id).toBe('src-2')
    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/sourcing`, {
      params: { status: 'pending' },
    })
    expect(list).toHaveLength(1)

    patchMock.mockResolvedValue({
      data: {
        id: 'src-1',
        category: 'lehenga',
        color: 'rose gold',
        itemDescription: 'Rose Gold Lehenga',
        status: 'approved',
      },
    })

    const updated = await updateSourcingRequestStatus(ORG, 'src-1', 'approved')
    expect(patchMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/sourcing/src-1/status`, {
      status: 'approved',
      notes: undefined,
    })
    expect(updated.status).toBe('approved')
  })

  it('fetchSuppliers and fetchSupplierCatalog work correctly', async () => {
    getMock.mockResolvedValueOnce({
      data: [{ id: 'sup-1', name: 'Heritage Silks', specialty: 'Kanjeevaram' }],
    })

    const suppliers = await fetchSuppliers(ORG)
    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/suppliers`)
    expect(suppliers).toHaveLength(1)

    getMock.mockResolvedValueOnce({
      data: [{ id: 'cat-1', name: 'Raw Silk Fabric', wholesalePrice: 120, inStock: true }],
    })

    const catalog = await fetchSupplierCatalog(ORG, 'sup-1', { category: 'silk' })
    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/suppliers/sup-1/catalog`, {
      params: { category: 'silk' },
    })
    expect(catalog).toHaveLength(1)
  })

  describe('normalizeCategory', () => {
    it('normalizes singular and synonym variants into canonical catalog categories', () => {
      expect(normalizeCategory('saree')).toBe('Sarees')
      expect(normalizeCategory('sari')).toBe('Sarees')
      expect(normalizeCategory('lehenga')).toBe('Lehengas')
      expect(normalizeCategory('ghagra choli')).toBe('Lehengas')
      expect(normalizeCategory('gown')).toBe('Gowns')
      expect(normalizeCategory('maxi dress')).toBe('Gowns')
      expect(normalizeCategory('evening gown')).toBe('Gowns')
      expect(normalizeCategory('kurta')).toBe('Kurtas & Tunics')
      expect(normalizeCategory('anarkali')).toBe('Kurtas & Tunics')
      expect(normalizeCategory('blouse')).toBe('Kurtas & Tunics')
      expect(normalizeCategory('blazer')).toBe('Outerwear')
      expect(normalizeCategory('jacket')).toBe('Outerwear')
      expect(normalizeCategory('shawl')).toBe('Drapes & Shawls')
      expect(normalizeCategory('dupatta')).toBe('Drapes & Shawls')
      expect(normalizeCategory('necklace')).toBe('Jewelry & Accessories')
      expect(normalizeCategory('earring')).toBe('Jewelry & Accessories')
      expect(normalizeCategory('clutch')).toBe('Jewelry & Accessories')
      expect(normalizeCategory('unknown')).toBe('Sarees')
      expect(normalizeCategory(undefined)).toBe('Sarees')
    })
  })

  describe('normalizeVisionAnalysis', () => {
    it('normalizes category, garmentType, suggestedItemName, and the declared wire colour', () => {
      const raw = {
        category: 'saree',
        garment_type: 'Kanjeevaram Silk Saree',
        suggested_item_name: 'Royal Fuchsia Zari Brocade Silk Saree',
        primary_color: 'Fuchsia Magenta',
        color_hex: '#FF00FF',
        fabric: 'Mulberry Silk',
        pattern: 'Gold Zari Brocade',
        style: 'Traditional Heirloom',
      }

      const normalized = normalizeVisionAnalysis(raw)

      expect(normalized.category).toBe('Sarees')
      expect(normalized.garmentType).toBe('Kanjeevaram Silk Saree')
      expect(normalized.suggestedItemName).toBe('Royal Fuchsia Zari Brocade Silk Saree')
      // The sample colour is deliberately not any default literal. When it was 'Emerald Green',
      // the old hardcoded fallback made this assertion pass whether or not `primary_color` was
      // read, so the test could not fail.
      expect(normalized.detectedColor).toBe('Fuchsia Magenta')
      expect(normalized.colorHex).toBe('#FF00FF')
      expect(normalized.fabric).toBe('Mulberry Silk')
      expect(normalized.pattern).toBe('Gold Zari Brocade')
    })

    it('reads the provider wire name primary_color for the detected colour', () => {
      // The real production payload: the .NET DTO pins this property as `primary_color`. Reading
      // camelCase `primaryColor` silently dropped the model's answer.
      const normalized = normalizeVisionAnalysis({
        category: 'Gowns',
        primary_color: 'Fuchsia Magenta',
        garmentType: 'Bodycon Mini Dress',
        fabric: 'Stretch Jersey',
      })

      expect(normalized.detectedColor).toBe('Fuchsia Magenta')
    })

    it('returns no colour when the provider supplied none, never a fabricated default', () => {
      const normalized = normalizeVisionAnalysis({ category: 'Gowns', garmentType: 'Bodycon Mini Dress' })

      expect(normalized.detectedColor).toBeUndefined()
      expect(normalized.colorHex).toBeUndefined()
      expect(normalized.detectedColor).not.toBe('Emerald Green')
      expect(normalized.colorHex).not.toBe(getColorHex('Emerald Green'))
    })

    it('does not invent fabric, pattern, style or a description the provider omitted', () => {
      const normalized = normalizeVisionAnalysis({
        category: 'Gowns',
        primary_color: 'Fuchsia Magenta',
      })

      expect(normalized.fabric).toBeUndefined()
      expect(normalized.pattern).toBeUndefined()
      expect(normalized.style).toBeUndefined()
      expect(normalized.description).toBeUndefined()
      expect(normalized.summary).not.toContain('Exquisite')
    })

    it('keeps only genuinely present values in a visualAttributes fallback, with no undefined entries', () => {
      const normalized = normalizeVisionAnalysis({ category: 'Gowns', primary_color: 'Fuchsia Magenta' })

      expect(normalized.visualAttributes).toEqual(['Fuchsia Magenta'])
      expect(normalized.visualAttributes).not.toContain(undefined)

      const fromKeywords = normalizeVisionAnalysis({
        category: 'Gowns',
        suggestedKeywords: ['gown', 'fuchsia'],
      })
      expect(fromKeywords.visualAttributes).toEqual(['gown', 'fuchsia'])
    })
  })

  describe('uploadCatalogImage and uploadBase64Image', () => {
    it('uploadCatalogImage posts multipart form data to images/upload', async () => {
      postMock.mockResolvedValue({
        data: {
          id: 'img-1',
          url: `/api/v1/orgs/${ORG}/catalog/images/img-1`,
          fileName: 'saree.jpg',
        },
      })

      const blob = new Blob(['fake image bytes'], { type: 'image/jpeg' })
      const result = await uploadCatalogImage(ORG, blob, 'saree.jpg')

      expect(postMock).toHaveBeenCalledWith(
        `/api/v1/orgs/${ORG}/catalog/images/upload`,
        expect.any(FormData),
        {
          headers: {
            'Content-Type': 'multipart/form-data',
          },
        },
      )
      expect(result.id).toBe('img-1')
      expect(result.url).toBe(`/api/v1/orgs/${ORG}/catalog/images/img-1`)
    })

    it('uploadBase64Image posts base64 payload to images/upload', async () => {
      postMock.mockResolvedValue({
        data: {
          id: 'img-2',
          url: `/api/v1/orgs/${ORG}/catalog/images/img-2`,
          fileName: 'garment.jpg',
        },
      })

      const dataUrl = 'data:image/jpeg;base64,12345'
      const result = await uploadBase64Image(ORG, dataUrl, 'garment.jpg')

      expect(postMock).toHaveBeenCalledWith(
        `/api/v1/orgs/${ORG}/catalog/images/upload`,
        {
          imageData: dataUrl,
          fileName: 'garment.jpg',
        },
      )
      expect(result.id).toBe('img-2')
      expect(result.url).toBe(`/api/v1/orgs/${ORG}/catalog/images/img-2`)
    })
  })

  describe('colorHex on the catalog wire', () => {
    it('posts the analysed hex when creating an item and reads the API hex back', async () => {
      const payload = {
        itemName: 'Fuchsia Bodycon Dress',
        category: 'Gowns',
        color: 'Fuchsia Pink',
        colorHex: '#D5006D',
        sizes: ['S'],
        price: 18000,
        quantity: 2,
      }
      postMock.mockResolvedValue({ data: { id: 'new-1', ...payload } })

      const result = await createCatalogItem(ORG, payload)

      expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/catalog/items`, payload)
      expect(result.colorHex).toBe('#D5006D')
    })

    it('reads the API hex through normalizeInventoryItem', () => {
      const result = normalizeInventoryItem({
        id: '1',
        itemName: 'Fuchsia Bodycon Dress',
        color: 'Fuchsia Pink',
        colorHex: '#D5006D',
      })

      expect(result.colorHex).toBe('#D5006D')
    })

    it('normalizes an item response with no hex to undefined, never the grey placeholder', () => {
      // `#4B5563` (Tailwind gray-600) used to stand in for a colour nobody measured. That literal
      // is exactly the flat default dot the operator saw instead of the analysed garment colour.
      const result = normalizeInventoryItem({
        id: '1',
        itemName: 'Fuchsia Bodycon Dress',
        color: 'Fuchsia Pink',
      })

      expect(result.colorHex).toBeUndefined()
      expect(result.colorHex).not.toBe('#4B5563')
    })
  })

  describe('getColorHex and normalizeOutfitComposition', () => {
    it('getColorHex resolves exact, partial, and fallback hex values', () => {
      expect(getColorHex(undefined)).toBe('#0f5132')
      expect(getColorHex('')).toBe('#0f5132')
      expect(getColorHex('emerald')).toBe('#0f5132')
      expect(getColorHex('emerald green')).toBe('#0f5132')
      expect(getColorHex('maroon')).toBe('#800000')
      expect(getColorHex('unknown neon tone', '#123456')).toBe('#123456')
    })

    it('normalizeOutfitComposition handles raw objects with fallback defaults', () => {
      const raw = {
        id: 'outfit-1',
        name: 'Royal Saree Ensemble',
        occasion: 'Wedding',
        totalPrice: '1200',
        styleNotes: 'Rich festive drape',
        heroImageUrl: 'http://example.com/img.jpg',
        organizationId: ORG,
        items: [
          { id: 'it-1', itemId: 'it-1', name: 'Silk Saree', category: 'Sarees', price: 1000 },
          { id: 'it-2', itemName: 'Gold Choker', price: '200' },
        ],
      }

      const result = normalizeOutfitComposition(raw)
      expect(result.id).toBe('outfit-1')
      expect(result.totalPrice).toBe(1200)
      expect(result.items).toHaveLength(2)
      expect(result.items[1].name).toBe('Gold Choker')
      expect(result.items[1].price).toBe(200)

      const fallback = normalizeOutfitComposition({})
      expect(fallback.name).toBe('Curated Ensemble')
      expect(fallback.totalPrice).toBe(0)
      expect(fallback.items).toEqual([])
    })
  })
})

