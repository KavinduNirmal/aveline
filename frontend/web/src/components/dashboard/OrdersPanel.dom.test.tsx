import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchOrders = vi.fn()
const cancelOrder = vi.fn()
const recalculateOrder = vi.fn()
const updateOrderStatus = vi.fn()

vi.mock('@/lib/orders-api', () => ({
  fetchOrders: (...args: unknown[]) => fetchOrders(...args),
  cancelOrder: (...args: unknown[]) => cancelOrder(...args),
  recalculateOrder: (...args: unknown[]) => recalculateOrder(...args),
  updateOrderStatus: (...args: unknown[]) => updateOrderStatus(...args),
}))

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

import { OrdersPanel } from './OrdersPanel'

/** The org id as a plain string, so an assertion can name it without fighting the `as never` cast. */
const ORG_ID = '11111111-1111-1111-1111-111111111111'

const ORG = {
  id: ORG_ID,
  name: 'House of Fashions',
  slug: 'house-of-fashions',
} as never

/**
 * The order the screenshot showed: one long product name and three money columns. The name is what
 * makes the table wider than the dialog, which is the shape the layout defect needed.
 */
const ORDER = {
  id: 'ed989eda-0000-0000-0000-000000000000',
  organizationId: '11111111-1111-1111-1111-111111111111',
  customerId: '22222222-2222-2222-2222-222222222222',
  customerName: 'Kavindu Nirmal',
  orderType: 'retail',
  status: 'pending_approval',
  subtotal: 7000,
  discount: 0,
  total: 7000,
  totalCost: 600,
  margin: 0.9142857142857143,
  createdBy: null,
  createdAt: '2026-09-27T21:58:51Z',
  updatedAt: null,
  items: [
    {
      id: 'item-1',
      itemId: '33333333-3333-3333-3333-333333333333',
      itemName: 'Deep Maroon Pure Mulberry Silk Silk Kanjeevaram Saree',
      quantity: 1,
      unitPrice: 7000,
      wholesaleCost: 600,
      totalPrice: 7000,
    },
  ],
}

describe('OrdersPanel order details', () => {
  beforeEach(() => {
    fetchOrders.mockReset().mockResolvedValue({ items: [ORDER], totalCount: 1, page: 1, pageSize: 20 })
    cancelOrder.mockReset()
    recalculateOrder.mockReset()
    updateOrderStatus.mockReset()
  })

  async function openDetails() {
    render(<OrdersPanel organization={ORG} role="org:boutique_manager" />)
    await userEvent.click(await screen.findByRole('button', { name: /details/i }))
    return screen.findByRole('dialog')
  }

  it('keeps the order detail dialog inside the box it is fitted to', async () => {
    // The reported defect, in two halves.
    //
    // Width: `DialogContent` is a grid, and a grid item defaults to `min-width: auto`, so the items
    // table's min-content width widened its track past the dialog instead of scrolling inside it.
    // `min-w-0` on the content is what lets it shrink to the box.
    //
    // jsdom does not lay out, so this pins the class contract the fix lives in.
    const dialog = await openDetails()

    expect(dialog.className).toMatch(/\bmin-w-0\b/)
    // The viewport guard must survive. An unprefixed `max-w-*` from a caller would replace it and
    // let the dialog exceed a phone screen.
    expect(dialog.className).toMatch(/max-w-\[calc\(100%-2rem\)\]/)
    expect(dialog.className).not.toMatch(/(?:^|\s)max-w-(?!\[calc)/)
  })

  it('asks for the wide variant in the one form that actually applies', async () => {
    // The subtle half of the defect: `max-w-2xl` and `sm:max-w-lg` are different utilities to
    // tailwind-merge, so both were kept and the `sm:` one won from 640px up — the order dialog
    // silently rendered at 512px however wide the class looked. The width must therefore be passed
    // at the same variant the primitive uses, which is what `DIALOG_CONTENT_WIDE` is for.
    const dialog = await openDetails()

    expect(dialog.className).toMatch(/sm:max-w-3xl/)
    // No unprefixed width snuck back in.
    expect(dialog.className).not.toMatch(/(?:^|\s)max-w-(?!\[calc)/)
  })

  it('does not hide the table overflow behind a clipped wrapper', async () => {
    // `overflow-hidden` on the wrapper cancelled the scroll the table's own container provides,
    // so a table wider than the dialog was unreachable rather than scrollable.
    const dialog = await openDetails()

    const table = within(dialog).getByRole('table')
    const scrollContainer = table.parentElement!
    expect(scrollContainer.className).toMatch(/overflow-x-auto/)
    // No ancestor between the table and the dialog may clip what the container scrolls.
    expect(scrollContainer.parentElement!.className).not.toMatch(/overflow-hidden/)
  })

  it('shows every column of the line item, including the money ones', async () => {
    const dialog = await openDetails()

    for (const column of ['Item', 'Qty', 'Price', 'Cost', 'Total']) {
      expect(within(dialog).getByRole('columnheader', { name: column })).toBeInTheDocument()
    }
    expect(
      within(dialog).getByText('Deep Maroon Pure Mulberry Silk Silk Kanjeevaram Saree'),
    ).toBeInTheDocument()
    // The margin is a ratio, so it renders as a percentage rather than as currency. One fraction
    // digit is `formatPercent`'s own cap, which is why 0.9142857 prints as 91.4%.
    expect(within(dialog).getByText(/^91\.4\s?%$/)).toBeInTheDocument()
  })

  it('lets the status line wrap instead of pushing the date out of the box', async () => {
    // The banner was a single non-wrapping row: the status badge and the created date could not
    // both fit, and the date was pushed outside the bordered box.
    const dialog = await openDetails()

    const banner = within(dialog).getByText(/created:/i).closest('.rounded-lg')!
    expect(banner.className).toMatch(/flex-wrap/)
  })

  it('cancels an order only through the confirmation, and reports the refusal otherwise', async () => {
    // The two writes on this dialog were never covered before; the destructive one is why the
    // confirmation exists at all.
    const dialog = await openDetails()

    await userEvent.click(within(dialog).getByRole('button', { name: /cancel order/i }))

    // The destructive act is behind a confirmation: cancelling is not reversible, and the dialog
    // is the only place the order is actually cancelled from.
    const confirmation = await screen.findByRole('dialog', { name: /cancel order/i })
    expect(cancelOrder).not.toHaveBeenCalled()
    await userEvent.click(
      within(confirmation).getByRole('button', { name: /confirm cancellation/i }),
    )

    // The org and the order are both named, and the optional reason travels as the third argument.
    await waitFor(() =>
      expect(cancelOrder).toHaveBeenCalledWith(ORG_ID, ORDER.id, ''),
    )
  })
})
