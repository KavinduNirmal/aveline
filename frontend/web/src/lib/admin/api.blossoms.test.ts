import { afterEach, describe, expect, it } from 'vitest'

import { apiClient } from '@/lib/api'
import { creditBlossoms, debitBlossoms, revokeBlossoms } from './api'

interface CapturedRequest {
  url: string | undefined
  body: unknown
  headers: Record<string, unknown>
}

/**
 * The three Blossom POSTs are idempotency-guarded server-side
 * (`IdempotencyEndpointFilter.cs:44-52`) and the header is mandatory. They also bind three
 * different request records, so one shared body type cannot be correct for all of them.
 */
function captureRequests(): CapturedRequest[] {
  const captured: CapturedRequest[] = []
  apiClient.defaults.adapter = async (config) => {
    captured.push({
      url: config.url,
      body:
        typeof config.data === 'string' && config.data.length > 0
          ? JSON.parse(config.data)
          : config.data,
      headers: config.headers as unknown as Record<string, unknown>,
    })
    return {
      status: 201,
      statusText: 'Created',
      headers: {},
      config,
      data: { ok: true },
    }
  }
  return captured
}

const originalAdapter = apiClient.defaults.adapter

afterEach(() => {
  apiClient.defaults.adapter = originalAdapter
})

describe('creditBlossoms', () => {
  it('POSTs to the credit route with a mandatory Idempotency-Key', async () => {
    const captured = captureRequests()
    await creditBlossoms(
      'org-1',
      { amount: 25, reason: 'goodwill' },
      'idem-credit-1',
    )

    expect(captured).toHaveLength(1)
    expect(captured[0].url).toBe('/api/v1/admin/orgs/org-1/blossoms/credit')
    expect(captured[0].body).toEqual({ amount: 25, reason: 'goodwill' })
    expect(captured[0].headers['Idempotency-Key']).toBe('idem-credit-1')
  })
})

describe('debitBlossoms', () => {
  it('POSTs to the debit route and always carries allowNegative', async () => {
    const captured = captureRequests()
    await debitBlossoms(
      'org-1',
      { amount: 5, reason: 'correction', allowNegative: false },
      'idem-debit-1',
    )

    expect(captured[0].url).toBe('/api/v1/admin/orgs/org-1/blossoms/debit')
    expect(captured[0].body).toEqual({
      amount: 5,
      reason: 'correction',
      allowNegative: false,
    })
    expect(captured[0].headers['Idempotency-Key']).toBe('idem-debit-1')
  })
})

describe('revokeBlossoms', () => {
  it('sends the ledger entry id the backend binds, not an amount', async () => {
    const captured = captureRequests()
    await revokeBlossoms(
      'org-1',
      { ledgerEntryId: '11111111-1111-7111-8111-111111111111', reason: 'duplicate' },
      'idem-revoke-1',
    )

    expect(captured[0].url).toBe('/api/v1/admin/orgs/org-1/blossoms/revoke')
    expect(captured[0].body).toEqual({
      ledgerEntryId: '11111111-1111-7111-8111-111111111111',
      reason: 'duplicate',
    })
    // The delivered client sent `{ amount, reason, allowNegative }`, which the backend
    // cannot bind (`RevokeBlossomsRequest(Guid LedgerEntryId, string Reason)`).
    expect(captured[0].body).not.toHaveProperty('amount')
    expect(captured[0].body).not.toHaveProperty('allowNegative')
    expect(captured[0].headers['Idempotency-Key']).toBe('idem-revoke-1')
  })

  it('requires the idempotency key at the type level', () => {
    // @ts-expect-error the Idempotency-Key is mandatory on every Blossom POST; a two-argument
    // call must not type-check. If this directive ever becomes unused, the key has become
    // optional and the guarantee is gone.
    void (() => revokeBlossoms('org-1', { ledgerEntryId: 'x', reason: 'y' }))
  })
})
