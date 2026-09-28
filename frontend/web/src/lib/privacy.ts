/**
 * The customer-facing opt-out flow (privacy plan §5.2, Phase 7 item 7.5).
 *
 * This module is the one place the browser speaks to the two anonymous privacy routes. Three rules
 * are encoded here rather than left to the page:
 *
 * 1. **The signed link is read, never constructed.** The disclosure builds
 *    `/privacy/opt-out?o={organizationId}&v={version}&s={hmac}`; the page only parses those three
 *    values and echoes them back. It cannot compute the HMAC and must not try to - the server
 *    re-verifies the signature on both calls (`PrivacyLinkSigner`).
 * 2. **The code is bound to the handle the server minted.** `start` returns an opaque `handle`; the
 *    code is only verifiable against it, so the handle travels back on `verify`. There is no phone
 *    number in the verify body - the server revokes the number the code *proved*.
 * 3. **Anti-enumeration is a UI contract too.** `start` answers the same `202` whether or not the
 *    phone exists, and this module reports only that constant answer. Nothing here may branch on
 *    whether a customer was found, and no error copy may say "unknown number" or "no record".
 */

import { ApiError } from '@/lib/api-error'
import { apiClient } from '@/lib/api'

/** The two scope literals the API validates (`PrivacyEndpoints.ScopeOrg` / `ScopeAll`). */
export const OPT_OUT_SCOPES = ['org', 'all'] as const

export type OptOutScope = (typeof OPT_OUT_SCOPES)[number]

/** The query values of the signed opt-out link: `o`, `v` and `s`. */
export interface PrivacyLink {
  organizationId: string
  version: string
  signature: string
}

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Reads `o`, `v` and `s` out of a query string.
 *
 * Returns `null` when any part is missing or malformed, so a page opened without a link (or with a
 * mangled one) refuses before it makes a request. The page cannot check the HMAC - only the server
 * holds the key - so a well-formed but forged link is rejected by the server, not here.
 */
export function parsePrivacyLink(search: string): PrivacyLink | null {
  let params: URLSearchParams
  try {
    params = new URLSearchParams(search)
  } catch {
    return null
  }

  const organizationId = params.get('o')?.trim() ?? ''
  const version = params.get('v')?.trim() ?? ''
  const signature = params.get('s')?.trim() ?? ''

  if (!UUID_PATTERN.test(organizationId) || version === '' || signature === '') {
    return null
  }

  return { organizationId, version, signature }
}

/** What `POST /privacy/opt-out/start` reports. `handle` is `null` only on a malformed response. */
export interface OptOutStartResult {
  status: string
  handle: string | null
  /** The server's code lifetime, used for the "expires in N minutes" copy. */
  expiresInSeconds: number | null
}

interface OptOutStartResponse {
  status?: string
  handle?: string
  expiresInSeconds?: number
}

/**
 * Asks the server to send a one-time code. The response is the constant accepted body on every
 * server-side path; a non-2xx is a refusal (bad number, rate limit, outage) and rejects.
 */
export async function startOptOut(
  link: PrivacyLink,
  phoneNumber: string,
  scope: OptOutScope,
  signal?: AbortSignal,
): Promise<OptOutStartResult> {
  const response = await apiClient.post<OptOutStartResponse>(
    '/api/v1/privacy/opt-out/start',
    {
      organizationId: link.organizationId,
      phoneNumber,
      scope,
      version: link.version,
      signature: link.signature,
    },
    { signal },
  )

  const data = response.data ?? {}

  return {
    status: typeof data.status === 'string' ? data.status : 'accepted',
    handle: typeof data.handle === 'string' && data.handle !== '' ? data.handle : null,
    expiresInSeconds:
      typeof data.expiresInSeconds === 'number' && Number.isFinite(data.expiresInSeconds)
        ? data.expiresInSeconds
        : null,
  }
}

export interface OptOutVerifyInput {
  link: PrivacyLink
  /** The opaque reference `start` returned. */
  handle: string
  phoneNumber: string
  otp: string
  scope: OptOutScope
}

/** The server's terminal answer to a verified opt-out. */
export interface OptOutRevocation {
  status: string
  scope: string
  effectiveAtUtc: string
}

/**
 * Verifies the code and revokes consent. `phoneNumber` is carried for the request's own record; the
 * server revokes the number the code proved and ignores any number a caller supplied.
 */
export async function verifyOptOut(
  input: OptOutVerifyInput,
  signal?: AbortSignal,
): Promise<OptOutRevocation> {
  const response = await apiClient.post<OptOutRevocation>(
    '/api/v1/privacy/opt-out/verify',
    {
      organizationId: input.link.organizationId,
      handle: input.handle,
      phoneNumber: input.phoneNumber,
      otp: input.otp,
      scope: input.scope,
      version: input.link.version,
      signature: input.link.signature,
    },
    { signal },
  )

  return response.data
}

/**
 * The sentence describing how long the code stays valid, from the lifetime the server reported.
 * The page must not hardcode five minutes: the TTL is a server parameter.
 */
export function describeCodeLifetime(expiresInSeconds: number | null): string {
  if (expiresInSeconds === null || !Number.isFinite(expiresInSeconds) || expiresInSeconds <= 0) {
    return 'The code expires shortly.'
  }

  const minutes = Math.max(1, Math.round(expiresInSeconds / 60))
  return minutes === 1 ? 'The code expires in 1 minute.' : `The code expires in ${minutes} minutes.`
}

/**
 * The API's own error code.
 *
 * The shared client puts axios's transport code (`ERR_BAD_REQUEST`) on `ApiError.code`, and the
 * API's business code (`otp-invalid`, `otp-rate-limited`, …) in the response body, which the client
 * carries as `ApiError.details`. Read the body first, and fall back to `code` so a directly-built
 * `ApiError` (in a test) still works.
 */
function businessCode(error: ApiError): string | undefined {
  const details = error.details
  if (typeof details === 'object' && details !== null) {
    const code = (details as Record<string, unknown>).code
    if (typeof code === 'string' && code !== '') {
      return code
    }
  }
  return error.code
}

/**
 * What to tell the customer when a call is refused.
 *
 * `otp-invalid` is **one** message on the wire for a wrong code, an expired code, a replayed code
 * and a spent attempt budget (the server's anti-enumeration rule), so it is one message here. No
 * branch may say the number or the customer is unknown.
 */
export function describeOptOutError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (businessCode(error)) {
      case 'otp-invalid':
        return 'That code is not valid, has expired, or was already used. Check the six digits and try again, or start over.'
      case 'otp-rate-limited':
        return 'Too many attempts were made. Wait a few minutes and try again.'
      case 'otp-unavailable':
        return 'The opt-out service is temporarily unavailable. Nothing was changed. Please try again shortly.'
      case 'invalid-phone-number':
        return 'Enter a Sri Lankan mobile number, for example 0771234567.'
      case 'invalid-scope':
        return 'Choose one of the two opt-out options and try again.'
      default:
        break
    }

    if (error.status === 429) {
      return 'Too many attempts were made. Wait a few minutes and try again.'
    }
    if (error.status === 503) {
      return 'The opt-out service is temporarily unavailable. Nothing was changed. Please try again shortly.'
    }

    return error.message
  }

  return 'The request did not complete. Nothing was changed; trying again is safe.'
}
