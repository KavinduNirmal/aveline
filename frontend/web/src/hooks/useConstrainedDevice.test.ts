import { afterEach, describe, expect, it } from 'vitest'

import { isConstrainedDevice } from './useConstrainedDevice'

/**
 * Each signal is exercised on its own, because the whole point of the gate is that it fires on
 * *some* devices and not others — a test that only asserted "true for a slow device" would pass
 * just as well if the function returned `true` unconditionally.
 */
const original = Object.getOwnPropertyDescriptor(globalThis, 'navigator')

function withNavigator(value: unknown) {
  Object.defineProperty(globalThis, 'navigator', { value, configurable: true, writable: true })
}

afterEach(() => {
  if (original) Object.defineProperty(globalThis, 'navigator', original)
})

describe('isConstrainedDevice', () => {
  it('is false for a capable device with no signals set', () => {
    withNavigator({ connection: { saveData: false, effectiveType: '4g' }, deviceMemory: 8 })
    expect(isConstrainedDevice()).toBe(false)
  })

  it('is true when the reader asked to save data', () => {
    withNavigator({ connection: { saveData: true, effectiveType: '4g' }, deviceMemory: 8 })
    expect(isConstrainedDevice()).toBe(true)
  })

  it('is true on the entry-level memory band', () => {
    withNavigator({ connection: { effectiveType: '4g' }, deviceMemory: 4 })
    expect(isConstrainedDevice()).toBe(true)
  })

  it('is false just above the memory band', () => {
    withNavigator({ connection: { effectiveType: '4g' }, deviceMemory: 8 })
    expect(isConstrainedDevice()).toBe(false)
  })

  it('is true on 2G-class connections', () => {
    withNavigator({ connection: { effectiveType: '2g' } })
    expect(isConstrainedDevice()).toBe(true)
    withNavigator({ connection: { effectiveType: 'slow-2g' } })
    expect(isConstrainedDevice()).toBe(true)
  })

  it('is false on 3G, which is slow but not a reason to drop the decoration', () => {
    withNavigator({ connection: { effectiveType: '3g' } })
    expect(isConstrainedDevice()).toBe(false)
  })

  it('tolerates a browser with no Network Information API at all', () => {
    withNavigator({})
    expect(isConstrainedDevice()).toBe(false)
  })

  it('tolerates a runtime with no navigator', () => {
    // @ts-expect-error deliberately removing the global to exercise the server-render path
    delete globalThis.navigator
    expect(isConstrainedDevice()).toBe(false)
  })
})
