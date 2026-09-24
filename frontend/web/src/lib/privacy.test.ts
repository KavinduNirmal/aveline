import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from './api-error'

const postMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    post: (...args: unknown[]) => postMock(...args),
  },
}))

import {
  describeCodeLifetime,
  describeOptOutError,
  OPT_OUT_SCOPES,
  parsePrivacyLink,
  startOptOut,
  verifyOptOut,
  type PrivacyLink,
} from './privacy'

const ORG = '9f1c1111-1111-4111-8111-111111111111'

const LINK: PrivacyLink = {
  organizationId: ORG,
  version: '1',
  signature: 'base64url-hmac',
}

beforeEach(() => {
  postMock.mockReset().mockResolvedValue({ data: {} })
})

afterEach(() => vi.clearAllMocks())

describe('parsePrivacyLink', () => {
  it('reads o, v and s from the signed link query string', () => {
    expect(parsePrivacyLink(`?o=${ORG}&v=1&s=abc-_123`)).toEqual({
      organizationId: ORG,
      version: '1',
      signature: 'abc-_123',
    })
  })

  it('accepts a query string without a leading question mark', () => {
    expect(parsePrivacyLink(`o=${ORG}&v=1&s=abc`)).toEqual({
      organizationId: ORG,
      version: '1',
      signature: 'abc',
    })
  })

  it('is case-insensitive about the half of the uuid the link spells in hex', () => {
    expect(parsePrivacyLink(`?o=${ORG.toUpperCase()}&v=1&s=abc`)?.organizationId).toBe(
      ORG.toUpperCase(),
    )
  })

  it.each([
    ['no query at all', ''],
    ['missing the organization', '?v=1&s=abc'],
    ['missing the version', `?o=${ORG}&s=abc`],
    ['missing the signature', `?o=${ORG}&v=1`],
    ['a blank signature', `?o=${ORG}&v=1&s=`],
    ['a blank version', `?o=${ORG}&v=&s=abc`],
    ['an organization that is not a uuid', '?o=not-an-org&v=1&s=abc'],
    ['a truncated uuid', '?o=9f1c1111-1111-4111-8111&v=1&s=abc'],
  ])('refuses %s rather than sending a half-formed link to the server', (_label, search) => {
    expect(parsePrivacyLink(search)).toBeNull()
  })
})

describe('startOptOut', () => {
  it('posts the link values with the phone and the scope to the anonymous start route', async () => {
    await startOptOut(LINK, '0771234567', 'all')

    expect(postMock).toHaveBeenCalledTimes(1)
    const [path, body] = postMock.mock.calls[0]
    expect(path).toBe('/api/v1/privacy/opt-out/start')
    expect(body).toEqual({
      organizationId: ORG,
      phoneNumber: '0771234567',
      scope: 'all',
      version: '1',
      signature: 'base64url-hmac',
    })
  })

  it('reports the accepted status, the opaque handle and the code lifetime', async () => {
    postMock.mockResolvedValue({
      data: { status: 'accepted', handle: 'opaque-handle', expiresInSeconds: 300 },
    })

    await expect(startOptOut(LINK, '0771234567', 'org')).resolves.toEqual({
      status: 'accepted',
      handle: 'opaque-handle',
      expiresInSeconds: 300,
    })
  })

  it('reports a null handle rather than inventing one when the body omits it', async () => {
    // A defensive read: the endpoint mints the handle unconditionally, so an absent one is a
    // regression the page must degrade on, not a normal state.
    postMock.mockResolvedValue({ data: { status: 'accepted' } })

    await expect(startOptOut(LINK, '0771234567', 'org')).resolves.toEqual({
      status: 'accepted',
      handle: null,
      expiresInSeconds: null,
    })
  })
})

describe('verifyOptOut', () => {
  it('posts the handle, the code and the link values to the anonymous verify route', async () => {
    postMock.mockResolvedValue({
      data: { status: 'revoked', scope: 'org', effectiveAtUtc: '2026-09-25T10:15:00Z' },
    })

    await verifyOptOut({
      link: LINK,
      handle: 'opaque-handle',
      phoneNumber: '0771234567',
      otp: '123456',
      scope: 'org',
    })

    expect(postMock).toHaveBeenCalledTimes(1)
    const [path, body] = postMock.mock.calls[0]
    expect(path).toBe('/api/v1/privacy/opt-out/verify')
    expect(body).toEqual({
      organizationId: ORG,
      handle: 'opaque-handle',
      phoneNumber: '0771234567',
      otp: '123456',
      scope: 'org',
      version: '1',
      signature: 'base64url-hmac',
    })
  })

  it('returns the server-reported revocation', async () => {
    postMock.mockResolvedValue({
      data: { status: 'revoked', scope: 'all', effectiveAtUtc: '2026-09-25T10:15:00Z' },
    })

    await expect(
      verifyOptOut({
        link: LINK,
        handle: 'h',
        phoneNumber: '0771234567',
        otp: '123456',
        scope: 'all',
      }),
    ).resolves.toEqual({
      status: 'revoked',
      scope: 'all',
      effectiveAtUtc: '2026-09-25T10:15:00Z',
    })
  })
})

