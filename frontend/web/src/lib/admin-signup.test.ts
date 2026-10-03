import { describe, expect, it } from 'vitest'

import { hasConsoleRole, isAdminSignUp, isOnboardingExempt } from './admin-signup'

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

  describe('isOnboardingExempt', () => {
    it('mirrors the backend Roles.OnboardingExemptRoles list', () => {
      // Aveline.Api/Authorization/Roles.cs: OnboardingExemptRoles = [moderator, admin, owner].
      // A drift here re-opens the /admin/pending redirect loop, because the API admits a role
      // that this side would bounce back into the pending screen.
      expect(isOnboardingExempt(['moderator'])).toBe(true)
      expect(isOnboardingExempt(['admin'])).toBe(true)
      expect(isOnboardingExempt(['owner'])).toBe(true)
      expect(isOnboardingExempt(['Admin'])).toBe(true)
      expect(isOnboardingExempt(['OWNER'])).toBe(true)
      expect(isOnboardingExempt(['staff', 'admin'])).toBe(true)
    })

    it('exempts a moderator even though the console door refuses it (C2)', () => {
      // The two lists are deliberately different widths: exempt from onboarding, refused at
      // the console with a stated reason rather than a redirect loop.
      expect(isOnboardingExempt(['moderator'])).toBe(true)
      expect(hasConsoleRole(['moderator'])).toBe(false)
    })

    it('does not exempt tenants, boutique roles, or an unset role', () => {
      expect(isOnboardingExempt([])).toBe(false)
      expect(isOnboardingExempt(null)).toBe(false)
      expect(isOnboardingExempt(undefined)).toBe(false)
      expect(isOnboardingExempt(['staff'])).toBe(false)
      expect(isOnboardingExempt(['customer_relations'])).toBe(false)
      expect(isOnboardingExempt(['org:boutique_owner'])).toBe(false)
      // The API assigns the literal "user" when the token carries no user_role claim.
      expect(isOnboardingExempt(['user'])).toBe(false)
    })
  })
})
