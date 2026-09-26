import { renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { useConstrainedDevice } from './useConstrainedDevice'

const original = Object.getOwnPropertyDescriptor(globalThis, 'navigator')

function withNavigator(value: unknown) {
  Object.defineProperty(globalThis, 'navigator', { value, configurable: true, writable: true })
}

afterEach(() => {
  if (original) Object.defineProperty(globalThis, 'navigator', original)
})

describe('useConstrainedDevice', () => {
  it('reports a constrained device on the first render, without a second pass', () => {
    // Synchronous on purpose: gating in an effect would render the full 42-layer animation
    // budget once and then unmount it, which is the cost the gate exists to avoid.
    withNavigator({ connection: { saveData: true } })
    const { result } = renderHook(() => useConstrainedDevice())
    expect(result.current).toBe(true)
  })

  it('reports an unconstrained device', () => {
    withNavigator({ connection: { effectiveType: '4g' }, deviceMemory: 8 })
    const { result } = renderHook(() => useConstrainedDevice())
    expect(result.current).toBe(false)
  })

  it('holds the answer stable across re-renders', () => {
    withNavigator({ connection: { saveData: true } })
    const { result, rerender } = renderHook(() => useConstrainedDevice())
    withNavigator({ connection: { saveData: false }, deviceMemory: 8 })
    rerender()
    expect(result.current).toBe(true)
  })
})
