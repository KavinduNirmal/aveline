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
})
