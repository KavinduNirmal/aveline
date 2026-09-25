import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api-error'

const fetchTopUpPacks = vi.fn()
const createTopUpCheckout = vi.fn()
const pollPaymentIntent = vi.fn()
const cancelPaymentIntent = vi.fn()

vi.mock('@/lib/payments', async () => {
  const actual = await vi.importActual<typeof import('@/lib/payments')>('@/lib/payments')
  return {
    ...actual,
    fetchTopUpPacks: (...a: unknown[]) => fetchTopUpPacks(...a),
    createTopUpCheckout: (...a: unknown[]) => createTopUpCheckout(...a),
    pollPaymentIntent: (...a: unknown[]) => pollPaymentIntent(...a),
    cancelPaymentIntent: (...a: unknown[]) => cancelPaymentIntent(...a),
  }
})

import type { PaymentIntent } from '@/lib/payments'

import { TopUpDialog } from './TopUpDialog'

const ORG = '11111111-1111-1111-1111-111111111111'
const INTENT = '22222222-2222-2222-2222-222222222222'

const PACKS = [
  { skuCode: 'blossom_pack_100', blossomQuantity: 100, priceLkr: 500, currency: 'LKR' },
  { skuCode: 'blossom_pack_500', blossomQuantity: 500, priceLkr: 2000, currency: 'LKR' },
]

const CHECKOUT = {
  paymentIntentId: INTENT,
  provider: 'mock',
  status: 'RequiresAction',
  skuCode: 'blossom_pack_500',
  blossomQuantity: 500,
  amountLkr: 2000,
  currency: 'LKR',
  checkoutUrl: `/api/v1/dev/mock-checkout/${INTENT}`,
  expiresAt: null,
}

function intent(overrides: Partial<PaymentIntent> = {}): PaymentIntent {
  return {
    paymentIntentId: INTENT,
    provider: 'mock',
    providerIntentId: `mock_${INTENT}`,
    purpose: 'BlossomTopUp',
    status: 'RequiresAction',
    amountLkr: 2000,
    currency: 'LKR',
    checkoutUrl: `/api/v1/dev/mock-checkout/${INTENT}`,
    failureCode: null,
    failureMessage: null,
    createdAt: '2026-09-24T00:00:00Z',
    settledAt: null,
    expiresAt: null,
    ...overrides,
  }
}

/** Opens the dialog and chooses the 500-Blossom pack. */
async function openAndChoose() {
  render(<TopUpDialog organizationId={ORG} openCheckout={vi.fn()} />)
  await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))
  await screen.findByRole('dialog')
  await userEvent.click(await screen.findByRole('button', { name: /500 Blossoms/i }))
}

