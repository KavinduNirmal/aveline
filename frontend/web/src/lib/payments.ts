/**
 * The tenant purchase path (plan §9.2, decision D8).
 *
 * This module is the one place the client speaks to the payment-intent routes. Three rules are
 * encoded here rather than left to a component:
 *
 * 1. **The price and the Blossom quantity are the server's.** A checkout sends `{ skuCode }` and
 *    nothing else: a caller cannot name the amount it is charged.
 * 2. **A redirect is not settlement.** `checkoutUrl` is where the customer is sent; the only
 *    terminal state comes from `GET …/payment-intents/{id}`, which is why `pollPaymentIntent`
 *    exists and why every piece of outcome copy is derived from a `PaymentIntent` response.
 * 3. **Only a write carries an `Idempotency-Key`, and it is the caller's key.** The lifecycle that
 *    decides when a key changes lives in `lib/admin/idempotency.ts`; this module only puts the
 *    header on the wire.
 */

import { apiClient } from '@/lib/api'
import { ApiError } from '@/lib/api-error'

/** A purchasable top-up pack (`GET …/blossoms/top-up-packs`, E-11). */
export interface TopUpPack {
  skuCode: string
  blossomQuantity: number
  priceLkr: number
  currency: string
}

/**
 * The handoff a checkout returns. `status` is the provider's own state at creation time — usually
 * `RequiresAction` — and deliberately is **not** treated as a result.
 */
export interface TopUpCheckout {
  paymentIntentId: string
  provider: string
  status: string
  skuCode: string
  blossomQuantity: number
  amountLkr: number
  currency: string
  checkoutUrl: string | null
  expiresAt: string | null
}

/** The client's poll of one intent: the server's terminal-state read. */
export interface PaymentIntent {
  paymentIntentId: string
  provider: string
  providerIntentId: string
  purpose: string
  status: string
  amountLkr: number
  currency: string
  checkoutUrl: string | null
  failureCode: string | null
  failureMessage: string | null
  createdAt: string
  settledAt: string | null
  expiresAt: string | null
}

/**
 * The states that end a poll. `RequiresAction` and `Processing` are not among them: the first is a
 * hosted page the customer may still close, and the second is a charge the provider is still
 * taking.
 */
export const TERMINAL_PAYMENT_STATUSES = [
  'Succeeded',
  'Failed',
  'Cancelled',
  'Expired',
  'Refunded',
] as const

export type TerminalPaymentStatus = (typeof TERMINAL_PAYMENT_STATUSES)[number]

/**
 * True only for a state the server can stop asking about.
 *
 * An unrecognised status is **not** terminal. A status this client has never heard of is a reason
 * to keep reading the server, not a reason to assume the charge succeeded.
 */
export function isTerminalStatus(status: string): boolean {
  return (TERMINAL_PAYMENT_STATUSES as readonly string[]).includes(status)
}

export interface PaymentOutcome {
  tone: 'success' | 'error' | 'info'
  title: string
  detail: string
}

const amountFormatter = new Intl.NumberFormat('en-LK', { maximumFractionDigits: 2 })

/**
 * The copy for an intent's **server-reported** state. Nothing here reads a redirect, a query
 * parameter, or the fact that a checkout window was opened: settlement is only ever what the poll
 * returned.
 */
export function paymentOutcome(intent: PaymentIntent): PaymentOutcome {
  const amount = `${intent.currency} ${amountFormatter.format(intent.amountLkr)}`

  switch (intent.status) {
    case 'Succeeded':
      return {
        tone: 'success',
        title: 'Top-up complete',
        detail: `Payment of ${amount} confirmed by ${intent.provider}. Your Blossom balance has been updated.`,
      }
    case 'Failed':
      return {
        tone: 'error',
        title: 'Payment failed',
        detail: intent.failureMessage
          ? `The provider declined the charge (${intent.failureMessage}). Nothing was granted.`
          : 'The provider did not complete the charge. Nothing was granted.',
      }
    case 'Cancelled':
      return {
        tone: 'info',
        title: 'Payment cancelled',
        detail: 'The checkout was abandoned. No money moved and your balance is unchanged.',
      }
    case 'Expired':
      return {
        tone: 'info',
        title: 'Checkout expired',
        detail: 'The checkout window closed before payment completed. Nothing was charged.',
      }
    case 'Refunded':
      return {
        tone: 'info',
        title: 'Payment refunded',
        detail: 'The charge was refunded, so the granted Blossoms were reversed.',
      }
    default:
      return {
        tone: 'info',
        title: 'Waiting for the provider…',
        detail:
          'Finish the payment in the checkout window. This page confirms only when the provider '
          + 'reports the result back to Aveline.',
      }
  }
}

/**
 * The URL to open for the customer. The mock provider hands back a same-origin path
 * (`/api/v1/dev/mock-checkout/…`); a real provider hands back an absolute one. Both have to be
 * openable from the dashboard without the dashboard reinterpreting either.
 */
export function absoluteCheckoutUrl(url: string, origin?: string): string {
  if (/^https?:\/\//i.test(url)) {
    return url
  }

  const base = origin ?? (typeof window !== 'undefined' ? window.location.origin : '')
  if (!base) {
    return url
  }

  return new URL(url, base).toString()
}

/**
 * The API's own error code.
 *
 * The shared client puts axios's transport code (`ERR_BAD_REQUEST`) on `ApiError.code` and the API's
 * business code (`payment-intent-state`, `unknown-sku`, …) in the response body, which the client
 * carries as `ApiError.details`. Read the body first, and fall back to `code` so a directly-built
 * `ApiError` still works. This mirrors `lib/privacy.ts`, which solved the same problem locally rather
 * than changing the shared normaliser every other caller depends on.
 */
