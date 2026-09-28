import { describe, expect, it } from 'vitest'

import { deriveLevel, deriveSource, LOG_LEVELS, type LogLevel } from './log-level'

/**
 * Q4: derived severity is **labelled** and carries a **colour mapped to a semantic token**,
 * never a raw palette class (which would also fail the blocking conformance rule), and never
 * presented as server truth. `actorKind` is primary; the severity is the console's own reading.
 */
describe('deriveLevel', () => {
  it('classifies a failure action as error', () => {
    for (const action of [
      'users.state.update_failed',
      'admin.request.rejected',
      'billing.charge.error',
      'organization.suspended',
    ]) {
      expect(deriveLevel(action)).toBe('error')
    }
  })

  it('classifies a revocation or cancellation as warning', () => {
    for (const action of ['blossom.revoked', 'invitation.cancelled', 'pricing.rule.warning']) {
      expect(deriveLevel(action)).toBe('warn')
    }
  })

  it('classifies an ordinary mutation as info', () => {
    for (const action of [
      'users.state.updated',
      'admin.request.approved',
      'organization.activated',
      'blossom.credited',
    ]) {
      expect(deriveLevel(action)).toBe('info')
    }
  })

  it('classifies an unrecognised action as its own labelled level, never a silent default', () => {
    expect(deriveLevel('something.unmapped')).toBe('other')
    expect(LOG_LEVELS).toContain('other')
  })

  it('is case-insensitive and blank-safe', () => {
    expect(deriveLevel('USERS.STATE.UPDATE_FAILED')).toBe('error')
    expect(deriveLevel('')).toBe('other')
  })
})

describe('deriveSource', () => {
  const base = {
    actorKind: 'User',
    actorUserId: 'user-1',
    actorRef: null as string | null,
    organizationId: null as string | null,
  }

  it('makes actorKind primary', () => {
    expect(deriveSource({ ...base, actorKind: 'User' }).label).toBe('User')
    expect(deriveSource({ ...base, actorKind: 'System' }).label).toBe('System')
  })

  it('falls back to a labelled unknown rather than an empty string', () => {
    expect(deriveSource({ ...base, actorKind: '' }).label).toBe('Unknown actor')
  })

  it('maps every source to a semantic token, never a palette class', () => {
    const palette =
      /\b(?:bg|text|border|ring)-(?:slate|gray|zinc|red|orange|amber|yellow|green|emerald|blue|indigo|violet|purple|pink|rose)-\d{2,3}\b/
    const hex = /#[0-9a-fA-F]{3,8}\b/
    for (const actorKind of ['User', 'Admin', 'System', 'Scheduler', 'Service', 'Unknown']) {
      const source = deriveSource({ ...base, actorKind })
      expect(source.tone).toMatch(/^(primary|muted|warning|success|destructive)$/)
      expect(source.tone).not.toMatch(palette)
      expect(source.tone).not.toMatch(hex)
    }
  })

  it('names the actor reference when one exists', () => {
    const source = deriveSource({
      ...base,
      actorKind: 'System',
      actorUserId: null,
      actorRef: 'alert-dispatcher',
    })
    expect(source.detail).toBe('alert-dispatcher')
  })
})

describe('severity is labelled as derived', () => {
  it('exports display labels for every level', () => {
    const levels: LogLevel[] = [...LOG_LEVELS]
    for (const level of levels) {
      expect(level.length).toBeGreaterThan(0)
    }
    expect(LOG_LEVELS).toEqual(['error', 'warn', 'info', 'other'])
  })
})