describe('TopUpDialog', () => {
  beforeEach(() => {
    fetchTopUpPacks.mockReset().mockResolvedValue(PACKS)
    createTopUpCheckout.mockReset().mockResolvedValue(CHECKOUT)
    pollPaymentIntent.mockReset().mockResolvedValue(intent({ status: 'Succeeded' }))
    cancelPaymentIntent.mockReset().mockResolvedValue(intent({ status: 'Cancelled' }))
  })

  it('lists the packs the server sells, with the server prices', async () => {
    render(<TopUpDialog organizationId={ORG} openCheckout={vi.fn()} />)

    expect(fetchTopUpPacks).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))
    const dialog = await screen.findByRole('dialog')

    expect(fetchTopUpPacks).toHaveBeenCalledWith(ORG, expect.anything())
    expect(await within(dialog).findByText('100 Blossoms')).toBeTruthy()
    expect(within(dialog).getByText('500 Blossoms')).toBeTruthy()
    // The price is rendered, so a pack cannot be offered without its cost.
    expect(within(dialog).getByText(/LKR\s?500/)).toBeTruthy()
    expect(within(dialog).getByText(/LKR\s?2,?000/)).toBeTruthy()
  })

  it('explains an empty price book rather than offering nothing silently', async () => {
    fetchTopUpPacks.mockResolvedValue([])

    render(<TopUpDialog organizationId={ORG} openCheckout={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))

    expect(
      await screen.findByText(/no top-up packs are configured/i),
    ).toBeTruthy()
  })

  it('posts the checkout exactly once, with the key the operation was opened with', async () => {
    await openAndChoose()

    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))

    await waitFor(() => expect(createTopUpCheckout).toHaveBeenCalledTimes(1))
    const [orgId, skuCode, key] = createTopUpCheckout.mock.calls[0]
    expect(orgId).toBe(ORG)
    expect(skuCode).toBe('blossom_pack_500')
    expect(typeof key).toBe('string')
    expect(key.length).toBeGreaterThan(0)
  })

  it('keeps the same key across a retry, so a retried purchase cannot double-charge', async () => {
    // The first attempt fails at the provider boundary. The operation is unchanged, so the retry
    // must carry the same key: a new key would be a second charge to the server.
    createTopUpCheckout
      .mockRejectedValueOnce(new ApiError(503, 'Unavailable', 'payment-provider-unavailable'))
      .mockResolvedValueOnce(CHECKOUT)

    await openAndChoose()

    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))
    expect(await screen.findByText(/nothing was charged/i)).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))
    await waitFor(() => expect(createTopUpCheckout).toHaveBeenCalledTimes(2))

    expect(createTopUpCheckout.mock.calls[0][2]).toBe(createTopUpCheckout.mock.calls[1][2])
  })

  it('mints a new key when the chosen pack changes, because that is a different purchase', async () => {
    createTopUpCheckout.mockRejectedValue(
      new ApiError(503, 'Unavailable', 'payment-provider-unavailable'),
    )

    render(<TopUpDialog organizationId={ORG} openCheckout={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))
    await screen.findByRole('dialog')

    await userEvent.click(await screen.findByRole('button', { name: /500 Blossoms/i }))
    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))
    expect(await screen.findByText(/nothing was charged/i)).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: /100 Blossoms/i }))
    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))
    await waitFor(() => expect(createTopUpCheckout).toHaveBeenCalledTimes(2))

    expect(createTopUpCheckout.mock.calls[0][2]).not.toBe(createTopUpCheckout.mock.calls[1][2])
  })

  it('hands the customer to the provider and then reports the terminal state the server returned', async () => {
    // The redirect happened (checkoutUrl was opened) but the provider declined. The dialog must
    // render the polled result, not the fact that a checkout page was opened.
    const openCheckout = vi.fn()
    pollPaymentIntent.mockResolvedValue(
      intent({
        status: 'Failed',
        failureCode: 'card_declined',
        failureMessage: 'insufficient_funds',
      }),
    )

    render(<TopUpDialog organizationId={ORG} openCheckout={openCheckout} />)
    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))
    await screen.findByRole('dialog')
    await userEvent.click(await screen.findByRole('button', { name: /500 Blossoms/i }))
    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))

    expect(await screen.findByText('Payment failed')).toBeTruthy()
    expect(screen.getByText(/insufficient_funds/)).toBeTruthy()
    // A closed tab or a completed redirect is not settlement.
    expect(screen.queryByText('Top-up complete')).toBeNull()
    expect(openCheckout).toHaveBeenCalledWith(
      `http://localhost:3000/api/v1/dev/mock-checkout/${INTENT}`,
    )
    expect(pollPaymentIntent).toHaveBeenCalledWith(ORG, INTENT, expect.anything())
  })

  it('renders a settled top-up only from the polled server response', async () => {
    const onSettled = vi.fn()
    pollPaymentIntent.mockResolvedValue(
      intent({ status: 'Succeeded', settledAt: '2026-09-24T00:05:00Z' }),
    )

    render(
      <TopUpDialog organizationId={ORG} openCheckout={vi.fn()} onSettled={onSettled} />,
    )
    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))
    await screen.findByRole('dialog')
    await userEvent.click(await screen.findByRole('button', { name: /500 Blossoms/i }))
    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))

    expect(await screen.findByText('Top-up complete')).toBeTruthy()
    await waitFor(() => expect(onSettled).toHaveBeenCalledTimes(1))
  })

  it('keeps waiting, and never claims success, when the poll has not reached a terminal state', async () => {
    // The deadline passed with the intent still unsettled. "Waiting" is the only true statement.
    pollPaymentIntent.mockResolvedValue(intent({ status: 'RequiresAction' }))

    render(<TopUpDialog organizationId={ORG} openCheckout={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))
    await screen.findByRole('dialog')
    await userEvent.click(await screen.findByRole('button', { name: /500 Blossoms/i }))
    await userEvent.click(screen.getByRole('button', { name: /continue to payment/i }))

    expect(await screen.findByText(/waiting for the provider/i)).toBeTruthy()
    expect(screen.queryByText('Top-up complete')).toBeNull()
    // The customer is told the redirect is not the result.
    expect(pollPaymentIntent).toHaveBeenCalled()
  })

  it('surfaces a catalogue read failure with a retry instead of an empty list', async () => {
    fetchTopUpPacks.mockRejectedValue(new ApiError(403, 'Forbidden'))

    render(<TopUpDialog organizationId={ORG} openCheckout={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: /top up blossoms/i }))

    expect(await screen.findByText(/could not load the top-up packs/i)).toBeTruthy()
    expect(screen.queryByText(/no top-up packs are configured/i)).toBeNull()
  })
})
