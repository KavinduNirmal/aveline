import type { AccountState } from '@/types/user'
import axios, { type AxiosInstance } from 'axios'

import { toApiError } from './api-error'
import { apiBaseUrl } from './env'

/** Returns the current Clerk JWT, or `null` when signed out. */
export type TokenGetter = () => Promise<string | null>

let tokenGetter: TokenGetter | null = null
let onUnauthorized: (() => void) | null = null
let onForbidden: ((error: unknown) => void) | null = null
let onOnboardingStatusChanged: ((isOnboarded: boolean) => void) | null = null
let onAccountStateChanged: ((accountState: AccountState) => void) | null = null

/** Registers the Clerk token getter (mounted inside the ClerkProvider). */
export function registerAuthTokenGetter(getter: TokenGetter | null): void {
  tokenGetter = getter
}

/** Registers the handler invoked when the API rejects with 401. */
export function registerUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler
}

/** Registers the handler invoked when the API rejects with 403. */
export function registerForbiddenHandler(
  handler: ((error: unknown) => void) | null,
): void {
  onForbidden = handler
}

/** Registers the handler invoked when X-Completed-Onboarding header is detected. */
export function registerOnboardingStatusHandler(
  handler: ((isOnboarded: boolean) => void) | null,
): void {
  onOnboardingStatusChanged = handler
}

/** Registers the handler invoked when the X-Account-State header is detected. */
export function registerAccountStateHandler(
  handler: ((accountState: AccountState) => void) | null,
): void {
  onAccountStateChanged = handler
}

const ACCOUNT_STATES: AccountState[] = ['OnboardingPending', 'Active', 'Suspended']

function isAccountState(value: string | undefined): value is AccountState {
  return value != null && ACCOUNT_STATES.includes(value as AccountState)
}

export function createApiClient(baseUrl: string): AxiosInstance {
  const client = axios.create({
    baseURL: baseUrl,
    headers: { 'Content-Type': 'application/json' },
  })

  // Attach the Clerk JWT to every request.
  client.interceptors.request.use(async (config) => {
    if (tokenGetter) {
      const token = await tokenGetter()
      if (token) {
        config.headers.Authorization = `Bearer ${token}`
      }
    }
    return config
  })

  // Surface typed errors, extract onboarding headers, and react to 401 / 403.
  client.interceptors.response.use(
    (response) => {
      const onboardingHeader =
        response.headers['x-completed-onboarding'] ??
        response.headers['X-Completed-Onboarding']
      if (typeof onboardingHeader === 'string') {
        onOnboardingStatusChanged?.(onboardingHeader.toLowerCase() === 'true')
      }

      const accountStateHeader =
        response.headers['x-account-state'] ??
        response.headers['X-Account-State']
      if (isAccountState(accountStateHeader)) {
        onAccountStateChanged?.(accountStateHeader)
      }
      return response
    },
    (error: unknown) => {
      const apiError = toApiError(error)
      if (apiError.status === 401) {
        onUnauthorized?.()
      } else if (apiError.status === 403) {
        onForbidden?.(apiError)
      }
      return Promise.reject(apiError)
    },
  )

  return client
}

/** Shared Aveline API client (see the README for the base URL config). */
export const apiClient = createApiClient(apiBaseUrl)
