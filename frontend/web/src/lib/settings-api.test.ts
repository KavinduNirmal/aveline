import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()
const patchMock = vi.fn()
const deleteMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...a: unknown[]) => getMock(...a),
    post: (...a: unknown[]) => postMock(...a),
    patch: (...a: unknown[]) => patchMock(...a),
    delete: (...a: unknown[]) => deleteMock(...a),
  },
}))

import {
  createApiKey,
  deleteApiKey,
  fetchApiKeys,
  fetchSettings,
  revokeApiKey,
  updateSettings,
} from './settings-api'

const ORG = '11111111-1111-1111-1111-111111111111'
const KEY = '44444444-4444-4444-4444-444444444444'

describe('settings-api', () => {
  beforeEach(() => {
    getMock.mockReset().mockResolvedValue({ data: {} })
    postMock.mockReset().mockResolvedValue({ data: {} })
    patchMock.mockReset().mockResolvedValue({ data: {} })
    deleteMock.mockReset().mockResolvedValue({ data: undefined })
  })

  afterEach(() => vi.clearAllMocks())

  it('reads settings and entitlements from their own routes', async () => {
    await fetchSettings(ORG)
    await fetchApiKeys(ORG)

    expect(getMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/settings`)
    expect(getMock.mock.calls[1][0]).toBe(`/api/v1/orgs/${ORG}/api-keys`)
  })

  it('patches only the profile fields the caller changed', async () => {
    await updateSettings(ORG, { name: 'New Name', contactEmail: 'hello@boutique.lk' })

    const [path, body] = patchMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}`)
    expect(body).toEqual({ name: 'New Name', contactEmail: 'hello@boutique.lk' })
  })

  it('creates a key and returns the one-time secret', async () => {
    await createApiKey(ORG, { name: 'POS', scopes: ['catalog:read'], environment: 'test' })

    const [path, body] = postMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/api-keys`)
    expect(body).toEqual({ name: 'POS', scopes: ['catalog:read'], environment: 'test' })
  })

  it('revokes and deletes through their own routes', async () => {
    await revokeApiKey(ORG, KEY, 'rotated')
    await deleteApiKey(ORG, KEY)

    expect(postMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/api-keys/${KEY}/revoke`)
    expect(postMock.mock.calls[0][1]).toEqual({ reason: 'rotated' })
    expect(deleteMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/api-keys/${KEY}`)
  })
})