function businessCode(error: ApiError): string | undefined {
  const details = error.details
  if (typeof details === 'object' && details !== null) {
    const code = (details as Record<string, unknown>).code
    if (typeof code === 'string' && code !== '') {
      return code
    }
  }
  return error.code
}

/**
 * What to tell the customer when a checkout or a cancel was refused. The server's own reason wins
 * when it has one; the known refusal codes get a sentence that says whether money moved, because
 * "try again" is the wrong advice for a request that may already have applied.
 */
export function describePaymentError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (businessCode(error)) {
      case 'idempotency-key-in-flight':
        return 'This request is still running. Waiting for it to finish — do not submit again.'
      case 'idempotency-key-reuse':
        return 'This attempt was already used for a different request. A new attempt has been prepared.'
      case 'unknown-sku':
        return 'That pack is no longer available. Reload the list and choose another.'
      case 'unpurchasable-sku':
        return 'That pack has no price on file and cannot be purchased.'
      case 'payment-intent-state':
        return 'This checkout can no longer be changed. Nothing further was charged.'
      default:
        break
    }

    // The status is the backstop: a 502/503 from this surface always means the provider boundary
    // did not complete the charge, whether or not the body carried a code.
    if (error.status === 503) {
      return 'The payment provider is temporarily unavailable. Nothing was charged.'
    }
    if (error.status === 502) {
      return 'The payment provider could not be reached. Nothing was charged.'
    }

    return error.message
  }

  return 'The request did not complete. Nothing was charged; retrying is safe.'
}

const orgBase = (organizationId: string) => `/api/v1/orgs/${organizationId}`

/** The purchasable packs, priced by the server's own price book. Requires `billing:manage`. */
export async function fetchTopUpPacks(
  organizationId: string,
  signal?: AbortSignal,
): Promise<TopUpPack[]> {
  const response = await apiClient.get<TopUpPack[]>(
    `${orgBase(organizationId)}/blossoms/top-up-packs`,
    { signal },
  )
  return response.data
}

/**
 * Creates the intent and returns the provider handoff. The `Idempotency-Key` is required by the
 * server; the key's lifecycle belongs to the caller (`lib/admin/idempotency.ts`), so the same
 * logical attempt always sends the same key.
 */
export async function createTopUpCheckout(
  organizationId: string,
  skuCode: string,
  idempotencyKey: string,
  signal?: AbortSignal,
): Promise<TopUpCheckout> {
  const response = await apiClient.post<TopUpCheckout>(
    `${orgBase(organizationId)}/blossoms/top-ups/checkout`,
    { skuCode },
    { headers: { 'Idempotency-Key': idempotencyKey }, signal },
  )
  return response.data
}

/** One read of an intent. The client polls this until `isTerminalStatus` is true. */
export async function fetchPaymentIntent(
  organizationId: string,
  paymentIntentId: string,
  signal?: AbortSignal,
): Promise<PaymentIntent> {
  const response = await apiClient.get<PaymentIntent>(
    `${orgBase(organizationId)}/payment-intents/${paymentIntentId}`,
    { signal },
  )
  return response.data
}

/**
 * Abandons an unsettled intent. `reason` is a query value: the route binds it from the query
 * string, and the request has no body. Requires its own `Idempotency-Key` — reusing the checkout's
 * key would be a different operation on the same key, which the server refuses.
 */
export async function cancelPaymentIntent(
  organizationId: string,
  paymentIntentId: string,
  idempotencyKey: string,
  reason?: string,
  signal?: AbortSignal,
): Promise<PaymentIntent> {
  const response = await apiClient.post<PaymentIntent>(
    `${orgBase(organizationId)}/payment-intents/${paymentIntentId}/cancel`,
    undefined,
    { headers: { 'Idempotency-Key': idempotencyKey }, params: { reason }, signal },
  )
  return response.data
}

export interface PollPaymentIntentOptions {
  /** Delay between reads. The dialog uses a couple of seconds; tests use zero. */
  intervalMs?: number
  /** How long to keep asking. The caller must stop: an intent a customer abandoned is common. */
  timeoutMs?: number
  signal?: AbortSignal
  /** Called with every server state read, in order, so a caller can render progress. */
  onIntent?: (intent: PaymentIntent) => void
}

function abortError(): Error {
  const error = new Error('The payment poll was cancelled.')
  error.name = 'AbortError'
  return error
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(abortError())
      return
    }

    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)

    function onAbort() {
      clearTimeout(timer)
      reject(abortError())
    }

    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/**
 * Reads the intent until the server reports a terminal state.
 *
 * Returns the **last server state**, whatever it is. When the deadline passes with the intent still
 * unsettled the caller gets that unsettled intent back, because "still waiting" is a true statement
 * about the server and "succeeded" would be an invention.
 */
export async function pollPaymentIntent(
  organizationId: string,
  paymentIntentId: string,
  options: PollPaymentIntentOptions = {},
): Promise<PaymentIntent> {
  const intervalMs = options.intervalMs ?? 2_000
  const deadline = Date.now() + (options.timeoutMs ?? 3 * 60_000)
  const signal = options.signal

  if (signal?.aborted) {
    throw abortError()
  }

  // The first read is immediate: the customer may already have completed the charge, and a poll
  // that waited one interval to find that out would be a second of the UI claiming to wait for
  // something that already happened.
  for (;;) {
    const intent = await fetchPaymentIntent(organizationId, paymentIntentId, signal)
    options.onIntent?.(intent)

    if (isTerminalStatus(intent.status)) {
      return intent
    }

    if (Date.now() >= deadline) {
      return intent
    }

    await sleep(intervalMs, signal)
  }
}
