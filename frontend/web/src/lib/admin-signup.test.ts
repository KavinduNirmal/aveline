import { describe, expect, it } from 'vitest'

import { hasConsoleRole, isAdminSignUp } from './admin-signup'

describe('admin-signup', () => {
  describe('isAdminSignUp', () => {
    it('is true only for unsafeMetadata.accountType === "admin"', () => {
      expect(isAdminSignUp({ unsafeMetadata: { accountType: 'admin' } })).toBe(true)
    })

    it('is false for other account types and missing metadata', () => {
      expect(isAdminSignUp({ unsafeMetadata: { accountType: 'owner' } })).toBe(false)
      expect(isAdminSignUp({ unsafeMetadata: { accountType: 'staff' } })).toBe(false)
      expect(isAdminSignUp({ unsafeMetadata: {} })).toBe(false)
      expect(isAdminSignUp({})).toBe(false)
      expect(isAdminSignUp(null)).toBe(false)
      expect(isAdminSignUp(undefined)).toBe(false)
    })
  })

  describe('hasConsoleRole', () => {
    it('admits team and boutique management roles, case-insensitively', () => {
      expect(hasConsoleRole(['owner'])).toBe(true)
      expect(hasConsoleRole(['Admin'])).toBe(true)
      expect(hasConsoleRole(['moderator'])).toBe(true)
      expect(hasConsoleRole(['org:boutique_owner'])).toBe(true)
      expect(hasConsoleRole(['org:boutique_manager'])).toBe(true)
      expect(hasConsoleRole(['org:boutique_supervisor'])).toBe(true)
      expect(hasConsoleRole(['staff', 'owner'])).toBe(true)
    })

    it('rejects roles that do not grant console access', () => {
      expect(hasConsoleRole([])).toBe(false)
      expect(hasConsoleRole(null)).toBe(false)
      expect(hasConsoleRole(undefined)).toBe(false)
      expect(hasConsoleRole(['staff'])).toBe(false)
      expect(hasConsoleRole(['org:boutique_staff'])).toBe(false)
    })
  })
})
