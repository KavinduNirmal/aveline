import { expect, test, type APIRequestContext } from '@playwright/test'
import { readFileSync } from 'node:fs'

/**
 * ============================================================================
 *  E1 — the cross-surface workflow test (SE3110 gap plan, §4)
 * ============================================================================
 *
 * React -> ASP.NET Core API -> Redis -> Python agent service -> Redis -> API -> PostgreSQL,
 * and back out through the API's own read route. Nothing about the system under test is
 * stubbed: no MSW, no `page.route`, no fake agent, no in-memory database.
 *
 * ## Which leg is browser-driven and which is HTTP-driven (stated plainly)
 *
 * * **Browser-driven:** `the real built web app boots` loads the real Vite app from
 *   `E2E_BASE_URL` in Chromium and asserts the SPA actually mounted React and rendered a real
 *   landing element. That is the honest limit of a browser leg here: every `/app/**` route sits
 *   inside `ProtectedRoute` and needs a real Clerk **frontend** session, which a stub OIDC
 *   issuer cannot mint (the Clerk JS SDK validates against the real Frontend API, not against
 *   the JWKS the API trusts). Faking that session would test a mock, so it is not done.
 * * **HTTP-driven, against the real API:** the authenticated workflow below runs over
 *   Playwright's `APIRequestContext` (`request`), which issues real HTTP requests to
 *   `E2E_API_BASE_URL` with a real RS256 bearer token that the API validates for real. This is
 *   still genuinely cross-surface: the request crosses into the API process, which performs the
 *   real agent hop over the network to the Python service.
 *
 * ## Self-skipping
 *
 * The spec is skipped unless **both** origins are present, so a checkout with no stack up
 * reports "skipped", never a false pass:
 *
 *   E2E_BASE_URL       the web origin (the real Vite app)          e.g. http://127.0.0.1:5173
 *   E2E_API_BASE_URL   the API origin (the real ASP.NET Core app)  e.g. http://127.0.0.1:5091
 *
 * Two variables on purpose: the plan's `E2E_BASE_URL` names the web origin, and pointing the
 * API leg at the web origin would be a silent wrong-target bug rather than a loud skip.
 *
 * `E2E_IDP_BASE_URL` (the stub issuer) and the seed outputs come from the runner; see
 * `docs/tests/e2e-integration.md`. Run it with:
 *
 *   scripts/e2e-composed-stack.sh run
 *   # or, against an already-running stack:
 *   cd frontend/web && E2E_BASE_URL=... E2E_API_BASE_URL=... E2E_IDP_BASE_URL=... \
 *     bun run test:e2e -- tests/e2e/integration/salon-cross-surface.spec.ts
 *
 * The whole walk is slow by nature (a real API cold start, a real agent run, a Redis round
 * trip, and a browser boot), so the timeout is raised **per spec** rather than in the shared
 * config, which is deliberately `expect.timeout: 15_000` for the signed-out suite.
 */

test.describe.configure({ timeout: 180_000 })

const WEB_BASE_URL = process.env.E2E_BASE_URL
const API_BASE_URL = process.env.E2E_API_BASE_URL
const IDP_BASE_URL = process.env.E2E_IDP_BASE_URL
const STATE_FILE = process.env.E2E_STATE_FILE
const CLERK_SUB = process.env.E2E_CLERK_SUB

interface SeedState {
  organizationId: string
  orgSlug: string
  clerkId: string
  userId: string
  customerId: string
  bearerToken?: string
  apiBaseUrl?: string
  idpBaseUrl?: string
}

/**
 * The seeded organisation and its identity. Read from the runner's state file when one is
 * given, otherwise from the individual `E2E_*` variables, so either the composed runner or a
 * hand-exported environment works.
 */
