import { expect, test } from '@playwright/test'
import { readFileSync } from 'node:fs'

/**
 * ============================================================================
 *  E1 finding — the API-key scheme and the conversation routes disagree
 * ============================================================================
 *
 * A real `X-Api-Key` credential is accepted by the API's authentication scheme and by the
 * route's authorization policy, and is still answered **401** by every route in
 * `ConversationEndpoints`. That is a contract gap between two shipped surfaces, not a test
 * artefact:
 *
 *   * `ApiKeyAuthenticationHandler` mints `ClaimTypes.NameIdentifier = "apikey:{id}"` plus
 *     `api_key_id` / `api_key_org` / `api_key_prefix` / `scope` claims.
 *   * `BoutiqueConversationAccessPolicy` is registered with `AllowBearerOrApiKey` and an
 *     `OrganizationScopeRequirement(conversations:view)`; `OrganizationScopeAuthorizationHandler`
 *     has an explicit API-key branch that reads the `scope` claims, so **authorization succeeds**.
 *   * The handler then calls `ResolveUserIdAsync`, which reads `ClaimTypes.NameIdentifier` (or
 *     `sub`) and looks the value up as a **Clerk id** in `Users`. For an API-key principal that
 *     value is `apikey:{id}`, which is never a `Users.ClerkId`, so it resolves to null and the
 *     endpoint answers 401.
 *
 * Verified directly against the composed stack (recorded in `docs/tests/e2e-integration.md`):
 * the same key with `catalog:view` answers **200** on `/catalog/items` and **403** (wrong scope)
 * on `/conversations`; the same key with `conversations:view` answers **403** on `/catalog/items`
 * and **401** on `/conversations`; and the bearer principal answers **200** on `/conversations`.
 *
 * The spec pins whichever behaviour the API ships. It is a **known-gap** spec: if the endpoint is
 * ever taught to resolve an API-key principal to an acting user (or to accept
 * `api_key_org` without one), the assertion flips and the finding is closed deliberately rather
 * than by drift.
 *
 * Skipped unless `E2E_API_BASE_URL` and a real key are supplied:
 *   E2E_API_KEY   an `X-Api-Key` secret whose scopes include `conversations:view`
 *                 (or point E2E_STATE_FILE at a seeder output that carries `apiKey`)
 */

test.describe.configure({ timeout: 60_000 })

const API_BASE_URL = process.env.E2E_API_BASE_URL
const STATE_FILE = process.env.E2E_STATE_FILE

function apiKeyFromState(): string | undefined {
  if (process.env.E2E_API_KEY) {
    return process.env.E2E_API_KEY
  }
  if (!STATE_FILE) {
    return undefined
  }
  try {
    const state = JSON.parse(readFileSync(STATE_FILE, 'utf8')) as { apiKey?: string }
    return state.apiKey
  } catch {
    return undefined
  }
}

function orgFromState(): string | undefined {
  if (process.env.E2E_ORG_ID) {
    return process.env.E2E_ORG_ID
  }
  if (!STATE_FILE) {
    return undefined
  }
  try {
    const state = JSON.parse(readFileSync(STATE_FILE, 'utf8')) as { organizationId?: string }
    return state.organizationId
  } catch {
    return undefined
  }
}

test.describe('the API-key principal cannot use the conversation routes', () => {
  test.skip(!API_BASE_URL, 'needs E2E_API_BASE_URL (the real API origin)')

  test('an X-Api-Key with conversations:view is authorized by the policy but refused 401 by the handler', async ({
    request,
  }) => {
    const apiKey = apiKeyFromState()
    const organizationId = orgFromState()
    test.skip(
      !apiKey || !organizationId,
      'needs E2E_API_KEY (an X-Api-Key secret with conversations:view) and E2E_ORG_ID/E2E_STATE_FILE',
    )

    const response = await request.get(
      `${API_BASE_URL}/api/v1/orgs/${organizationId}/conversations`,
      { headers: { 'X-Api-Key': apiKey as string } },
    )

    // 401, not 403: the key passed the policy and failed user resolution inside the handler.
    // A 403 here would mean the scope was not granted; a 200 would mean the gap is closed.
    expect(
      response.status(),
      'the API-key scheme is accepted by the conversation route policy but not by ResolveUserIdAsync',
    ).toBe(401)
  })
})
