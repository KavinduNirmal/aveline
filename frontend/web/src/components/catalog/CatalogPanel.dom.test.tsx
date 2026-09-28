import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchCatalogItemsMock = vi.hoisted(() => vi.fn())
const fetchCatalogItemMock = vi.hoisted(() => vi.fn())
const createCatalogItemMock = vi.hoisted(() => vi.fn())
const updateCatalogItemMock = vi.hoisted(() => vi.fn())
const recordCatalogSaleMock = vi.hoisted(() => vi.fn())
const updateLookbookMock = vi.hoisted(() => vi.fn())
const deleteLookbookMock = vi.hoisted(() => vi.fn())
// The hex the stand-in drawer emits. Empty means "no measurement", which the panel must omit.
const emittedHex = vi.hoisted(() => ({ value: '#D5006D' }))

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/catalog-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/catalog-api')>()
  return {
    ...actual,
    fetchCatalogItems: (...args: unknown[]) => fetchCatalogItemsMock(...args),
    fetchCatalogItem: (...args: unknown[]) => fetchCatalogItemMock(...args),
    fetchLookbooks: async () => [],
    fetchSourcingRequests: async () => [],
    fetchSuppliers: async () => [],
    createCatalogItem: (...args: unknown[]) => createCatalogItemMock(...args),
    updateCatalogItem: (...args: unknown[]) => updateCatalogItemMock(...args),
    deleteCatalogItem: async () => undefined,
    recordCatalogSale: (...args: unknown[]) => recordCatalogSaleMock(...args),
    updateLookbook: (...args: unknown[]) => updateLookbookMock(...args),
    deleteLookbook: (...args: unknown[]) => deleteLookbookMock(...args),
    updateSourcingRequestStatus: async () => undefined,
    createSourcingRequest: async () => undefined,
  }
})

// The drawer has its own test file. Here it is a stand-in that emits one known analysed piece, so
// the only thing under test is how `CatalogPanel.handleSaveProduct` turns that piece into a payload.
vi.mock('./AddProductModal', () => ({
  AddProductModal: ({
    editingItem,
    onSave,
  }: {
    editingItem?: InventoryItemMock | null
    onSave: (item: InventoryItemMock) => void
  }) => (
    <button
      type="button"
      onClick={() =>
        onSave({
          id: editingItem?.id ?? 'new-1',
          name: 'Fuchsia Bodycon Dress',
          sku: 'AVL-900',
          category: 'Gowns',
          color: 'Fuchsia Pink',
          colorHex: emittedHex.value,
          fabric: 'Stretch Jersey',
          style: 'Modern',
          sizes: ['S'],
          price: 18000,
          cost: 8000,
          stockQuantity: 2,
          status: 'available',
          imageUrl: '',
          createdAt: '2026-09-22T00:00:00Z',
        })
      }
    >
      emit-analysed-piece
    </button>
  ),
}))

import { CatalogPanel } from './CatalogPanel'
import type { InventoryItemMock } from './mockData'
import type { OrganizationProfileDto } from '@/types/organization'

const ORGANIZATION = { id: 'org-1', name: 'Aveline' } as OrganizationProfileDto

const INVENTORY_ITEM: InventoryItemMock = {
  id: 'item-1',
  name: 'Royal Emerald Silk Saree',
  sku: 'AVL-851',
  category: 'Sarees',
  color: 'Emerald Green',
  colorHex: '#046307',
  fabric: 'Pure Mulberry Silk',
  style: 'Zari Brocade',
  sizes: ['38', '40'],
  price: 1250,
  cost: 550,
  stockQuantity: 4,
  status: 'available',
  imageUrl: '',
  createdAt: '2026-09-22T00:00:00Z',
}