function loadSeed(): SeedState {
  if (STATE_FILE) {
    try {
      return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as SeedState
    } catch (error) {
      throw new Error(
        `E2E_STATE_FILE=${STATE_FILE} could not be read as JSON. Run ` +
          `'scripts/e2e-composed-stack.sh seed' first. (${(error as Error).message})`,
      )
    }
  }

  const organizationId = process.env.E2E_ORG_ID
  const customerId = process.env.E2E_CUSTOMER_ID
  if (!organizationId || !customerId) {
    throw new Error(
      'no seed state: set E2E_STATE_FILE to the seeder output, or both E2E_ORG_ID and E2E_CUSTOMER_ID.',
    )
  }
  return {
    organizationId,
    orgSlug: process.env.E2E_ORG_SLUG ?? 'e2e-cross-surface',
    clerkId: CLERK_SUB ?? 'user_e2e_cross_surface',
    userId: process.env.E2E_USER_ID ?? '',
    customerId,
  }
}

const skipReason = !WEB_BASE_URL
  ? 'needs E2E_BASE_URL (the real web origin) - see the header of this file'
  : !API_BASE_URL
    ? 'needs E2E_API_BASE_URL (the real API origin) - see the header of this file'
    : undefined

test.skip(Boolean(skipReason), skipReason)

/** A real RS256 bearer token, minted by the stub issuer for the seeded Clerk subject. */
async function mintToken(request: APIRequestContext, seed: SeedState): Promise<string> {
  const issuer = IDP_BASE_URL ?? seed.idpBaseUrl
  if (!issuer) {
    throw new Error('no issuer: set E2E_IDP_BASE_URL (or let the seeder state file carry idpBaseUrl).')
  }

  const response = await request.post(`${issuer}/token`, {
    data: { sub: CLERK_SUB ?? seed.clerkId },
    headers: { 'Content-Type': 'application/json' },
  })
  expect(response.status(), `the stub issuer must mint a token for ${CLERK_SUB ?? seed.clerkId}`).toBe(200)

  const body = (await response.json()) as { access_token?: string }
  expect(body.access_token, 'the stub issuer returned no access_token').toBeTruthy()
  return body.access_token as string
}

/** The bearer-authenticated API context every authenticated step uses. */
async function apiFor(
  request: APIRequestContext,
  seed: SeedState,
): Promise<{ token: string; headers: Record<string, string> }> {
  const token = await mintToken(request, seed)
  return { token, headers: { Authorization: `Bearer ${token}` } }
}

/** A stable per-run idempotency key, so a retry of one send cannot write a second message. */
function clientMessageId(): string {
  return crypto.randomUUID()
}

interface MessageDto {
  id: string
  conversationId: string
  authorKind: string
  agentKey: string | null
  kind: string
  contentBlocks: unknown
  createdAt: string
}

interface MessagePage {
  items: MessageDto[]
  total: number
  page: number
  pageSize: number
}

/**
 * Polls the API's own transcript route until an agent-authored message appears.
 *
 * The answer is produced asynchronously: the send triggers the agent, the agent publishes
 * `message.created` on Redis, and this API instance's subscriber persists the row and
 * broadcasts it. Polling the read route is therefore the true assertion that the whole
 * round trip landed - a direct read of the agent's HTTP response would prove only half of it.
 */
async function waitForAgentMessage(
  request: APIRequestContext,
  headers: Record<string, string>,
  conversationId: string,
  options: { timeoutMs?: number } = {},
): Promise<MessageDto> {
  const deadline = Date.now() + (options.timeoutMs ?? 60_000)
  let last: MessagePage | undefined

  while (Date.now() < deadline) {
    const response = await request.get(
      `${API_BASE_URL}/api/v1/orgs/${seedOrg}/conversations/${conversationId}/messages`,
      { headers },
    )
    if (response.status() === 200) {
      last = (await response.json()) as MessagePage
      const fromAgent = last.items.find((item) => item.authorKind !== 'User')
      if (fromAgent) {
        return fromAgent
      }
    }
    await new Promise((resolve) => setTimeout(resolve, 500))
  }

  throw new Error(
    `no agent-authored message appeared in conversation ${conversationId} within the window. ` +
      `Last read: ${JSON.stringify(last ?? null)}. Check the agent log for 'Agent query received'.`,
  )
}

let seed: SeedState
let seedOrg: string

test.beforeAll(() => {
  seed = loadSeed()
  seedOrg = seed.organizationId
})

