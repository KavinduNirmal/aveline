import { describe, expect, it } from 'vitest'

import { decodeJwtPayload, hasAdminRole } from './auth'

function makeToken(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
  return `header.${payload}.signature`
}

describe('decodeJwtPayload', () => {
  it('decodes a JWT payload', () => {
    const claims = {
      user_role: 'associate',
      org_role: 'org:manager',
      org_id: 'org_1',
    }

    expect(decodeJwtPayload(makeToken(claims))).toEqual(claims)
  })

  it('returns an empty object for a malformed token', () => {
    expect(decodeJwtPayload('not-a-token')).toEqual({})
  })
})

describe('hasAdminRole', () => {
  it.each([
    ['org:owner', true],
    ['org:manager', true],
    ['org:admin', true],
    ['manager', true],
    ['owner', true],
    ['associate', false],
    ['org:associate', false],
    ['org:member', false],
  ])('org_role %s -> %s', (orgRole, expected) => {
    expect(hasAdminRole({ user_role: 'associate', org_role: orgRole })).toBe(
      expected,
    )
  })

  it('grants access via user_role too', () => {
    expect(hasAdminRole({ user_role: 'manager', org_role: 'associate' })).toBe(
      true,
    )
  })

  it('is case-insensitive', () => {
    expect(hasAdminRole({ user_role: 'associate', org_role: 'ORG:OWNER' })).toBe(
      true,
    )
  })

  it('treats missing roles as non-admin', () => {
    expect(hasAdminRole({})).toBe(false)
  })
})
