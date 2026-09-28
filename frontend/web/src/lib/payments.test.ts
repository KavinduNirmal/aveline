import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from './api-error'

const getMock = vi.fn()
const postMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
  },
}))

import {
  absoluteCheckoutUrl,
  cancelPaymentIntent,
  createTopUpCheckout,
  describePaymentError,
  fetchPaymentIntent,
  fetchTopUpPacks,
  isTerminalStatus,
  paymentOutcome,
  pollPaymentIntent,
  TERMINAL_PAYMENT_STATUSES,
  type PaymentIntent,
} from './payments'

const ORG = '11111111-1111-1111-1111-111111111111'
const INTENT = '22222222-2222-2222-2222-222222222222'

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

// Top-level so every describe gets a clean call history: an assertion about "how many requests did
// this test make" must not be able to see a previous test's requests.
beforeEach(() => {
  getMock.mockReset().mockResolvedValue({ data: {} })
  postMock.mockReset().mockResolvedValue({ data: {} })
})

afterEach(() => vi.clearAllMocks())

describe('payments-api', () => {
  it('reads the purchasable packs from the same route the catalogue uses', async () => {
    await fetchTopUpPacks(ORG)

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/blossoms/top-up-packs`)
    // A read carries no idempotency key: only a write does.
    expect(config.headers).toBeUndefined()
  })

  it('posts the checkout with only the SKU body the server accepts, and the Idempotency-Key header', async () => {
    postMock.mockResolvedValue({ data: CHECKOUT })

    const handoff = await createTopUpCheckout(ORG, 'blossom_pack_500', 'key-123')

    const [path, body, config] = postMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/blossoms/top-ups/checkout`)
    // The client never names the price or the quantity: the price book is the server's.
    expect(body).toEqual({ skuCode: 'blossom_pack_500' })
    expect(config.headers['Idempotency-Key']).toBe('key-123')
    expect(handoff).toEqual(CHECKOUT)
  })

  it('polls one intent by its own route', async () => {
    await fetchPaymentIntent(ORG, INTENT)

    expect(getMock.mock.calls[0][0]).toBe(
      `/api/v1/orgs/${ORG}/payment-intents/${INTENT}`,
    )
  })

  it('cancels through the intent route with the reason as a query value and a fresh key', async () => {
    await cancelPaymentIntent(ORG, INTENT, 'key-cancel', 'Changed my mind.')

    const [path, body, config] = postMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/payment-intents/${INTENT}/cancel`)
    // The minimal API binds `reason` from the query string; it is not a body field.
    expect(config.params).toEqual({ reason: 'Changed my mind.' })
    expect(config.headers['Idempotency-Key']).toBe('key-cancel')
    expect(body).toBeUndefined()
  })

  it('omits the reason query when none is given', async () => {
    await cancelPaymentIntent(ORG, INTENT, 'key-cancel')

    expect(postMock.mock.calls[0][2].params).toEqual({ reason: undefined })
  })

  it('surfaces the server refusal rather than swallowing it', async () => {
    const refusal = new ApiError(403, "You don't have permission to buy Blossoms.", 'forbidden')
    postMock.mockRejectedValue(refusal)

    await expect(createTopUpCheckout(ORG, 'blossom_pack_500', 'key-1')).rejects.toBe(
      refusal,
    )
  })
})

describe('isTerminalStatus', () => {
  it('names exactly the states that stop the poll', () => {
    expect(TERMINAL_PAYMENT_STATUSES).toEqual([
      'Succeeded',
      'Failed',
      'Cancelled',
      'Expired',
      'Refunded',
    ])
  })

  it('is false while the provider still has work to do', () => {
    // `RequiresAction` is a hosted page, not a result; `Processing` is a charge in flight.
    expect(isTerminalStatus('RequiresAction')).toBe(false)
    expect(isTerminalStatus('Processing')).toBe(false)
    expect(isTerminalStatus('Succeeded')).toBe(true)
    expect(isTerminalStatus('Failed')).toBe(true)
    expect(isTerminalStatus('Expired')).toBe(true)
  })

  it('does not treat an unknown status as terminal', () => {
    // A status this client does not know is "keep asking", never "assume success".
    expect(isTerminalStatus('SomethingNew')).toBe(false)
  })
})

describe('paymentOutcome', () => {
  it('reports a settled intent as a completed top-up', () => {
    const outcome = paymentOutcome(
      intent({ status: 'Succeeded', settledAt: '2026-09-24T00:05:00Z' }),
    )

    expect(outcome.tone).toBe('success')
    expect(outcome.detail).toContain('2,000')
  })

  it('renders a decline from the server, never as a success', () => {
    const outcome = paymentOutcome(
      intent({
        status: 'Failed',
        failureCode: 'card_declined',
        failureMessage: 'insufficient_funds',
      }),
    )

    expect(outcome.tone).toBe('error')
    expect(outcome.detail).toContain('insufficient_funds')
    expect(outcome.title.toLowerCase()).not.toContain('complete')
  })

  it('says nothing was charged for a cancelled or expired checkout', () => {
    for (const status of ['Cancelled', 'Expired']) {
      const outcome = paymentOutcome(intent({ status }))
      expect(outcome.tone).toBe('info')
      expect(outcome.detail).toMatch(/nothing was charged|no money moved/i)
    }
  })

  it('is still waiting while the redirect is the only thing that happened', () => {
    const outcome = paymentOutcome(intent({ status: 'RequiresAction' }))

    expect(outcome.tone).toBe('info')
    expect(outcome.title).toMatch(/waiting/i)
    expect(outcome.detail).toMatch(/checkout/i)
  })
})

describe('absoluteCheckoutUrl', () => {
  it('leaves an absolute provider URL exactly as the provider sent it', () => {
    expect(absoluteCheckoutUrl('https://checkout.stripe.com/c/pay/abc')).toBe(
      'https://checkout.stripe.com/c/pay/abc',
    )
  })

  it('resolves the relative mock checkout page against the current origin', () => {
    expect(absoluteCheckoutUrl('/api/v1/dev/mock-checkout/abc', 'http://localhost:5173')).toBe(
      'http://localhost:5173/api/v1/dev/mock-checkout/abc',
    )
  })
})

describe('describePaymentError', () => {
  // The codes are the server's own (`PaymentDomainExceptions`), not a client vocabulary.
  it('names the provider outage and says nothing was charged', () => {
    const message = describePaymentError(
      new ApiError(503, 'Service unavailable', 'payment-provider-unavailable'),
    )

    expect(message).toMatch(/unavailable/i)
    expect(message).toMatch(/nothing was charged/i)
  })

  it('also reads the outage from the status when the body carries no code', () => {
    expect(describePaymentError(new ApiError(503, 'Service unavailable'))).toMatch(
      /nothing was charged/i,
    )
    expect(describePaymentError(new ApiError(502, 'Bad gateway'))).toMatch(
      /nothing was charged/i,
    )
  })

  it('explains an in-flight idempotency key instead of asking for a retry', () => {
    const message = describePaymentError(
      new ApiError(409, 'Conflict', 'idempotency-key-in-flight'),
    )

    expect(message).toMatch(/still running|waiting/i)
  })

  it('names an unknown SKU as a stale catalogue', () => {
    const message = describePaymentError(new ApiError(400, 'Unknown top-up SKU.', 'unknown-sku'))

    expect(message).toMatch(/no longer available|reload/i)
  })

  it('falls back to the server message, then to a neutral statement', () => {
    expect(describePaymentError(new ApiError(403, 'Forbidden here.'))).toBe('Forbidden here.')
    expect(describePaymentError(new Error('offline'))).toMatch(/did not complete/i)
  })

  // The shared client puts axios's transport code on `ApiError.code` (`ERR_BAD_REQUEST`) and the
  // API's business code in the response body, which the client carries as `ApiError.details` (see
  // `toApiError`). Reading only `code` means none of the business-code copy below ever appears, so
  // these cases pin the body-first read.
  it('reads the business code from the response body when the transport code is on ApiError.code', () => {
    const message = describePaymentError(
      new ApiError(409, 'Conflict', 'ERR_BAD_REQUEST', { code: 'payment-intent-state' }),
    )

    expect(message).toMatch(/no longer be changed/i)
  })

  it('reads every business code from the response body, not just the first', () => {
    const cases: Array<[string, RegExp]> = [
      ['payment-intent-state', /no longer be changed/i],
      ['unknown-sku', /no longer available|reload/i],
      ['unpurchasable-sku', /no price on file/i],
      ['idempotency-key-in-flight', /still running|waiting/i],
      ['idempotency-key-reuse', /already used/i],
    ]

    for (const [code, expected] of cases) {
      const message = describePaymentError(
        new ApiError(409, 'Conflict', 'ERR_BAD_REQUEST', { code }),
      )

      expect(message, code).toMatch(expected)
    }
  })

  it('still reads a business code carried directly on ApiError.code', () => {
    // Private callers and tests build an ApiError with the business code in the third position;
    // the body-first read must keep that working.
    expect(describePaymentError(new ApiError(400, 'Unknown.', 'unknown-sku'))).toMatch(
      /no longer available|reload/i,
    )
  })
})

describe('pollPaymentIntent', () => {
  it('polls until the server reports a terminal state, then stops', async () => {
    getMock
      .mockResolvedValueOnce({ data: intent({ status: 'RequiresAction' }) })
      .mockResolvedValueOnce({ data: intent({ status: 'Processing' }) })
      .mockResolvedValueOnce({
        data: intent({ status: 'Succeeded', settledAt: '2026-09-24T00:05:00Z' }),
      })

    const settled = await pollPaymentIntent(ORG, INTENT, { intervalMs: 0, timeoutMs: 5_000 })

    expect(settled.status).toBe('Succeeded')
    expect(getMock).toHaveBeenCalledTimes(3)
  })

  it('stops at the deadline and returns the last server state rather than inventing one', async () => {
    getMock.mockResolvedValue({ data: intent({ status: 'RequiresAction' }) })

    const last = await pollPaymentIntent(ORG, INTENT, { intervalMs: 0, timeoutMs: 0 })

    expect(last.status).toBe('RequiresAction')
    expect(getMock).toHaveBeenCalledTimes(1)
  })

  it('reports each polled state so the caller can render progress', async () => {
    const seen: string[] = []
    getMock
      .mockResolvedValueOnce({ data: intent({ status: 'RequiresAction' }) })
      .mockResolvedValueOnce({ data: intent({ status: 'Succeeded' }) })

    await pollPaymentIntent(ORG, INTENT, {
      intervalMs: 0,
      timeoutMs: 5_000,
      onIntent: (polled) => seen.push(polled.status),
    })

    expect(seen).toEqual(['RequiresAction', 'Succeeded'])
  })

  it('does not begin polling when the caller has already navigated away', async () => {
    const controller = new AbortController()
    controller.abort()

    await expect(
      pollPaymentIntent(ORG, INTENT, { intervalMs: 0, signal: controller.signal }),
    ).rejects.toThrow()
    expect(getMock).not.toHaveBeenCalled()
  })
})
