import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api-error'

import { usePanelLoad } from './usePanelLoad'

describe('usePanelLoad', () => {
  it('applies the value of the newest invocation', async () => {
    const apply = vi.fn()
    const fail = vi.fn()
    const { result } = renderHook(() =>
      usePanelLoad(async () => 'book', apply, fail, []),
    )

    await result.current()

    expect(apply).toHaveBeenCalledWith('book')
    expect(fail).not.toHaveBeenCalled()
  })

  it('does not report an aborted request as a failure', async () => {
    // This is the defect behind "every page needs Try again": StrictMode aborts the first request,
    // and a cancellation is a decision already made, not a load failure.
    const apply = vi.fn()
    const fail = vi.fn()
    const { result } = renderHook(() =>
      usePanelLoad(
        async () => {
          throw new ApiError(0, 'canceled', 'ERR_CANCELED')
        },
        apply,
        fail,
        [],
      ),
    )

    await result.current()

    expect(fail).not.toHaveBeenCalled()
    expect(apply).not.toHaveBeenCalled()
  })

  it('still reports a real failure', async () => {
    const apply = vi.fn()
    const fail = vi.fn()
    const { result } = renderHook(() =>
      usePanelLoad(
        async () => {
          throw new ApiError(500, 'Something went wrong')
        },
        apply,
        fail,
        [],
      ),
    )

    await result.current()

    expect(fail).toHaveBeenCalledTimes(1)
    expect(apply).not.toHaveBeenCalled()
  })

  it('discards a slow response that a newer request has already replaced', async () => {
    const apply = vi.fn()
    const fail = vi.fn()
    let settleSlow: ((value: string) => void) | null = null as ((value: string) => void) | null
    let call = 0
    const { result } = renderHook(() =>
      usePanelLoad(
        async () => {
          call += 1
          if (call === 1) {
            return await new Promise<string>((resolve) => {
              settleSlow = resolve as (value: string) => void
            })
          }
          return 'newer'
        },
        apply,
        fail,
        [],
      ),
    )

    const slow = result.current()
    await result.current()
    await waitFor(() => expect(apply).toHaveBeenCalledWith('newer'))

    // The superseded request now resolves with data that is no longer current.
    settleSlow?.('stale')
    await slow

    expect(apply).toHaveBeenCalledTimes(1)
    expect(apply).not.toHaveBeenCalledWith('stale')
  })

  it('discards a stale failure so it cannot blank newer data', async () => {
    const apply = vi.fn()
    const fail = vi.fn()
    let rejectSlow: ((reason: unknown) => void) | null = null as ((reason: unknown) => void) | null
    let call = 0
    const { result } = renderHook(() =>
      usePanelLoad(
        async () => {
          call += 1
          if (call === 1) {
            return await new Promise<string>((_resolve, reject) => {
              rejectSlow = reject as (reason: unknown) => void
            })
          }
          return 'newer'
        },
        apply,
        fail,
        [],
      ),
    )

    const slow = result.current()
    await result.current()
    await waitFor(() => expect(apply).toHaveBeenCalledWith('newer'))

    rejectSlow?.(new ApiError(500, 'Something went wrong'))
    await slow

    expect(fail).not.toHaveBeenCalled()
  })
})
