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
