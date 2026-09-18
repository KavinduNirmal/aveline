import { describe, expect, it } from 'vitest'
import { apiBaseUrl, clerkPublishableKey } from './env'

describe('env', () => {
  it('provides default apiBaseUrl', () => {
    expect(apiBaseUrl).toBeDefined()
    expect(typeof apiBaseUrl).toBe('string')
  })

  it('clerkPublishableKey returns the key or throws with helpful message if missing', () => {
    if (import.meta.env.VITE_CLERK_PUBLISHABLE_KEY) {
      expect(clerkPublishableKey()).toBe(import.meta.env.VITE_CLERK_PUBLISHABLE_KEY)
    } else {
      expect(() => clerkPublishableKey()).toThrowError(/VITE_CLERK_PUBLISHABLE_KEY is not set/)
    }
  })
})
