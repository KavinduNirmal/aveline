import { describe, expect, it } from 'vitest'

import { describeError, extractTraceId } from './errors'

/**
 * A `500` from the API returns `{ status, message, traceId }`. The `traceId` is the support handle
 * — it is the only thing that correlates a user-visible failure with the server log, because the
 * exception itself is never serialised.
 */
describe('extractTraceId', () => {
  it('reads the traceId field the API returns', () => {
    expect(extractTraceId({ status: 500, traceId: 'trace-abc' })).toBe('trace-abc')
  })

  it('falls back to the correlation headers', () => {
    expect(extractTraceId({ status: 500, headers: { 'x-request-id': 'req-1' } })).toBe('req-1')
    expect(extractTraceId({ status: 500, headers: { 'x-trace-id': 'trace-2' } })).toBe('trace-2')
  })

  it('returns null when the proxy stripped every handle', () => {
    expect(extractTraceId({ status: 500 })).toBeNull()
    expect(extractTraceId(new Error('boom'))).toBeNull()
    expect(extractTraceId(null)).toBeNull()
  })
})

describe('describeError', () => {
  it('names the status and keeps the server message', () => {
    const description = describeError({ status: 404, message: 'No such user' })
    expect(description.message).toBe('No such user')
    expect(description.status).toBe(404)
  })

  it('renders a stable fallback rather than "undefined"', () => {
    expect(describeError(new Error('network down')).message).toBe('network down')
    expect(describeError(null).message).toBe('Something went wrong.')
  })

  it('carries the traceId when there is one', () => {
    expect(describeError({ status: 500, traceId: 'trace-9' }).traceId).toBe('trace-9')
  })
})
