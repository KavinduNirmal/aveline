import { describe, expect, it, vi } from 'vitest'

import {
  afterOutcome,
  beginOperation,
  operationFingerprint,
  type OperationSnapshot,
} from './idempotency'

const CREDIT: OperationSnapshot = {
  verb: 'credit',
  organizationId: 'org-1',
  amount: 25,
  reason: 'goodwill',
}

/**
 * The plan's §5.1 key lifecycle, as pure functions.
 *
 * The one rule that makes this testable: the key is a property of **the operation the user is
 * trying to perform**, derived from the payload snapshot, so it does not change across a retry of
 * that same payload. The delivered button minted the key inside the submit handler, so a retry
 * after a `5xx` produced a second, distinct operation.
 */
describe('operationFingerprint', () => {
  it('is stable for the same payload and different for a changed one', () => {
    expect(operationFingerprint(CREDIT)).toBe(operationFingerprint({ ...CREDIT }))
    expect(operationFingerprint(CREDIT)).not.toBe(
      operationFingerprint({ ...CREDIT, amount: 26 }),
    )
    expect(operationFingerprint(CREDIT)).not.toBe(
      operationFingerprint({ ...CREDIT, reason: 'different' }),
    )
  })
})

describe('beginOperation', () => {
  it('mints one key when the operation is new', () => {
    const mintKey = vi.fn(() => 'key-1')
    const state = beginOperation({ snapshot: CREDIT, previous: null, mintKey })
    expect(state).toEqual({ key: 'key-1', fingerprint: operationFingerprint(CREDIT) })
    expect(mintKey).toHaveBeenCalledTimes(1)
  })

  it('reuses the key for the same payload', () => {
    const mintKey = vi.fn(() => 'key-1')
    const first = beginOperation({ snapshot: CREDIT, previous: null, mintKey })
    const second = beginOperation({ snapshot: { ...CREDIT }, previous: first, mintKey })
    expect(second.key).toBe('key-1')
    expect(mintKey).toHaveBeenCalledTimes(1)
  })

  it('mints a new key when a payload field changes', () => {
    const keys = ['key-1', 'key-2']
    const mintKey = vi.fn(() => keys.shift() as string)
    const first = beginOperation({ snapshot: CREDIT, previous: null, mintKey })
    const second = beginOperation({ snapshot: { ...CREDIT, amount: 99 }, previous: first, mintKey })
    expect(second.key).toBe('key-2')
  })
})

describe('afterOutcome', () => {
  const state = { key: 'key-1', fingerprint: operationFingerprint(CREDIT) }

  it('discards the key on success', () => {
    const decision = afterOutcome(state, 'success')
    expect(decision.key).toBeNull()
    expect(decision.autoRetry).toBe(false)
  })

  it('keeps the key across a network error, a timeout and a 5xx, because the operation may have applied', () => {
    for (const outcome of ['network', 'timeout', 'server'] as const) {
      const decision = afterOutcome(state, outcome)
      expect(decision.key, outcome).toBe('key-1')
      expect(decision.autoRetry, outcome).toBe(false)
    }
  })

  it('keeps the key on a 400, because nothing applied', () => {
    expect(afterOutcome(state, 'validation').key).toBe('key-1')
  })

  it('mints a fresh key on 409 idempotency-key-reuse, because the previous operation was a different one', () => {
    const decision = afterOutcome(state, 'key-reuse', () => 'key-2')
    expect(decision.key).toBe('key-2')
  })

  it('keeps the key and issues no second request on 409 idempotency-key-in-flight', () => {
    const decision = afterOutcome(state, 'key-in-flight')
    expect(decision.key).toBe('key-1')
    expect(decision.autoRetry).toBe(false)
    expect(decision.disableSubmit).toBe(true)
  })

  it('keeps the key and never auto-retries on 503 idempotency-unavailable', () => {
    const decision = afterOutcome(state, 'unavailable')
    expect(decision.key).toBe('key-1')
    expect(decision.autoRetry).toBe(false)
    expect(decision.message).toMatch(/not applied|safe to retry/i)
  })

  it('explains a replayed response', () => {
    expect(afterOutcome(state, 'replayed').message).toMatch(/already applied/i)
    expect(afterOutcome(state, 'replayed').key).toBeNull()
  })
})
