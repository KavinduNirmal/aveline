import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { LookbooksTab } from './LookbooksTab'
import type { OutfitCompositionMock } from './mockData'

function outfit(overrides: Partial<OutfitCompositionMock> = {}): OutfitCompositionMock {
  return {
    id: 'look-1',
    name: 'Royal Sangeet Ensemble',
    occasion: 'Sangeet & Reception',
    totalPrice: 2400,
    styleNotes: 'Pair with polki jewelry.',
    heroImageUrl: '',
    createdAt: '2026-09-22T00:00:00Z',
    items: [
      {
        id: 'oi-1',
        itemId: 'item-1',
        name: 'Emerald Silk Saree',
        category: 'Sarees',
        price: 1500,
        imageUrl: '',
        position: 'primary',
      },
      {
        id: 'oi-2',
        itemId: 'item-2',
        name: 'Gold Choker',
        category: 'Jewelry & Accessories',
        price: 900,
        imageUrl: '',
        position: 'accessory',
      },
    ],
    ...overrides,
  }
}

/** Radix opens its menu from the trigger's key handler; a synthesized pointer click can be swallowed. */
async function openRowMenu(name: RegExp) {
  screen.getByRole('button', { name }).focus()
  await userEvent.keyboard('{ArrowDown}')
}

describe('LookbooksTab CRUD', () => {
  it('renames a lookbook through the edit dialog', async () => {
    const onUpdateLookbook = vi.fn().mockResolvedValue(true)
    const user = userEvent.setup()

    render(
      <LookbooksTab
        outfits={[outfit()]}
        onComposeLook={vi.fn()}
        onUpdateLookbook={onUpdateLookbook}
        onDeleteLookbook={vi.fn()}
      />,
    )

    await openRowMenu(/actions for royal sangeet ensemble/i)
    await user.click(await screen.findByRole('menuitem', { name: /edit lookbook/i }))

    const dialog = await screen.findByRole('dialog', { name: /edit lookbook/i })
    const name = within(dialog).getByLabelText(/look name/i)
    await user.clear(name)
    await user.type(name, 'Royal Gala Ensemble')
    await user.click(within(dialog).getByRole('button', { name: /save changes/i }))

    await waitFor(() => expect(onUpdateLookbook).toHaveBeenCalledTimes(1))
    expect(onUpdateLookbook.mock.calls[0][0]).toBe('look-1')
    expect(onUpdateLookbook.mock.calls[0][1]).toMatchObject({ name: 'Royal Gala Ensemble' })
  })

  it('removes a lookbook only after the confirmation is accepted', async () => {
    const onDeleteLookbook = vi.fn().mockResolvedValue(true)
    const user = userEvent.setup()

    render(
      <LookbooksTab
        outfits={[outfit()]}
        onComposeLook={vi.fn()}
        onUpdateLookbook={vi.fn()}
        onDeleteLookbook={onDeleteLookbook}
      />,
    )

    await openRowMenu(/actions for royal sangeet ensemble/i)
    await user.click(await screen.findByRole('menuitem', { name: /delete lookbook/i }))

    const dialog = await screen.findByRole('dialog', { name: /delete lookbook/i })
    expect(onDeleteLookbook).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: /^delete lookbook$/i }))

    await waitFor(() => expect(onDeleteLookbook).toHaveBeenCalledWith('look-1'))
  })

  it('offers the empty state when the occasion filter matches nothing', async () => {
    const user = userEvent.setup()
    render(
      <LookbooksTab
        outfits={[outfit()]}
        onComposeLook={vi.fn()}
        onUpdateLookbook={vi.fn()}
        onDeleteLookbook={vi.fn()}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Bridal Heirloom' }))

    expect(screen.getByText(/no lookbooks found/i)).toBeInTheDocument()
  })
})