describe('CatalogPanel colour persistence', () => {
  beforeEach(() => {
    fetchCatalogItemsMock.mockReset()
    fetchCatalogItemMock.mockReset()
    createCatalogItemMock.mockReset()
    updateCatalogItemMock.mockReset()
    recordCatalogSaleMock.mockReset()
    updateLookbookMock.mockReset()
    deleteLookbookMock.mockReset()
    emittedHex.value = '#D5006D'

    // The list is the source for these cases; a deep-link lookup finds nothing.
    fetchCatalogItemMock.mockRejectedValue(new Error('not found'))

    fetchCatalogItemsMock.mockResolvedValue([])
    createCatalogItemMock.mockResolvedValue({
      id: 'new-1',
      name: 'Fuchsia Bodycon Dress',
      category: 'Gowns',
      color: 'Fuchsia Pink',
      colorHex: '#D5006D',
      fabric: 'Stretch Jersey',
      style: 'Modern',
      sizes: ['S'],
      price: 18000,
      cost: 8000,
      stockQuantity: 2,
      status: 'available',
      imageUrl: '',
      createdAt: '2026-09-22T00:00:00Z',
    })
    updateCatalogItemMock.mockResolvedValue({
      id: 'item-1',
      name: 'Fuchsia Bodycon Dress',
      category: 'Gowns',
      color: 'Fuchsia Pink',
      colorHex: '#D5006D',
      sizes: ['S'],
      price: 18000,
      stockQuantity: 2,
      status: 'available',
      imageUrl: '',
    })
  })

  it('includes the analysed hex in the create payload', async () => {
    const user = userEvent.setup()
    render(<CatalogPanel organization={ORGANIZATION} />)

    await waitFor(() => expect(fetchCatalogItemsMock).toHaveBeenCalledTimes(1))
    await user.click(await screen.findByRole('button', { name: 'emit-analysed-piece' }))

    await waitFor(() => expect(createCatalogItemMock).toHaveBeenCalledTimes(1))
    expect(createCatalogItemMock.mock.calls[0][1]).toMatchObject({ colorHex: '#D5006D' })
  })

  it('omits the hex field entirely when the piece carries no measurement', async () => {
    emittedHex.value = ''
    const user = userEvent.setup()
    render(<CatalogPanel organization={ORGANIZATION} />)

    await waitFor(() => expect(fetchCatalogItemsMock).toHaveBeenCalledTimes(1))
    await user.click(await screen.findByRole('button', { name: 'emit-analysed-piece' }))

    await waitFor(() => expect(createCatalogItemMock).toHaveBeenCalledTimes(1))
    const payload = createCatalogItemMock.mock.calls[0][1] as { colorHex?: string }
    expect(payload.colorHex).toBeUndefined()
  })

  it('includes the analysed hex in the update payload', async () => {
    fetchCatalogItemsMock.mockResolvedValue([INVENTORY_ITEM])
    render(<CatalogPanel organization={ORGANIZATION} />)

    await screen.findByRole('heading', { name: 'Royal Emerald Silk Saree' })
    // Open the row menu the way a keyboard operator does. Radix opens the menu from the trigger's
    // key handler; a synthesized pointer click is swallowed once an earlier panel test has run.
    screen.getByRole('button', { name: /actions for royal emerald silk saree/i }).focus()
    await userEvent.keyboard('{ArrowDown}')
    await userEvent.click(await screen.findByRole('menuitem', { name: /edit piece/i }))
    await userEvent.click(screen.getByRole('button', { name: 'emit-analysed-piece' }))

    await waitFor(() => expect(updateCatalogItemMock).toHaveBeenCalledTimes(1))
    expect(updateCatalogItemMock.mock.calls[0][1]).toBe('item-1')
    expect(updateCatalogItemMock.mock.calls[0][2]).toMatchObject({ colorHex: '#D5006D' })
  })
})

