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

  it('returns unknown for a non-self id while managed scope is disabled', async () => {
    // Q1 is unproven (the probe returned 401), so managed scope is off by default and the
    // segment is a restatement of the caller.
    const findUserById = vi.fn()
    await expect(
      resolveAdminScope({
        segment: OTHER_ID,
        sessionUserId: USER_ID,
        canReadUsers: true,
        findUserById,
      }),
    ).resolves.toEqual({ kind: 'unknown' })
    expect(findUserById).not.toHaveBeenCalled()
  })
})
