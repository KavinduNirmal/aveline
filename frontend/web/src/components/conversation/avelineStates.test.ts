import { describe, expect, it } from 'vitest'

import { isTerminalState } from './avelineStates'

describe('isTerminalState', () => {
  it('marks success / error / response as terminal', () => {
    expect(isTerminalState('success')).toBe(true)
    expect(isTerminalState('error')).toBe(true)
    expect(isTerminalState('response')).toBe(true)
  })

  it('does not mark in-progress states as terminal', () => {
    for (const state of ['idle', 'thinking', 'searching', 'processing', 'tool_call', 'waiting']) {
      expect(isTerminalState(state as never)).toBe(false)
    }
  })
})