test.describe('the real web app', () => {
  test('the real built web app boots and React renders the landing surface', async ({ page }) => {
    // The browser leg. Nothing is intercepted; the real Vite dev/build output is loaded.
    const pageErrors: string[] = []
    page.on('pageerror', (error) => pageErrors.push(error.message))

    // The existing signed-out suite asserts the tenant redirect; here the claim is narrower and
    // deterministic: no dashboard chrome is rendered and no tenant data is requested.
    const orgRequests: string[] = []
    page.on('request', (request) => {
      if (request.url().includes('/api/v1/orgs/')) orgRequests.push(request.url())
    })

    const response = await page.goto(WEB_BASE_URL as string, { waitUntil: 'domcontentloaded' })
    expect(response?.status(), 'the web origin must serve the app').toBeLessThan(400)

    // The browser claim, asserted against the rendered markup of the real landing page.
    //
    // The `#root` contents are polled rather than read once, because `main.tsx` wraps the whole
    // tree in `ClerkProvider`: while Clerk's SDK loads from Clerk's own CDN, the app renders a
    // skeleton (and, if the CDN never answers, the skeleton is all there ever is). Waiting for the
    // real hero text is therefore the assertion that the app's own code ran; a single read right
    // after `domcontentloaded` would race that load and pass on the skeleton.
    await expect
      .poll(
        async () => ((await page.locator('#root').innerHTML()) ?? ''),
        { timeout: 60_000, message: 'the real landing page must render into #root' },
      )
      .toContain('remembers, so')

    const rootHtml = await page.locator('#root').innerHTML()
    expect(rootHtml, 'the public nav must be present').toContain('Plans')
    expect(rootHtml, 'the alpha notice must be present').toMatch(/alpha development/i)

    // Report what the Clerk-gated chrome did, as evidence rather than as an assertion.
    const alphaDialog = page.getByRole('dialog', { name: /welcome to the aveline ai preview/i })
    const sawDialog = await alphaDialog.isVisible().catch(() => false)
    if (sawDialog) {
      await alphaDialog.getByRole('button', { name: /understand/i }).click()
      await expect(alphaDialog).toBeHidden()
    }

    // The authenticated surface is genuinely unreachable without a Clerk frontend session. This
    // is asserted as "no dashboard chrome and no tenant request" rather than "redirected to
    // /sign-in": the redirect is Clerk's, and Clerk's SDK is fetched from its own CDN, so its
    // timing is not this stack's to promise (the existing signed-out suite already pins the
    // redirect in the environment where it is deterministic).
    await page.goto(`${WEB_BASE_URL}/app`, { waitUntil: 'domcontentloaded' })
    await expect(page.locator('[aria-label="Switch boutique"]')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Top up' })).toHaveCount(0)
    expect(orgRequests, 'a signed-out visitor must issue no /api/v1/orgs/ request').toEqual([])

    // Clerk's own SDK talks to its CDN; an offline CI runner can surface a load error that is
    // not this app's. Report them as evidence rather than failing the walk on a network hiccup,
    // because the markup assertions above are the actual proof.
    test.info().annotations.push({
      type: 'page-errors',
      description: pageErrors.length ? pageErrors.join(' | ') : 'none',
    })
    test.info().annotations.push({
      type: 'clerk-gated-chrome',
      description: sawDialog
        ? 'the alpha dialog rendered and was dismissed (Clerk finished loading from its CDN)'
        : 'the alpha dialog was not visible - Clerk had not finished loading; the boot assertions above do not depend on it',
    })
  })
})

test.describe('the authenticated workflow over the real API', () => {
  test('a customer message reaches the real agent and the answer is persisted', async ({ request }) => {
    const { headers } = await apiFor(request, seed)

    // --- 1. create the Salon for the seeded customer -------------------------------------
    const createResponse = await request.post(
      `${API_BASE_URL}/api/v1/orgs/${seed.organizationId}/conversations`,
      { headers, data: { customerId: seed.customerId } },
    )
    expect(
      createResponse.status(),
      `POST /conversations must be 200 (check the membership seed and Clerk__Authority). Body: ${await createResponse.text()}`,
    ).toBe(200)

    const conversation = (await createResponse.json()) as { id: string; threadId: string }
    expect(conversation.id).toBeTruthy()

    // --- 2. send the customer's message (this is what triggers the agent) ---------------
    const messageId = clientMessageId()
    const sendResponse = await request.post(
      `${API_BASE_URL}/api/v1/orgs/${seed.organizationId}/conversations/${conversation.id}/messages`,
      { headers, data: { text: 'What do you have in stock right now?', clientMessageId: messageId } },
    )
    expect(sendResponse.status(), `POST /messages must be 200. Body: ${await sendResponse.text()}`).toBe(200)

    const sent = (await sendResponse.json()) as MessageDto
    expect(sent.kind).toBe('Note')
    expect(sent.authorKind).toBe('User')

    // The send is idempotent on clientMessageId: a replay returns the same row, not a second one.
    const replay = await request.post(
      `${API_BASE_URL}/api/v1/orgs/${seed.organizationId}/conversations/${conversation.id}/messages`,
      { headers, data: { text: 'What do you have in stock right now?', clientMessageId: messageId } },
    )
    expect(replay.status()).toBe(200)
    expect(((await replay.json()) as MessageDto).id).toBe(sent.id)

    // --- 3. the agent answered, and the API persisted it ---------------------------------
    const answer = await waitForAgentMessage(request, headers, conversation.id)
    expect(answer.authorKind, 'the answer must be attributed to the agent, not the user').not.toBe('User')
    expect(answer.agentKey, 'the answer must name the persona that produced it').toBeTruthy()
    expect(answer.contentBlocks, 'the answer must carry real content blocks').toBeTruthy()

    // What the agent actually answered, kept in the report so the run is self-evidencing.
    const answerText = JSON.stringify(answer.contentBlocks)
    test.info().annotations.push({
      type: 'agent-answer',
      description: `${answer.agentKey}: ${answerText.length > 300 ? `${answerText.slice(0, 300)}...` : answerText}`,
    })
    // eslint-disable-next-line no-console
    console.log(`[e1] agent answered: agentKey=${answer.agentKey} kind=${answer.kind} blocks=${answerText}`)

    // The transcript now holds both rows, and the read route is the API's own view of the DB.
    // The read is anchored on the sent message (`around=`) and asks for the route's maximum page
    // size: the route pages newest-first, so a bare page 1 stops proving anything once the thread
    // has more rows than `pageSize` - which repeated runs of this spec make true, since it always
    // reuses the seeded customer's one Salon.
    const transcript = (await (
      await request.get(
        `${API_BASE_URL}/api/v1/orgs/${seed.organizationId}/conversations/${conversation.id}` +
          `/messages?around=${sent.id}&pageSize=200`,
        { headers },
      )
    ).json()) as MessagePage
    expect(transcript.total).toBeGreaterThanOrEqual(2)
    expect(transcript.items.map((item) => item.id)).toContain(sent.id)
    expect(transcript.items.map((item) => item.id)).toContain(answer.id)
  })

  test('an unknown conversation is refused, so the walk is scoped to a real tenant', async ({ request }) => {
    const { headers } = await apiFor(request, seed)

    // A random id in the seeded organisation is a 404 from the API, not an empty 200.
    const response = await request.post(
      `${API_BASE_URL}/api/v1/orgs/${seed.organizationId}/conversations/00000000-0000-0000-0000-0000000000ff/messages`,
      { headers, data: { text: 'this conversation does not exist', clientMessageId: clientMessageId() } },
    )
    expect(response.status()).toBe(404)

    // ...and an unauthenticated caller is refused outright, so the 404 above was authorized.
    const anonymous = await request.get(
      `${API_BASE_URL}/api/v1/orgs/${seed.organizationId}/conversations`,
    )
    expect(anonymous.status()).toBe(401)
  })

  // The API-key contract gap is asserted by its own focused spec
  // (`api-key-conversation-gap.spec.ts`) so that this walk stays a passing, always-on proof of
  // the composed stack rather than carrying a second, environment-dependent credential.
})
