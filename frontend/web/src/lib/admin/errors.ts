/**
 * Error description and the support handle.
 *
 * A `500` from the API returns `{ status, message, traceId }` (`GlobalExceptionHandler`), and the
 * same value is mirrored on `X-Request-Id` / `X-Trace-Id`. The exception itself is never
 * serialised, so the `traceId` is the **only** thing that ties a user-visible failure to a server
 * log line — which is why it is rendered with a copy button rather than swallowed.
 */

export interface ErrorDescription {
  status: number | null
  message: string
  traceId: string | null
}

interface ErrorShape {
  status?: unknown
  message?: unknown
  traceId?: unknown
  headers?: Record<string, unknown>
}

function asRecord(value: unknown): ErrorShape {
  return typeof value === 'object' && value !== null ? (value as ErrorShape) : {}
}

export function extractTraceId(error: unknown): string | null {
  const shape = asRecord(error)
  if (typeof shape.traceId === 'string' && shape.traceId.length > 0) return shape.traceId

  const headers = shape.headers
  if (headers !== undefined) {
    for (const key of ['x-request-id', 'X-Request-Id', 'x-trace-id', 'X-Trace-Id']) {
      const value = headers[key]
      if (typeof value === 'string' && value.length > 0) return value
    }
  }

  return null
}

export function describeError(error: unknown): ErrorDescription {
  const shape = asRecord(error)
  const status = typeof shape.status === 'number' ? shape.status : null

  let message: string
  if (typeof shape.message === 'string' && shape.message.length > 0) {
    message = shape.message
  } else if (error instanceof Error && error.message.length > 0) {
    message = error.message
  } else {
    message = 'Something went wrong.'
  }

  return { status, message, traceId: extractTraceId(error) }
}
