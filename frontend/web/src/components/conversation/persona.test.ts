import { describe, expect, it } from 'vitest'

import { personaForAgent, personaForAuthor } from './persona'

describe('personaForAgent', () => {
  it('returns Aveline for the aveline key', () => {
    expect(personaForAgent('aveline').name).toBe('Aveline')
  })

  it('returns Ava for the ava key', () => {
    expect(personaForAgent('ava').name).toBe('Ava')
  })

  it('returns Elle for the elle key', () => {
    expect(personaForAgent('elle').name).toBe('Elle')
  })

  it('returns Lina for the lina key', () => {
    expect(personaForAgent('lina').name).toBe('Lina')
  })

  /**
   * Lina's accent must not go through `commerce`.
   *
   * `commerce` is the brand wine-rose and is also what paints the documentation section, the
   * pricing CTA and the orders/payments surfaces. Routing her accent through it meant an
   * agent-colour change would recolour all of those, and it also made her avatar identical to
   * Aveline's, who uses `primary` - the same value. She has her own lilac token instead.
   */
  describe("Lina's accent", () => {
    it('uses the lilac token rather than commerce or primary', () => {
      const lina = personaForAgent('lina')
      expect(lina.bg).toBe('bg-lilac')
      expect(lina.text).toBe('text-lilac')
      expect(lina.bgSoft).toBe('bg-lilac/10')
      expect(lina.ring).toBe('ring-lilac/20')
    })

    it('is distinct from every other persona accent', () => {
      const accents = ['aveline', 'ava', 'elle', 'lina'].map((key) => personaForAgent(key).bg)
      expect(new Set(accents).size).toBe(4)
    })

    it('leaves Aveline on the brand primary and the others on their own tokens', () => {
      expect(personaForAgent('aveline').bg).toBe('bg-primary')
      expect(personaForAgent('ava').bg).toBe('bg-memory')
      expect(personaForAgent('elle').bg).toBe('bg-visual')
    })
  })

  it('defaults to Aveline for an unknown key', () => {
    expect(personaForAgent('unknown').name).toBe('Aveline')
  })

  it('defaults to Aveline for null/undefined', () => {
    expect(personaForAgent(null).name).toBe('Aveline')
    expect(personaForAgent(undefined).name).toBe('Aveline')
  })
})

describe('personaForAuthor', () => {
  it('returns the persona for an Agent author', () => {
    expect(personaForAuthor('Agent', 'elle')?.name).toBe('Elle')
  })

  it('returns null for a User author', () => {
    expect(personaForAuthor('User', null)).toBeNull()
  })

  it('returns null for a System author', () => {
    expect(personaForAuthor('System', null)).toBeNull()
  })
})