describe('describeOptOutError', () => {
  it('reads the business code from the response body, not from the transport code', () => {
    // The real client surfaces axios's transport code (`ERR_BAD_REQUEST`); the API's own code lives
    // in the response body, which `ApiError` carries as `details`. Mapping on `error.code` alone
    // silently falls through to the server's raw message - the E2E walk caught exactly that.
    const error = new ApiError(
      400,
      'The code is invalid, expired or was already used.',
      'ERR_BAD_REQUEST',
      { code: 'otp-invalid', message: 'The code is invalid, expired or was already used.' },
    )

    expect(describeOptOutError(error)).toMatch(/not valid, has expired, or was already used/i)
  })

  it('reads a rate-limit and an outage code from the body too', () => {
    expect(
      describeOptOutError(
        new ApiError(429, 'slow down', 'ERR_BAD_REQUEST', { code: 'otp-rate-limited' }),
      ),
    ).toMatch(/too many/i)
    expect(
      describeOptOutError(
        new ApiError(503, 'down', 'ERR_BAD_RESPONSE', { code: 'otp-unavailable' }),
      ),
    ).toMatch(/temporarily unavailable/i)
  })

  it('does not tell the customer their number is unknown, which would undo anti-enumeration', () => {
    const message = describeOptOutError(new ApiError(400, 'bad', 'otp-invalid'))

    expect(message.toLowerCase()).not.toContain('not found')
    expect(message.toLowerCase()).not.toContain('unknown')
    expect(message.toLowerCase()).not.toContain('no record')
  })

  it.each([
    ['otp-invalid', 'not valid, has expired, or was already used'],
    ['otp-rate-limited', 'too many'],
    ['otp-unavailable', 'temporarily unavailable'],
    ['invalid-phone-number', 'Sri Lankan'],
  ])('maps the %s code to copy that names the cause', (code, fragment) => {
    expect(describeOptOutError(new ApiError(400, 'x', code)).toLowerCase()).toContain(
      fragment.toLowerCase(),
    )
  })

  it('treats a 429 without a code as a rate limit', () => {
    expect(describeOptOutError(new ApiError(429, 'x')).toLowerCase()).toContain('too many')
  })

  it('treats a 503 without a code as an outage', () => {
    expect(describeOptOutError(new ApiError(503, 'x')).toLowerCase()).toContain(
      'temporarily unavailable',
    )
  })

  it('falls back to the ApiError message, then to a generic sentence', () => {
    expect(describeOptOutError(new ApiError(400, 'A specific server reason.'))).toBe(
      'A specific server reason.',
    )
    expect(describeOptOutError(new Error('boom')).toLowerCase()).toContain('did not complete')
  })
})

describe('describeCodeLifetime', () => {
  it('reads the lifetime the server reported rather than assuming five minutes', () => {
    expect(describeCodeLifetime(300)).toBe('The code expires in 5 minutes.')
    expect(describeCodeLifetime(60)).toBe('The code expires in 1 minute.')
    expect(describeCodeLifetime(120)).toBe('The code expires in 2 minutes.')
  })

  it('rounds to the nearest minute, never below one', () => {
    expect(describeCodeLifetime(30)).toBe('The code expires in 1 minute.')
    expect(describeCodeLifetime(90)).toBe('The code expires in 2 minutes.')
  })

  it('says only that it expires shortly when the server omitted the lifetime', () => {
    expect(describeCodeLifetime(null)).toBe('The code expires shortly.')
    expect(describeCodeLifetime(0)).toBe('The code expires shortly.')
  })
})

describe('OPT_OUT_SCOPES', () => {
  it('is exactly the two scope literals the API validates', () => {
    expect(OPT_OUT_SCOPES).toEqual(['org', 'all'])
  })
})
