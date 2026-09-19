import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { OperationSnapshot } from '@/lib/admin/idempotency'
import { IdempotentActionButton } from './IdempotentActionButton'

const SNAPSHOT: OperationSnapshot = {
  verb: 'credit',
  organizationId: 'org-1',
  amount: 25,
  reason: 'goodwill',
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

/**
 * The acceptance test the plan names: *"a double-click, or a `5xx` followed by a retry, produces
 * exactly one ledger entry"*, asserted on the `Idempotency-Key` headers the mocked client
 * received.
 */
describe('IdempotentActionButton', () => {
  it('issues exactly one request for a double-click', async () => {
    const gate = deferred<{ replayed: boolean }>()
    const execute = vi.fn(() => gate.promise)

    render(<IdempotentActionButton snapshot={SNAPSHOT} execute={execute} label="Credit" />)
    const button = screen.getByRole('button', { name: /credit/i })

    // Two clicks before the first request settles: the ref guard must absorb the second.
    await userEvent.click(button)
    await userEvent.click(button)

    expect(execute).toHaveBeenCalledTimes(1)
    gate.resolve({ replayed: false })
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /credit/i })).toBeEnabled()
    })
  })

  it('reuses the key when a 5xx is retried, so the server sees one operation', async () => {
    const execute = vi
      .fn()
      .mockRejectedValueOnce(Object.assign(new Error('boom'), { status: 500 }))
      .mockResolvedValueOnce({ replayed: false })

    render(<IdempotentActionButton snapshot={SNAPSHOT} execute={execute} label="Credit" />)
    const button = screen.getByRole('button', { name: /credit/i })

    await userEvent.click(button)
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(/safe/i)
    })
    await userEvent.click(button)

    await waitFor(() => {
      expect(execute).toHaveBeenCalledTimes(2)
    })
    expect(execute.mock.calls[0][0].key).toBe(execute.mock.calls[1][0].key)
  })

  it('never auto-retries a 503 idempotency-unavailable', async () => {
    const execute = vi
      .fn()
      .mockRejectedValue(
        Object.assign(new Error('unavailable'), {
          status: 503,
          code: 'idempotency-unavailable',
        }),
      )

    render(<IdempotentActionButton snapshot={SNAPSHOT} execute={execute} label="Credit" />)
    await userEvent.click(screen.getByRole('button', { name: /credit/i }))

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(/not applied/i)
    })
    // Give any stray timer or microtask a chance to fire a second call.
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(execute).toHaveBeenCalledTimes(1)
  })

  it('disables the button while a key-in-flight conflict is unresolved', async () => {
    const execute = vi.fn().mockRejectedValue(
      Object.assign(new Error('in flight'), {
        status: 409,
        code: 'idempotency-key-in-flight',
      }),
    )

    render(<IdempotentActionButton snapshot={SNAPSHOT} execute={execute} label="Credit" />)
    await userEvent.click(screen.getByRole('button', { name: /credit/i }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /credit/i })).toBeDisabled()
    })
    expect(execute).toHaveBeenCalledTimes(1)
  })

  it('surfaces a replayed response rather than pretending it applied', async () => {
    const execute = vi.fn().mockResolvedValue({ replayed: true })

    render(<IdempotentActionButton snapshot={SNAPSHOT} execute={execute} label="Credit" />)
    await userEvent.click(screen.getByRole('button', { name: /credit/i }))

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(/already applied/i)
    })
  })
})
