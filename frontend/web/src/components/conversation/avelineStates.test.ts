import { describe, expect, it } from 'vitest'

import { avelineStateConfig, AVELINE_STATES } from './avelineStates'

describe('avelineStates', () => {
  it('exposes configs for every planned state', () => {
    expect(Object.keys(AVELINE_STATES).sort()).toEqual(
      ['awaiting', 'error', 'idle', 'thinking', 'working'].sort(),
    )
  })

  it('idle cycles colour and spins slowly without sway', () => {
    const config = avelineStateConfig('idle')
    expect(config.colourCycle).toBe(true)
    expect(config.sway).toBe(false)
    expect(config.spinSeconds).toBe(12)
  })

  it('thinking sways and spins faster', () => {
    const config = avelineStateConfig('thinking')
    expect(config.sway).toBe(true)
    expect(config.colourCycle).toBe(true)
    expect(config.spinSeconds).toBeLessThan(12)
  })

  it('defaults to idle for an unknown state', () => {
    expect(avelineStateConfig('unknown' as never)).toEqual(AVELINE_STATES.idle)
  })
})
