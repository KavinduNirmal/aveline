import { describe, expect, it, vi } from 'vitest'

import type { AdminUserDto } from '@/types/admin'
import { isGuidLike, resolveAdminScope } from './scope'

const USER_ID = '01a0ba0b-485f-780e-b465-ae11670539e9'
const OTHER_ID = '11111111-1111-7111-8111-111111111111'

function user(id: string): AdminUserDto {
  return { id } as AdminUserDto
}

describe('isGuidLike', () => {
  it('accepts a GUID and rejects a name, an email and a Clerk id', () => {
    expect(isGuidLike(USER_ID)).toBe(true)
    expect(isGuidLike('kaveesha')).toBe(false)
    expect(isGuidLike('owner@aveline.lk')).toBe(false)
    expect(isGuidLike('user_2abcDEF')).toBe(false)
  })
})

describe('resolveAdminScope', () => {
  it('returns unknown for a missing segment, without a request', async () => {
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: undefined,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'unknown' })
    expect(findUserById).not.toHaveBeenCalled()
  })

  it('resolves self by equality, before any shape check, and makes no request', async () => {
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: USER_ID,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'self', userId: USER_ID })

    // Clerk subject ids are not GUIDs; equality must win over the shape check or a caller
    // could never open their own console.
    await expect(
      resolveAdminScope({
        segment: 'user_2abcDEF',
        sessionUserId: 'user_2abcDEF',
        canReadUsers: false,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'self', userId: 'user_2abcDEF' })

    expect(findUserById).not.toHaveBeenCalled()
  })

  it('returns unknown for a non-GUID that is not the caller', async () => {
    await expect(
      resolveAdminScope({
        segment: 'kaveesha',
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById: vi.fn(),
      }),
    ).resolves.toEqual({ kind: 'unknown' })
  })

  it('returns forbidden when the caller may not read users', async () => {
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: OTHER_ID,
        sessionUserId: USER_ID,
        canReadUsers: false,
        findUserById,
        managedEnabled: true,
      }),
    ).resolves.toEqual({ kind: 'forbidden' })
    expect(findUserById).not.toHaveBeenCalled()
  })

  it('resolves managed only on an exact id hit', async () => {
    const findUserById = vi.fn().mockResolvedValue(user(OTHER_ID))
    await expect(
      resolveAdminScope({
        segment: OTHER_ID,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
        managedEnabled: true,
      }),
    ).resolves.toEqual({ kind: 'managed', userId: OTHER_ID, user: user(OTHER_ID) })
  })

  it('never resolves managed from a fuzzy hit', async () => {
    // A name or email search that returns a near-match must not become `managed`: showing
    // admin data scoped to a guessed user is the same failure class as a fabricated dashboard.
    const findUserById = vi.fn().mockResolvedValue(null)
    await expect(
      resolveAdminScope({
        segment: OTHER_ID,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
        managedEnabled: true,
      }),
    ).resolves.toEqual({ kind: 'unknown' })
  })

  it('falls back to unknown (never managed) while managed scope is explicitly disabled', async () => {
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: OTHER_ID,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
        managedEnabled: false,
      }),
    ).resolves.toEqual({ kind: 'unknown' })
    expect(findUserById).not.toHaveBeenCalled()
  })

  it('treats the application database id as self, not only the Clerk subject', async () => {
    // The console's URLs are built from the database id (`User.Id`, a UUIDv7 Guid), while
    // `/auth/claims` returns the Clerk subject. Comparing only against the subject is why a
    // caller's own `/admin/<guid>/dashboard` failed to resolve.
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: USER_ID,
        sessionUserId: 'user_2abcDEF',
        selfUserIds: [USER_ID],
        canReadUsers: true,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'self', userId: USER_ID })
    expect(findUserById).not.toHaveBeenCalled()
  })

  it('still resolves self from the Clerk subject when it appears in the URL', async () => {
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: 'user_2abcDEF',
        sessionUserId: 'user_2abcDEF',
        selfUserIds: [USER_ID],
        canReadUsers: true,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'self', userId: 'user_2abcDEF' })
  })

  it('has managed scope enabled by default, because the guard falls back rather than dead-ends', async () => {
    // Q1's probe is still unproven, so the resolver cannot be the only thing between an
    // administrator and their console: `AdminRouteGuard` redirects an `unknown` scope to the
    // caller's own console (C1 option (c)).
    const findUserById = vi.fn().mockResolvedValue(user(OTHER_ID))
    await expect(
      resolveAdminScope({
        segment: OTHER_ID,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'managed', userId: OTHER_ID, user: user(OTHER_ID) })
  })
})
