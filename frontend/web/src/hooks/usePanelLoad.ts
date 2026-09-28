import { useCallback, useRef } from 'react'

import { isCanceledError } from '@/lib/api-error'

/**
 * Runs one panel load so that only the **newest** request can change the panel's state, and so an
 * aborted request is never reported as a failure.
 *
 * Two defects made every dashboard page require a "Try again" on first load, and both are the same
 * root cause: a request that was cancelled or superseded still drove the UI.
 *
 *  - **Cancellation is not a failure.** In development React StrictMode mounts, unmounts and mounts
 *    again, so a panel's first request is aborted before it is dispatched. axios reports that as a
 *    `CanceledError` (`ERR_CANCELED`), and each panel's `catch` turned it into "could not load".
 *    The operator saw an error card, pressed "Try again", and the retry worked because a click
 *    issues a request no effect cleanup aborts.
 *  - **A late response must not overwrite a newer one.** A cancelled request can settle *after* the
 *    request that replaced it has already rendered. Unguarded, that stale rejection blanked a
 *    correct table and raised the error card over it.
 *
 * The guard is an invocation counter because `signal` is optional: the "Try again" path calls the
 * loader with no signal at all, so `signal.aborted` cannot be the test.
 *
 * The runner does not own the loading flag. Each panel already threads `isLoading` through its
 * render (including a skeleton while the first page is in flight), so the runner only decides
 * whether the outcome is still current.
 *
 * @param run The request. Its resolved value is applied only when this is the newest invocation.
 * @param apply Receives the value when the invocation is still current.
 * @param fail Called with the thrown value when it is a real failure of the newest invocation.
 * @param deps The request's inputs. The returned loader is re-created when these change, which is
 *   what makes the panels' `useEffect(..., [loader])` refetch on a search, filter or page change.
 */
export function usePanelLoad<T>(
  run: (signal?: AbortSignal) => Promise<T>,
  apply: (value: T) => void,
  fail: (error: unknown) => void,
  deps: readonly unknown[],
): (signal?: AbortSignal) => Promise<void> {
  const latest = useRef(0)

  // The callbacks are re-created on every render by the panels that use this, so the loader closes
  // over them through refs and depends only on the request inputs the caller names. Without the
  // refs, `deps` could not be the sole dependency without capturing a stale closure.
  const runRef = useRef(run)
  const applyRef = useRef(apply)
  const failRef = useRef(fail)
  runRef.current = run
  applyRef.current = apply
  failRef.current = fail

  return useCallback(
    async (signal?: AbortSignal) => {
      const invocation = ++latest.current
      try {
        const value = await runRef.current(signal)
        if (invocation !== latest.current) return
        applyRef.current(value)
      } catch (error) {
        if (invocation !== latest.current || isCanceledError(error)) return
        failRef.current(error)
      }
    },
    // The caller owns this list; `run`/`apply`/`fail` are read through refs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    deps,
  )
}
