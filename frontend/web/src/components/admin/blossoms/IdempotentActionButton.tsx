import { useCallback, useEffect, useRef, useState } from "react"

import { Button } from "@/components/ui/button"
import {
  afterOutcome,
  beginOperation,
  operationFingerprint,
  outcomeForResponse,
  type IdempotencyState,
  type OperationSnapshot,
  type Outcome,
} from "@/lib/admin/idempotency"

export interface ExecutionResult {
  replayed: boolean
}

export class IdempotentCallError extends Error {
  status: number
  code?: string
  constructor(status: number, message: string, code?: string) {
    super(message)
    this.status = status
    if (code !== undefined) this.code = code
  }
}

/**
 * The only way a money-shaped write is submitted.
 *
 * Two guarantees, both asserted in `IdempotentActionButton.dom.test.tsx`:
 *
 * 1. **Single-flight.** A double-click produces exactly one request, because the guard is a ref
 *    read synchronously before the first `await`, not a `disabled` attribute that React has not
 *    re-rendered yet.
 * 2. **One key per logical operation.** The key is derived from the payload fingerprint, so a
 *    retry of the same payload reuses it and the server sees one operation.
 */
export function IdempotentActionButton({
  snapshot,
  execute,
  label,
  disabled = false,
  onSettled,
}: {
  snapshot: OperationSnapshot
  execute: (input: {
    key: string
    snapshot: OperationSnapshot
  }) => Promise<ExecutionResult>
  label: string
  disabled?: boolean
  onSettled?: (outcome: Outcome) => void
}) {
  const fingerprint = operationFingerprint(snapshot)
  const [state, setState] = useState<IdempotencyState>(() =>
    beginOperation({ snapshot, previous: null, mintKey: () => crypto.randomUUID() }),
  )
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [blocked, setBlocked] = useState(false)
  const inFlight = useRef(false)

  // A changed payload deserves a different key; reusing one would make the server replay the
  // *old* result.
  useEffect(() => {
    setState((previous) =>
      beginOperation({ snapshot, previous, mintKey: () => crypto.randomUUID() }),
    )
    setMessage(null)
    setBlocked(false)
  }, [fingerprint, snapshot])

  const submit = useCallback(async () => {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setMessage(null)

    try {
      const result = await execute({ key: state.key, snapshot })
      const decision = afterOutcome(state, result.replayed ? 'replayed' : 'success')
      setState((previous) => ({
        key: decision.key ?? crypto.randomUUID(),
        fingerprint: previous.fingerprint,
      }))
      setMessage(decision.message.length > 0 ? decision.message : null)
      onSettled?.(result.replayed ? 'replayed' : 'success')
    } catch (err: unknown) {
      const status =
        typeof (err as { status?: unknown })?.status === 'number'
          ? ((err as { status: number }).status as number)
          : 500
      const code = (err as { code?: string })?.code
      const outcome = outcomeForResponse(status, code)
      const decision = afterOutcome(state, outcome)
      setState((previous) => ({ ...previous, key: decision.key ?? previous.key }))
      setMessage(decision.message.length > 0 ? decision.message : null)
      setBlocked(decision.disableSubmit)
      onSettled?.(outcome)
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }, [execute, onSettled, snapshot, state])

  return (
    <div className="space-y-2">
      <Button
        onClick={() => void submit()}
        disabled={disabled || busy || blocked}
        className="text-xs h-9"
      >
        {busy ? 'Executing…' : label}
      </Button>
      {message !== null && (
        <p role="status" className="text-[11px] text-muted-foreground">
          {message}
        </p>
      )}
    </div>
  )
}
