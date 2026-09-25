import { useEffect, useRef, useState, type ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { afterOutcome, beginOperation, outcomeForResponse, type IdempotencyState } from '@/lib/admin/idempotency'
import { formatCount, formatMoney } from '@/lib/format-money'
import {
  absoluteCheckoutUrl,
  cancelPaymentIntent,
  createTopUpCheckout,
  describePaymentError,
  fetchTopUpPacks,
  isTerminalStatus,
  paymentOutcome,
  pollPaymentIntent,
  type PaymentIntent,
  type TopUpCheckout,
  type TopUpPack,
} from '@/lib/payments'

/** The verb half of the key lifecycle is shared with the admin console (`lib/admin/idempotency.ts`). */
type TopUpOperation = IdempotencyState

interface TopUpDialogProps {
  organizationId: string
  /** The control that opens the dialog. Defaults to the dashboard's own button. */
  trigger?: ReactNode
  /** Called once the server reports a settled intent, so the caller can refetch the balance. */
  onSettled?: (intent: PaymentIntent) => void
  /**
   * Opens the provider's hosted page. Injectable so a test can assert the handoff without a window,
   * and so a native shell can route it differently.
   */
  openCheckout?: (url: string) => void
  /** How often the poll reads the server. Exposed for tests; the shipped value is deliberate. */
  pollIntervalMs?: number
}

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `topup-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

function defaultOpenCheckout(url: string): void {
  if (typeof window !== 'undefined') {
    window.open(url, '_blank', 'noopener,noreferrer')
  }
}

function statusOf(error: unknown): number {
  return typeof error === 'object' && error !== null && 'status' in error
    ? ((error as { status?: number }).status ?? 0)
    : 0
}

function codeOf(error: unknown): string | undefined {
  return typeof error === 'object' && error !== null && 'code' in error
    ? ((error as { code?: string }).code ?? undefined)
    : undefined
}

/**
 * The tenant top-up dialog (plan §9.2).
 *
 * The packs come from the server's own price book, so the dialog cannot offer a SKU the checkout
 * would reject. The flow is: choose a pack, POST the checkout with the operation's
 * `Idempotency-Key`, hand the customer to the provider's page, then **poll** the intent.
 *
 * The redirect is not proof of settlement. A customer can close the hosted page and a card can
 * still decline after the page reports success, so the terminal state rendered here is always the
 * one `GET …/payment-intents/{id}` returned. While that read is still non-terminal, the dialog says
 * it is waiting and claims nothing.
 */
export function TopUpDialog({
  organizationId,
  trigger,
  onSettled,
  openCheckout = defaultOpenCheckout,
  pollIntervalMs = 2_500,
}: TopUpDialogProps) {
  const [open, setOpen] = useState(false)
  const [packs, setPacks] = useState<TopUpPack[] | null>(null)
  const [packsError, setPacksError] = useState<string | null>(null)
  const [packsReload, setPacksReload] = useState(0)
  const [skuCode, setSkuCode] = useState('')
  const [checkout, setCheckout] = useState<TopUpCheckout | null>(null)
  const [intent, setIntent] = useState<PaymentIntent | null>(null)
  const [operation, setOperation] = useState<TopUpOperation | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // One controller per dialog lifetime: closing the dialog or unmounting it stops the poll rather
  // than leaving a request loop running behind a dismissed modal.
  const pollAbort = useRef<AbortController | null>(null)
  const reloadToken = useRef(0)

  const selected = packs?.find((pack) => pack.skuCode === skuCode) ?? null
  const outcome = intent ? paymentOutcome(intent) : null
  const hasResult = intent !== null && isTerminalStatus(intent.status)
  // `null` means "not read yet", so loading is derived rather than set inside the effect: a
  // synchronous setState in an effect is a cascading render the repository's lint rule flags.
  const loadingPacks = open && packs === null && packsError === null

  useEffect(() => {
    if (!open) {
      return
    }

    const controller = new AbortController()
    const token = ++reloadToken.current

    fetchTopUpPacks(organizationId, controller.signal)
      .then((loaded) => {
        if (token === reloadToken.current) {
          setPacks(loaded)
        }
      })
      .catch(() => {
        if (token === reloadToken.current) {
          setPacksError('Could not load the top-up packs. Try again in a moment.')
        }
      })

    return () => controller.abort()
  }, [open, organizationId, packsReload])

  useEffect(() => () => pollAbort.current?.abort(), [])

  const choosePack = (pack: TopUpPack) => {
    setSkuCode(pack.skuCode)
    setError(null)
    // The key belongs to the operation (same pack -> same key). Changing the pack is a different
    // purchase, so it becomes a different key.
    setOperation((previous) =>
      beginOperation({
        snapshot: {
          verb: 'top-up-checkout',
          organizationId,
          reason: pack.skuCode,
          amount: pack.priceLkr,
        },
        previous,
        mintKey: newIdempotencyKey,
      }),
    )
  }

  const pollIntent = async (paymentIntentId: string) => {
    pollAbort.current?.abort()
    const controller = new AbortController()
    pollAbort.current = controller

    try {
      const result = await pollPaymentIntent(organizationId, paymentIntentId, {
        intervalMs: pollIntervalMs,
        signal: controller.signal,
        onIntent: setIntent,
      })
      setIntent(result)
      if (result.status === 'Succeeded') {
        onSettled?.(result)
      }
    } catch (pollError) {
      if ((pollError as { name?: string } | null)?.name === 'AbortError') {
        return
      }
      setError(describePaymentError(pollError))
    }
  }

  const submit = async () => {
    if (!selected || !operation) {
      setError('Choose a pack first.')
      return
    }

    setIsSubmitting(true)
    setError(null)
    try {
      const handoff = await createTopUpCheckout(organizationId, selected.skuCode, operation.key)
      // The attempt succeeded, so its key is consumed: a later, separate purchase mints a new one.
      setOperation(null)
      setCheckout(handoff)
      setIntent(null)

      if (handoff.checkoutUrl) {
        openCheckout(absoluteCheckoutUrl(handoff.checkoutUrl))
      }

      await pollIntent(handoff.paymentIntentId)
    } catch (submitError) {
      // The operation may have applied, so the key is kept for exactly the outcomes where a retry
      // is safe, and rotated where the server said the key belonged to a different operation.
      const decision = afterOutcome(
        operation,
        outcomeForResponse(statusOf(submitError), codeOf(submitError)),
        newIdempotencyKey,
      )
      setOperation(
        decision.key === null ? null : { key: decision.key, fingerprint: operation.fingerprint },
      )
      setError(describePaymentError(submitError))
    } finally {
      setIsSubmitting(false)
    }
  }

  const cancel = async () => {
    if (!checkout) {
      return
    }

    setError(null)
    const state = beginOperation({
      snapshot: {
        verb: 'top-up-cancel',
        organizationId,
        reason: checkout.paymentIntentId,
      },
      previous: null,
      mintKey: newIdempotencyKey,
    })

    try {
      const cancelled = await cancelPaymentIntent(
        organizationId,
        checkout.paymentIntentId,
        state.key,
        'Customer abandoned the checkout.',
      )
      setIntent(cancelled)
    } catch (cancelError) {
      setError(describePaymentError(cancelError))
    }
  }

  const reset = () => {
    pollAbort.current?.abort()
    setCheckout(null)
    setIntent(null)
    setOperation(null)
    setSkuCode('')
    setError(null)
  }

  const waiting = intent !== null && !hasResult

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) {
          reset()
        }
      }}
    >
      <DialogTrigger asChild>
        {trigger ?? (
          <Button type="button" className="gap-1.5 rounded-full">
            Top up Blossoms
          </Button>
        )}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Top up Blossoms</DialogTitle>
          <DialogDescription>
            Choose a pack and pay through the provider&apos;s secure checkout. Your balance changes
            only when the provider confirms the payment back to Aveline.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          {loadingPacks ? (
            <p className="text-sm text-muted-foreground">Reading the top-up packs…</p>
          ) : packsError ? (
            <div className="flex flex-col gap-2">
              <p className="text-sm text-destructive">{packsError}</p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => {
                  setPacksError(null)
                  setPacksReload((value) => value + 1)
                }}
              >
                Try again
              </Button>
            </div>
          ) : packs === null || packs.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No top-up packs are configured in the price book, so nothing can be purchased.
            </p>
          ) : (
            <div className="flex flex-col gap-2" role="group" aria-label="Top-up packs">
              {packs.map((pack) => (
                <Button
                  key={pack.skuCode}
                  type="button"
                  variant={pack.skuCode === skuCode ? 'default' : 'outline'}
                  aria-pressed={pack.skuCode === skuCode}
                  onClick={() => choosePack(pack)}
                  className="h-auto w-full justify-between px-3 py-2 text-left text-sm"
                >
                  <span className="font-medium">
                    {formatCount(pack.blossomQuantity)} Blossoms
                  </span>
                  <span className="opacity-80">
                    {formatMoney(pack.priceLkr, pack.currency)}
                  </span>
                </Button>
              ))}
            </div>
          )}

          {outcome ? (
            <div
              role="status"
              className={`rounded-lg border px-3 py-2 ${
                outcome.tone === 'success'
                  ? 'border-primary/30 bg-primary/5'
                  : outcome.tone === 'error'
                    ? 'border-destructive/30 bg-destructive/5'
                    : 'border-border bg-muted/40'
              }`}
            >
              <p className="text-sm font-semibold">{outcome.title}</p>
              <p className="mt-1 text-sm text-muted-foreground">{outcome.detail}</p>
            </div>
          ) : null}

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          {waiting ? (
            <>
              <Button type="button" variant="outline" onClick={() => void cancel()}>
                Cancel this payment
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => checkout && void pollIntent(checkout.paymentIntentId)}
              >
                Check for the result
              </Button>
            </>
          ) : hasResult ? (
            <Button type="button" variant="outline" onClick={reset}>
              Buy another pack
            </Button>
          ) : (
            <Button type="button" disabled={isSubmitting || !selected} onClick={() => void submit()}>
              {isSubmitting ? 'Opening the checkout…' : 'Continue to payment'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
