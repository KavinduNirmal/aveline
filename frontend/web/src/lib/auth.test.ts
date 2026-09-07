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
      user_role: 'staff',
      org_role: 'org:boutique_manager',
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
    ['org:boutique_owner', true],
    ['org:boutique_supervisor', true],
    ['org:boutique_manager', true],
    ['moderator', true],
    ['admin', true],
    ['owner', true],
    ['staff', false],
    ['customer_relations', false],
    ['org:boutique_staff', false],
  ])('org_role %s -> %s', (orgRole, expected) => {
    expect(hasAdminRole({ user_role: 'staff', org_role: orgRole })).toBe(
      expected,
    )
  })

  it('grants access via user_role too', () => {
    expect(hasAdminRole({ user_role: 'admin', org_role: 'org:boutique_staff' })).toBe(
      true,
    )
  })

  it('is case-insensitive', () => {
    expect(hasAdminRole({ user_role: 'staff', org_role: 'ORG:BOUTIQUE_OWNER' })).toBe(
      true,
    )
  })

  it('treats missing roles as non-admin', () => {
    expect(hasAdminRole({})).toBe(false)
  })
})
