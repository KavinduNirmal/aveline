import { describe, expect, it } from 'vitest'

import { avelineStateConfig, AVELINE_STATES } from './avelineStates'

describe('avelineStates', () => {
  it('exposes configs for every agentic-workflow state', () => {
    expect(Object.keys(AVELINE_STATES).sort()).toEqual(
      [
        'error',
        'idle',
        'processing',
        'response',
        'searching',
        'success',
        'thinking',
        'tool_call',
        'waiting',
      ].sort(),
    )
  })

  it('idle breathes and cycles colour without sway', () => {
    const config = avelineStateConfig('idle')
    expect(config.mode).toBe('breathing')
    expect(config.colourCycle).toBe(true)
    expect(config.sway).toBe(false)
    expect(config.looping).toBe(true)
  })

  it('thinking pulses and sways', () => {
    const config = avelineStateConfig('thinking')
    expect(config.mode).toBe('pulse')
    expect(config.sway).toBe(true)
    expect(config.colourCycle).toBe(true)
  })

  it('searching ripples around the petals', () => {
    const config = avelineStateConfig('searching')
    expect(config.mode).toBe('ripple')
    expect(config.looping).toBe(true)
  })

  it('processing rotates smoothly', () => {
    const config = avelineStateConfig('processing')
    expect(config.mode).toBe('rotation')
    expect(config.spinSeconds).toBeLessThan(12)
  })

  it('tool_call uses a mechanical rotation pulse', () => {
    const config = avelineStateConfig('tool_call')
    expect(config.mode).toBe('rotationPulse')
  })

  it('waiting sways without colour cycle', () => {
    const config = avelineStateConfig('waiting')
    expect(config.mode).toBe('sway')
    expect(config.sway).toBe(true)
    expect(config.colourCycle).toBe(false)
  })

  it('success and error are one-shot (non-looping)', () => {
    expect(avelineStateConfig('success').mode).toBe('bloom')
    expect(avelineStateConfig('success').looping).toBe(false)
    expect(avelineStateConfig('error').mode).toBe('shake')
    expect(avelineStateConfig('error').looping).toBe(false)
  })

  it('response blooms then settles', () => {
    const config = avelineStateConfig('response')
    expect(config.mode).toBe('bloom')
    expect(config.looping).toBe(false)
  })

  it('defaults to idle for an unknown state', () => {
    expect(avelineStateConfig('unknown' as never)).toEqual(AVELINE_STATES.idle)
  })
})
