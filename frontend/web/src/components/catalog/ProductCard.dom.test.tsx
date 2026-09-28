import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ProductCard } from './ProductCard'
import type { InventoryItemMock } from './mockData'

function item(overrides: Partial<InventoryItemMock> = {}): InventoryItemMock {
  return {
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
    imageUrl: 'https://example.test/saree.jpg',
    description: 'Handwoven.',
    createdAt: '2026-09-21T09:00:00Z',
    ...overrides,
  }
}

const noop = () => {}

function renderCard(overrides: Partial<InventoryItemMock> = {}) {
  return render(
    <ProductCard
      item={item(overrides)}
      onViewMatches={noop}
      onComposeOutfit={noop}
    />,
  )
}

describe('ProductCard', () => {
  it('prices the piece in the dashboard currency, not in dollars', () => {
    // The tenant surface has one money formatter and it reports LKR. A bare `$1,250` asserted a
    // currency the server never sent this item.
    renderCard()

    expect(screen.getByText(/1,250\.00/)).toBeInTheDocument()
    expect(screen.queryByText(/\$/)).not.toBeInTheDocument()
  })

  it('does not claim a Vision AI confidence the list payload does not carry', () => {
    // `InventoryItemDto` has no `ConfidenceScore`; the normaliser defaulted it to 0.95, so every
    // card reported "95% Vision AI" regardless of what any analysis produced. The item below
    // carries that value and the card must still not report one.
    renderCard({ confidenceScore: 0.95 })

    expect(screen.queryByText(/vision ai/i)).not.toBeInTheDocument()
  })

  it('leads with the piece and keeps its stock state legible', () => {
    renderCard({ status: 'low_stock', stockQuantity: 1 })

    expect(screen.getByRole('heading', { name: 'Royal Emerald Silk Saree' })).toBeInTheDocument()
    // SKU and category share the identity line, which is why this matches on the SKU alone.
    expect(screen.getByText(/AVL-851/)).toBeInTheDocument()
    expect(screen.getByText(/low stock/i)).toBeInTheDocument()
  })

  it('paints the swatch with the colour the analysis measured', () => {
    const { container } = renderCard({ colorHex: '#D5006D' })

    // `VisualAttributesBadge` renders the dot only when a hex is present; the inline style is the
    // dot and nothing else on the card carries an inline colour.
    const swatch = container.querySelector('span[aria-hidden][style]')
    expect(swatch).not.toBeNull()
    expect(swatch).toHaveStyle({ backgroundColor: '#D5006D' })
  })

  it('renders no swatch when the item carries no measured hex, never a grey default', () => {
    const { container } = renderCard({ colorHex: undefined })

    // The colour name is still observed and shown; only the unmeasured dot is withheld.
    expect(screen.getByText('Emerald Green')).toBeInTheDocument()
    expect(container.querySelector('span[aria-hidden][style]')).toBeNull()
    // #4B5563 is Tailwind gray-600, the placeholder the normaliser used to invent.
    expect(container.querySelector('[style*="75, 85, 99"]')).toBeNull()
  })

  it('collects the piece actions into one menu and offers only the ones it was given', async () => {
    const onEditItem = vi.fn()
    render(
      <ProductCard
        item={item()}
        onViewMatches={noop}
        onComposeOutfit={noop}
        onEditItem={onEditItem}
      />,
    )

    // The primary actions stay on the card; the row actions do not compete with them.
    expect(screen.getByRole('button', { name: /vip matches/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /style look/i })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /actions for/i }))

    expect(await screen.findByRole('menuitem', { name: /edit piece/i })).toBeInTheDocument()
    // No QR and no delete handler were passed, so neither is offered.
    expect(screen.queryByRole('menuitem', { name: /qr/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: /delete/i })).not.toBeInTheDocument()
  })

  it('offers the stock adjustments it was given, and not the ones it was not', async () => {
    render(
      <ProductCard
        item={item()}
        onViewMatches={noop}
        onComposeOutfit={noop}
        onReduceStock={vi.fn()}
        onMarkOutOfStock={vi.fn()}
      />,
    )

    // Radix opens its menu from the trigger's key handler; a synthesized pointer click can be
    // swallowed once an earlier DOM test has run in the same file.
    screen.getByRole('button', { name: /actions for/i }).focus()
    await userEvent.keyboard('{ArrowDown}')

    expect(await screen.findByRole('menuitem', { name: /reduce stock/i })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: /mark out of stock/i })).toBeInTheDocument()
  })

  it('opens the piece from its image and from its name when a handler is given', async () => {
    const onOpen = vi.fn()
    render(
      <ProductCard
        item={item()}
        onViewMatches={noop}
        onComposeOutfit={noop}
        onOpen={onOpen}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: /view details for royal emerald silk saree/i }))
    expect(onOpen).toHaveBeenCalledTimes(1)

    // The name is the second affordance, so a reader who aims at the text still opens the piece.
    await userEvent.click(screen.getByRole('button', { name: 'Royal Emerald Silk Saree' }))
    expect(onOpen).toHaveBeenCalledTimes(2)
  })

  it('stays a plain tile with no dead open controls when no handler is given', () => {
    renderCard()

    expect(screen.queryByRole('button', { name: /view details for/i })).not.toBeInTheDocument()
    // The name is still a heading, not a button, so nothing invites a click that does nothing.
    expect(screen.getByRole('heading', { name: 'Royal Emerald Silk Saree' })).toBeInTheDocument()
  })
})
