import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { useDashboardWindow } from './useDashboardWindow'

/**
 * The shell's single window state (T4).
 *
 * `useDashboardWindow.test.ts` pins the boundary arithmetic and the preset list as pure functions;
 * this file renders the hook, because the thing the shell actually consumes is the state pair — the
 * window and the range it resolves to — and a hook whose `setWindow` moved one without the other
 * would make two panels describe different periods. It is a `*.dom.test.*` file because it needs a
 * renderer, not the node project.
 */
describe('useDashboardWindow', () => {
  it('defaults to the last 30 days', () => {
    const { result } = renderHook(() => useDashboardWindow())
    expect(result.current.window).toBe('30d')
  })

  it('starts on the requested window with a resolved range', () => {
    const { result } = renderHook(() => useDashboardWindow('7d'))

    expect(result.current.window).toBe('7d')
    expect(new Date(result.current.range.from).getTime()).toBeLessThan(
      new Date(result.current.range.to).getTime(),
    )
  })

  it('moves the window and its range together, so two panels cannot disagree', () => {
    const { result } = renderHook(() => useDashboardWindow('30d'))
    const before = result.current.range

    act(() => result.current.setWindow('ytd'))

    expect(result.current.window).toBe('ytd')
    expect(result.current.range).not.toEqual(before)
    // Year-to-date starts at 00:00 UTC on 1 January, the same boundary the server uses.
    expect(result.current.range.from).toBe(`${new Date().getUTCFullYear()}-01-01T00:00:00.000Z`)
  })

  it('keeps the setter stable across renders', () => {
    const { result, rerender } = renderHook(() => useDashboardWindow('30d'))
    const setter = result.current.setWindow

    rerender()

    expect(result.current.setWindow).toBe(setter)
  })
})
