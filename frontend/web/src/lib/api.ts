import axios from 'axios'

import { apiBaseUrl } from './env'

/**
 * Shared axios client for the Aveline API.
 *
 * The Clerk JWT interceptor (Issue #13) is added here once auth is wired in.
 */
export const apiClient = axios.create({
  baseURL: apiBaseUrl,
  headers: { 'Content-Type': 'application/json' },
})
