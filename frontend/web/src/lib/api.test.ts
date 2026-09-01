import {
  AxiosError,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios'
import { afterEach, describe, expect, it } from 'vitest'

import {
  createApiClient,
  registerAuthTokenGetter,
  registerForbiddenHandler,
  registerUnauthorizedHandler,
} from './api'
import { ApiError } from './api-error'

function ok(config: InternalAxiosRequestConfig): AxiosResponse {
  return { status: 200, statusText: 'OK', headers: {}, config, data: { ok: true } }
}

function httpError(status: number, config: InternalAxiosRequestConfig): AxiosError {
  const response: AxiosResponse = {
    status,
    statusText: '',
    headers: {},
    config,
    data: { message: 'error' },
  }
  return new AxiosError(
    `Request failed with status code ${status}`,
    String(status),
    config,
    null,
    response,
  )
}

function buildClient(): ReturnType<typeof createApiClient> {
  const client = createApiClient('http://api.test')
  return client
}

afterEach(() => {
  registerAuthTokenGetter(null)
  registerUnauthorizedHandler(null)
  registerForbiddenHandler(null)
})

describe('request interceptor', () => {
  it('attaches the Clerk JWT as a Bearer token', async () => {
    registerAuthTokenGetter(() => Promise.resolve('tok-1'))
    const client = buildClient()
    const captured: (string | undefined)[] = []

    client.defaults.adapter = async (config) => {
      captured.push(config.headers.Authorization as string | undefined)
      return ok(config)
    }

    await client.get('/ping')

    expect(captured).toEqual(['Bearer tok-1'])
  })

  it('adds no Authorization header when signed out', async () => {
    registerAuthTokenGetter(() => Promise.resolve(null))
    const client = buildClient()
    const captured: (string | undefined)[] = []

    client.defaults.adapter = async (config) => {
      captured.push(config.headers.Authorization as string | undefined)
      return ok(config)
    }

    await client.get('/ping')

    expect(captured).toEqual([undefined])
  })
})

describe('response interceptor', () => {
  it('rejects 401 with ApiError and triggers the unauthorized handler', async () => {
    registerUnauthorizedHandler(() => {
      void 0
    })
    const client = buildClient()
    let unauthorizedCalls = 0
    registerUnauthorizedHandler(() => {
      unauthorizedCalls++
    })
    client.defaults.adapter = async (config) => {
      throw httpError(401, config)
    }

    const error = (await client.get('/ping').catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(401)
    expect(unauthorizedCalls).toBe(1)
  })

  it('rejects 403 with ApiError and triggers the forbidden handler', async () => {
    const client = buildClient()
    const forbidden: { error?: ApiError } = {}
    registerForbiddenHandler((error: unknown) => {
      forbidden.error = error as ApiError
    })
    client.defaults.adapter = async (config) => {
      throw httpError(403, config)
    }

    const error = (await client.get('/ping').catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(403)
    expect(forbidden.error).toBeDefined()
    expect(forbidden.error?.status).toBe(403)
  })

  it('does not trigger handlers for other status codes', async () => {
    const client = buildClient()
    let unauthorizedCalls = 0
    let forbiddenCalls = 0
    registerUnauthorizedHandler(() => {
      unauthorizedCalls++
    })
    registerForbiddenHandler(() => {
      forbiddenCalls++
    })
    client.defaults.adapter = async (config) => {
      throw httpError(500, config)
    }

    const error = (await client.get('/ping').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(500)
    expect(unauthorizedCalls).toBe(0)
    expect(forbiddenCalls).toBe(0)
  })

  it('resolves successful responses normally', async () => {
    const client = buildClient()
    client.defaults.adapter = async (config) => ok(config)

    const response = await client.get('/ping')

    expect(response.status).toBe(200)
    expect(response.data).toEqual({ ok: true })
  })
})
