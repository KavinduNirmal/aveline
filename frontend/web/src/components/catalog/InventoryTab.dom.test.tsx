import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { InventoryTab } from './InventoryTab'
import type { InventoryItemMock } from './mockData'

const mockInventory: InventoryItemMock[] = [
  {
    id: 'item-1',
    name: 'Silk Banarasi Saree',
    sku: 'AVL-101',
    category: 'Sarees',
    color: 'Crimson',
    fabric: 'Silk',
    style: 'Traditional',
    sizes: ['Free Size'],
    price: 450,
    cost: 200,
    stockQuantity: 1,
    status: 'low_stock',
    imageUrl: '/mock/saree.jpg',
    createdAt: '2026-01-01',
  },
  {
    id: 'item-2',
    name: 'Embroidered Anarkali',
    sku: 'AVL-102',
    category: 'Gowns',
    color: 'Emerald',
    fabric: 'Georgette',
    style: 'Contemporary',
    sizes: ['M', 'L'],
    price: 320,
    cost: 150,
    stockQuantity: 10,
    status: 'available',
    imageUrl: '/mock/anarkali.jpg',
    createdAt: '2026-01-02',
  },
]

describe('InventoryTab - Low Stock Warning Banner', () => {
  const defaultProps = {
    inventory: mockInventory,
    onViewMatches: vi.fn(),
    onComposeOutfit: vi.fn(),
    onEditItem: vi.fn(),
    onViewQr: vi.fn(),
    onDeleteItem: vi.fn(),
  }

  it('renders low stock warning banner when low stock items exist', () => {
    render(<InventoryTab {...defaultProps} />)
    expect(screen.getByText(/low stock levels/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /filter low stock/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /dismiss/i })).toBeInTheDocument()
  })

  it('dismisses the warning banner when the dismiss (X) button is clicked', async () => {
    const user = userEvent.setup()
    render(<InventoryTab {...defaultProps} />)

    const dismissButton = screen.getByRole('button', { name: /dismiss/i })
    expect(dismissButton).toBeInTheDocument()

    await user.click(dismissButton)

    expect(screen.queryByText(/low stock levels/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument()
  })
})
