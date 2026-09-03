import { describe, expect, it } from 'vitest'

import { ApiError, toApiError } from './api-error'

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
