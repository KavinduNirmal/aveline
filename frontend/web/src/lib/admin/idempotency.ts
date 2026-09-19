/**
 * The `IdempotentActionButton` key lifecycle (plan §5.1).
 *
 * The delivered button called `crypto.randomUUID()` **inside the submit handler**, so a retry
 * after a failure minted a new key and the server saw two distinct operations. The fix is not
 * "hoist the call": it is to define what a *logical operation* is and when the key changes.
 *
 * This module is pure so the whole lifecycle table is unit-testable.
 */

export type BlossomVerb = 'credit' | 'debit' | 'revoke'

export interface OperationSnapshot {
  verb: BlossomVerb
  organizationId: string
  reason: string
  amount?: number
  allowNegative?: boolean
  ledgerEntryId?: string
}

export interface IdempotencyState {
  key: string
  /** The payload this key belongs to. A different payload deserves a different key. */
  fingerprint: string
}

export type Outcome =
  | 'success'
  | 'replayed'
  | 'network'
  | 'timeout'
  | 'server'
  | 'validation'
  | 'key-reuse'
  | 'key-in-flight'
  | 'unavailable'

export interface KeyDecision {
  key: string | null
  /** True only where a retry is safe without asking the user. Currently never. */
  autoRetry: boolean
  disableSubmit: boolean
  message: string
}

/** A stable identity for the operation the user is trying to perform. */
export function operationFingerprint(snapshot: OperationSnapshot): string {
  return JSON.stringify({
    verb: snapshot.verb,
    organizationId: snapshot.organizationId,
    reason: snapshot.reason,
    amount: snapshot.amount ?? null,
    allowNegative: snapshot.allowNegative ?? null,
    ledgerEntryId: snapshot.ledgerEntryId ?? null,
  })
}

/**
 * Called when the form opens, and again whenever a field changes. The same payload keeps its key;
 * a changed payload mints a new one.
 */
export function beginOperation({
  snapshot,
  previous,
  mintKey,
}: {
  snapshot: OperationSnapshot
  previous: IdempotencyState | null
  mintKey: () => string
}): IdempotencyState {
  const fingerprint = operationFingerprint(snapshot)
  if (previous !== null && previous.fingerprint === fingerprint) return previous
  return { key: mintKey(), fingerprint }
}

export function afterOutcome(
  state: IdempotencyState,
  outcome: Outcome,
  mintKey: () => string = () => crypto.randomUUID(),
): KeyDecision {
  switch (outcome) {
    case 'success':
      // The operation is complete; a subsequent unrelated submit mints a fresh key.
      return { key: null, autoRetry: false, disableSubmit: false, message: '' }
    case 'replayed':
      return {
        key: null,
        autoRetry: false,
        disableSubmit: false,
        message: 'This operation was already applied.',
      }
    case 'network':
    case 'timeout':
    case 'server':
      // The operation may have applied. This is the case idempotency exists for.
      return {
        key: state.key,
        autoRetry: false,
        disableSubmit: false,
        message: 'The request did not complete. Retrying is safe and will not double-apply.',
      }
    case 'validation':
      return {
        key: state.key,
        autoRetry: false,
        disableSubmit: false,
        message: 'The request was rejected; correct the field and retry.',
      }
    case 'key-reuse':
      // The key was already used for a *different* payload: this is a different operation.
      return {
        key: mintKey(),
        autoRetry: false,
        disableSubmit: false,
        message: 'This key was already used for a different operation; a new one has been issued.',
      }
    case 'key-in-flight':
      // The same operation is still executing. Re-poll the same key; never issue a second request.
      return {
        key: state.key,
        autoRetry: false,
        disableSubmit: true,
        message: 'This operation is still running. Waiting for it to finish.',
      }
    case 'unavailable':
      // The backend fails closed and nothing was applied. Never auto-retry: that hides an outage.
      return {
        key: state.key,
        autoRetry: false,
        disableSubmit: false,
        message: 'Not applied — safe to retry when the service recovers.',
      }
    default:
      return { key: state.key, autoRetry: false, disableSubmit: false, message: '' }
  }
}

/** Maps an HTTP status (and the idempotency error code) to an outcome. */
export function outcomeForResponse(status: number, code?: string): Outcome {
  if (status === 201 || status === 200) return 'success'
  if (status === 400) return 'validation'
  if (status === 409 && code === 'idempotency-key-in-flight') return 'key-in-flight'
  if (status === 409) return 'key-reuse'
  if (status === 503) return 'unavailable'
  if (status >= 500) return 'server'
  return 'validation'
}
