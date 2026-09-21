import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { AddProductModal } from './AddProductModal'
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

function renderDrawer(overrides: Partial<React.ComponentProps<typeof AddProductModal>> = {}) {
  return render(
    <AddProductModal
      open
      organizationId="11111111-1111-1111-1111-111111111111"
      onClose={noop}
      onSave={noop}
      {...overrides}
    />,
  )
}

describe('the add/edit piece drawer', () => {
  it('opens as a right-hand side drawer, not a centred modal', () => {
    // The piece form is long; a centred dialog fought the photograph, the extracted attributes and
    // the floor tag for the same small box. `right-0` is the shadcn sheet's right side; a centred
    // modal would be `inset-x-0`.
    renderDrawer()

    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveClass('right-0')
    expect(dialog).not.toHaveClass('inset-x-0')
  })

  it('titles itself "Add a piece" for a new piece and "Edit piece" for an existing one', () => {
    const { unmount } = renderDrawer()
    expect(screen.getByRole('heading', { name: /add a piece/i })).toBeInTheDocument()
    unmount()

    renderDrawer({ editingItem: item() })
    expect(screen.getByRole('heading', { name: /edit piece/i })).toBeInTheDocument()
  })

  it('offers both photograph sources through one labelled control', () => {
    renderDrawer()

    expect(screen.getByRole('radiogroup', { name: /photograph source/i })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /upload file/i })).toBeChecked()
    expect(screen.getByRole('radio', { name: /image url/i })).toBeInTheDocument()
  })

  it('does not quote the piece in dollars anywhere in the form', () => {
    // The floor tag inside the drawer prices the piece through the shared formatter; a bare `$`
    // would quote it in a currency the server never sent.
    renderDrawer({ editingItem: item() })

    expect(screen.queryByText(/\$/)).not.toBeInTheDocument()
  })

  it('submits the piece the operator typed', async () => {
    const onSave = vi.fn()
    renderDrawer({ onSave })

    await userEvent.type(screen.getByLabelText(/item name/i), 'Ivory Kanjeevaram')
    await userEvent.click(screen.getByRole('button', { name: /add to catalog/i }))

    expect(onSave).toHaveBeenCalledTimes(1)
    expect(onSave.mock.calls[0][0]).toMatchObject({ name: 'Ivory Kanjeevaram' })
  })
})
