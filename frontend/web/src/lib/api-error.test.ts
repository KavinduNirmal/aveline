import { describe, expect, it } from 'vitest'

import { ApiError, isCanceledError, toApiError } from './api-error'

describe('ApiError', () => {
  it('carries status, message, code and details', () => {
    const error = new ApiError(403, 'No thanks', 'ERR_HTTP_REQUEST_FAILED', {
      reason: 'nope',
    })

    expect(error).toBeInstanceOf(Error)
    expect(error.name).toBe('ApiError')
    expect(error.status).toBe(403)
    expect(error.message).toBe('No thanks')
    expect(error.code).toBe('ERR_HTTP_REQUEST_FAILED')
    expect(error.details).toEqual({ reason: 'nope' })
  })
})

describe('toApiError', () => {
  it('passes ApiError through unchanged', () => {
    const original = new ApiError(500, 'boom')
    expect(toApiError(original)).toBe(original)
  })

  it('maps an axios 401 to a friendly session message', () => {
    const error = toApiError({ isAxiosError: true, response: { status: 401 } })
    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(401)
    expect(error.message).toContain('session has expired')
  })

  it('maps an axios 403 to a friendly permission message', () => {
    const error = toApiError({ isAxiosError: true, response: { status: 403 } })
    expect(error.status).toBe(403)
    expect(error.message).toContain("don't have permission")
  })

  it('prefers the server-provided message over the fallback', () => {
    const error = toApiError({
      isAxiosError: true,
      response: { status: 422, data: { message: 'Email already in use' } },
    })
    expect(error.status).toBe(422)
    expect(error.message).toBe('Email already in use')
  })

  it('extracts the RFC 7807 problem type from the response body', () => {
    const error = toApiError({
      isAxiosError: true,
      response: {
        status: 403,
        data: {
          type: 'https://aveline.app/errors/account-suspended',
          title: 'Account Suspended',
          detail: 'This account is suspended.',
        },
      },
    })
    expect(error.type).toBe('https://aveline.app/errors/account-suspended')
    expect(error.message).toContain('This account is suspended.')
  })

  it('leaves the problem type undefined when the body has none', () => {
    const error = toApiError({
      isAxiosError: true,
      response: { status: 403, data: { message: 'forbidden' } },
    })
    expect(error.type).toBeUndefined()
  })

  it('maps a network failure (no response) to a connection message', () => {
    const error = toApiError({ isAxiosError: true, response: undefined })
    expect(error.status).toBe(0)
    expect(error.message).toContain('Unable to reach the server')
  })

  it('maps a server 500 to a generic message', () => {
    const error = toApiError({ isAxiosError: true, response: { status: 500 } })
    expect(error.status).toBe(500)
    expect(error.message).toContain('Something went wrong')
  })

  it('maps a plain Error', () => {
    const error = toApiError(new Error('kaput'))
    expect(error.status).toBe(0)
    expect(error.message).toBe('kaput')
  })
})

describe('isCanceledError', () => {
  it('recognizes an aborted axios request by its code', () => {
    // What the request interceptor becomes once `AbortController.abort()` fires: axios throws a
    // CanceledError and `toApiError` carries `ERR_CANCELED` through as `code`.
    expect(isCanceledError(new ApiError(0, 'canceled', 'ERR_CANCELED'))).toBe(true)
  })

  it('recognizes a CanceledError that never reached the interceptor', () => {
    const canceled = new Error('canceled')
    canceled.name = 'CanceledError'
    expect(isCanceledError(canceled)).toBe(true)
  })

  it('does not claim an AbortError, which this client never produces', () => {
    // `AbortError` is native `fetch` vocabulary. The panels call axios, which reports an abort as a
    // `CanceledError`; recognizing a name the client cannot emit would be untested code dressed as
    // robustness, so the contract stops at the two shapes axios actually throws.
    const aborted = new Error('The operation was aborted.')
    aborted.name = 'AbortError'
    expect(isCanceledError(aborted)).toBe(false)
  })

  it('does not mistake a real failure for a cancellation', () => {
    expect(isCanceledError(new ApiError(500, 'Something went wrong'))).toBe(false)
    expect(isCanceledError(new ApiError(0, 'Unable to reach the server'))).toBe(false)
    expect(isCanceledError(new Error('kaput'))).toBe(false)
    expect(isCanceledError(null)).toBe(false)
    expect(isCanceledError('canceled')).toBe(false)
  })
})