describe('CatalogPanel piece information page', () => {
  beforeEach(() => {
    fetchCatalogItemsMock.mockReset()
    fetchCatalogItemMock.mockReset()
    recordCatalogSaleMock.mockReset()
    updateCatalogItemMock.mockReset()
    fetchCatalogItemsMock.mockResolvedValue([INVENTORY_ITEM])
    fetchCatalogItemMock.mockRejectedValue(new Error('not found'))
    updateCatalogItemMock.mockImplementation(
      (_orgId: string, _itemId: string, payload: { quantity?: number; status?: string }) => ({
        ...INVENTORY_ITEM,
        stockQuantity: payload.quantity ?? INVENTORY_ITEM.stockQuantity,
        status: payload.status ?? INVENTORY_ITEM.status,
      }),
    )
    recordCatalogSaleMock.mockResolvedValue({
      itemId: 'item-1',
      itemName: 'Royal Emerald Silk Saree',
      sku: 'AVL-851',
      quantitySold: 2,
      unitPrice: 1250,
      totalAmount: 2500,
      remainingStock: 2,
      status: 'low_stock',
      ledgerEntryId: 'ledger-1',
      recordedAtUtc: '2026-09-22T00:00:00Z',
    })
  })

  it('opens the piece named by the URL, and its back control closes it', async () => {
    const onCloseItem = vi.fn()
    const user = userEvent.setup()
    render(
      <CatalogPanel
        organization={ORGANIZATION}
        openItemId="item-1"
        onOpenItem={vi.fn()}
        onCloseItem={onCloseItem}
      />,
    )

    // The information page leads with the piece, its SKU and its money.
    expect(await screen.findByRole('heading', { name: 'Royal Emerald Silk Saree' })).toBeInTheDocument()
    expect(screen.getByText(/AVL-851/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /record sale/i })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /back to catalog/i }))
    expect(onCloseItem).toHaveBeenCalledTimes(1)
  })

  it('records a counter sale and applies the server receipt to the piece', async () => {
    const user = userEvent.setup()
    render(<CatalogPanel organization={ORGANIZATION} openItemId="item-1" />)

    await screen.findByRole('heading', { name: 'Royal Emerald Silk Saree' })
    await user.click(screen.getByRole('button', { name: /record sale/i }))

    const dialog = await screen.findByRole('dialog', { name: /record a sale of royal emerald silk saree/i })
    const quantity = within(dialog).getByLabelText(/pieces sold/i)
    await user.clear(quantity)
    await user.type(quantity, '2')
    await user.click(within(dialog).getByRole('button', { name: /^record sale$/i }))

    await waitFor(() => expect(recordCatalogSaleMock).toHaveBeenCalledTimes(1))
    expect(recordCatalogSaleMock.mock.calls[0][1]).toBe('item-1')
    expect(recordCatalogSaleMock.mock.calls[0][2]).toMatchObject({ quantity: 2, unitPrice: 1250 })

    // The server's remaining stock is the truth shown on the page, not a local subtraction.
    expect(await screen.findByText(/low stock · 2/i)).toBeInTheDocument()
  })

  it('fetches a linked piece that the loaded list page does not carry', async () => {
    fetchCatalogItemsMock.mockResolvedValue([])
    fetchCatalogItemMock.mockResolvedValue(INVENTORY_ITEM)

    render(<CatalogPanel organization={ORGANIZATION} openItemId="item-1" />)

    expect(await screen.findByRole('heading', { name: 'Royal Emerald Silk Saree' })).toBeInTheDocument()
    expect(fetchCatalogItemMock).toHaveBeenCalledWith('org-1', 'item-1')
  })

  it('reduces stock through the catalog update, not the takings journal', async () => {
    const user = userEvent.setup()
    render(<CatalogPanel organization={ORGANIZATION} openItemId="item-1" />)

    await screen.findByRole('heading', { name: 'Royal Emerald Silk Saree' })
    await user.click(screen.getByRole('button', { name: /reduce stock/i }))

    const dialog = await screen.findByRole('dialog', {
      name: /reduce stock for royal emerald silk saree/i,
    })
    const quantity = within(dialog).getByLabelText(/pieces to remove/i)
    await user.clear(quantity)
    await user.type(quantity, '2')
    await user.click(within(dialog).getByRole('button', { name: /^reduce stock$/i }))

    await waitFor(() => expect(updateCatalogItemMock).toHaveBeenCalledTimes(1))
    expect(updateCatalogItemMock.mock.calls[0][1]).toBe('item-1')
    // Two left derives low_stock, the same status the drawer and a counter sale derive.
    expect(updateCatalogItemMock.mock.calls[0][2]).toMatchObject({ quantity: 2, status: 'low_stock' })
    // No money moved, so no sale was recorded.
    expect(recordCatalogSaleMock).not.toHaveBeenCalled()
    expect(await screen.findByText(/low stock · 2/i)).toBeInTheDocument()
  })

  it('marks a piece out of stock as a zero count with the reserved status', async () => {
    const user = userEvent.setup()
    render(<CatalogPanel organization={ORGANIZATION} openItemId="item-1" />)

    await screen.findByRole('heading', { name: 'Royal Emerald Silk Saree' })
    await user.click(screen.getByRole('button', { name: /mark out of stock/i }))

    const dialog = await screen.findByRole('dialog', {
      name: /mark royal emerald silk saree out of stock/i,
    })
    await user.click(within(dialog).getByRole('button', { name: /^mark out of stock$/i }))

    await waitFor(() => expect(updateCatalogItemMock).toHaveBeenCalledTimes(1))
    expect(updateCatalogItemMock.mock.calls[0][2]).toMatchObject({ quantity: 0, status: 'reserved' })
  })

  it('reports a piece the catalog does not carry instead of rendering nothing', async () => {
    fetchCatalogItemsMock.mockResolvedValue([])
    render(<CatalogPanel organization={ORGANIZATION} openItemId="missing-piece" />)

    expect(await screen.findByText(/not in the catalog/i)).toBeInTheDocument()
  })
})
