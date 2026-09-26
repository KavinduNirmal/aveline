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
    it('admits exactly the owner and admin roles, case-insensitively', () => {
      expect(hasConsoleRole(['owner'])).toBe(true)
      expect(hasConsoleRole(['Admin'])).toBe(true)
      expect(hasConsoleRole(['OWNER'])).toBe(true)
      expect(hasConsoleRole(['staff', 'owner'])).toBe(true)
    })

    it('refuses a moderator, which holds no admin surface of its own (C2)', () => {
      expect(hasConsoleRole(['moderator'])).toBe(false)
    })

    it('refuses boutique roles, which have their own dashboard at app/b/{slug}', () => {
      expect(hasConsoleRole(['org:boutique_owner'])).toBe(false)
      expect(hasConsoleRole(['org:boutique_manager'])).toBe(false)
      expect(hasConsoleRole(['org:boutique_supervisor'])).toBe(false)
      expect(hasConsoleRole(['org:boutique_staff'])).toBe(false)
    })

    it('rejects roles that do not grant console access', () => {
      expect(hasConsoleRole([])).toBe(false)
      expect(hasConsoleRole(null)).toBe(false)
      expect(hasConsoleRole(undefined)).toBe(false)
      expect(hasConsoleRole(['staff'])).toBe(false)
      expect(hasConsoleRole(['customer_relations'])).toBe(false)
    })
  })
})
