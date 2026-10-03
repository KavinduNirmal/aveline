# The composed cross-surface E2E stack (gap E1)

This is the one test that crosses every surface: **React → ASP.NET Core API → Redis → Python
agent service → Redis → API → PostgreSQL**. It exists to close the two `Not met` rows in the
SE3110 report — *Integration / end-to-end testing* and *One integrated workflow test* — and it
is the only suite in `tests/e2e/` that does not stub the API.

- Spec (the walk): `tests/e2e/integration/salon-cross-surface.spec.ts`
- Spec (the finding): `tests/e2e/integration/api-key-conversation-gap.spec.ts`
- Stub identity provider: `tests/e2e/fixtures/stub_oidc_issuer.py`
- Boot / seed / run: `scripts/e2e-composed-stack.sh`, `scripts/seed-e2e-composed-stack.sh`

## What is real and what is not

| Piece | Real? | Notes |
|---|---|---|
| PostgreSQL | **Real** | the dockerised `postgres` service (`pgvector/pgvector:pg16-bookworm`), migrated by the API's own `MigrateAsync()` |
| Redis | **Real** | the dockerised `redis` service, the same pub/sub bus the production path uses |
| ASP.NET Core API | **Real** | a real `dotnet` process, its real middleware pipeline, real EF Core, real authorization |
| JWT validation | **Real** | real `JwtBearer`: discovery, JWKS fetch, RS256 signature, issuer and lifetime |
| Membership resolution | **Real** | real `Users` / `Organizations` / `OrganizationMemberships` rows |
| API → agent hop | **Real** | a real HTTP request to `POST /agents/query` with the real `X-Internal-Token` |
| Python agent service | **Real** | a real `uvicorn` process running the real LangGraph concierge workflow |
| Message persistence | **Real** | the agent publishes `message.created` on Redis; the API's subscriber writes the row |
| Web app (browser leg) | **Real** | the real Vite app, loaded in Chromium, asserted to have mounted and rendered |
| Identity provider | **Stub** | `tests/e2e/fixtures/stub_oidc_issuer.py` — see *Authentication* below |

Nothing about the system under test is stubbed: there is no MSW, no `page.route`, no fake agent
and no in-memory database anywhere in `tests/e2e/integration/`.

## Authentication: what was decided, and why

The API validates bearer tokens for real against `Clerk:Authority`
(`Aveline.Api/Configurations/AuthenticationConfiguration.cs`). There is no way to reach a hosted
Clerk instance from CI without a human clicking through its sign-in page, so the identity
provider is replaced by a **stub OIDC issuer** and pointed at with
`Clerk__Authority=<stub>` + `Clerk__RequireHttpsMetadata=false`. This is the same approach
`Aveline.Api.Tests/StubServers.cs` already takes (`StubAuthServer`); the fixture here is that
pattern without the `dotnet test` host around it.

The identity provider is not the system under test. Everything the API does with the token —
discovery, JWKS, signature, issuer, lifetime, role normalisation, membership lookup, tenant
scoping — stays real.

### The API-key option does not work: a recorded contract gap

The plan offered `X-Api-Key` as the cheap alternative for the API leg. **It does not work
unmodified for the conversation routes**, and this was confirmed against the running stack, not
inferred:

| Request | Result |
|---|---|
| `GET /api/v1/orgs/{org}/conversations` with the bearer token | **200** |
| the same with a real `X-Api-Key` carrying `conversations:view` | **401** |
| the same key against `GET /api/v1/orgs/{org}/catalog/items` (no `catalog:view` scope) | **403** |
| a second key carrying `catalog:view` against `/catalog/items` | **200** |
| that second key against `/conversations` | **403** |

Reading the three results together: the key **passes authentication** and **passes the route's
authorization policy** (the 403s prove the scheme is evaluated and the scope is consulted;
`OrganizationScopeAuthorizationHandler` has an explicit API-key branch that succeeds on the
`scope` claims). It then fails inside the handler, because
`ConversationEndpoints.ResolveUserIdAsync` reads `ClaimTypes.NameIdentifier` / `sub` and looks
that value up as a **Clerk id** in `Users`, while `ApiKeyAuthenticationHandler` sets
`ClaimTypes.NameIdentifier = "apikey:{id}"`. That value is never a `Users.ClerkId`, so it
resolves to null and the endpoint answers `401`:

```csharp
// ConversationEndpoints.cs
var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
if (string.IsNullOrEmpty(clerkId)) return null;
var dbUser = await users.GetByClerkIdAsync(clerkId, cancellationToken);  // null for "apikey:{id}"
return dbUser?.Id;                                                        // -> null -> 401
```

**Which scopes an `X-Api-Key` principal does satisfy:** all of them, for routes that do not need
an acting *user*. The scheme mints `api_key_id`, `api_key_org`, `api_key_prefix` and one `scope`
claim per grant; `OrganizationScopeAuthorizationHandler` accepts that principal for any
`OrganizationScopeRequirement`, but the `scope` list must contain the required permission or the
answer is 403 (e.g. `catalog:view` satisfies `BoutiqueAccessPolicy`; `conversations:view`
satisfies `BoutiqueConversationAccessPolicy`). What no API key can satisfy is a route that then
asks *which person* is calling — which is the whole conversation module. `apikey:{id}` is a
non-null NameIdentifier, so the guard passes and the resolution fails, and the honest status is
401 rather than 403.

`tests/e2e/integration/api-key-conversation-gap.spec.ts` pins this. If the endpoint is ever
taught to resolve an API-key principal to an acting user, that assertion flips and the gap closes
deliberately. The walk itself uses the bearer token.

### The browser leg, stated plainly

* **Browser-driven:** loading the real Vite app from `E2E_BASE_URL` in Chromium and asserting the
  SPA booted — the landing page's own React dialog renders, it can be dismissed, the hero heading
  appears, and `#root` is non-empty. That is the honest limit: every `/app/**` route sits inside
  `ProtectedRoute` and needs a real Clerk **frontend** session, which a stub OIDC issuer cannot
  provide (the Clerk JS SDK validates against the real Frontend API, not against the JWKS the API
  trusts). Faking that session would test a mock, so the spec additionally asserts that `/app`
  really does redirect to `/sign-in`.
* **HTTP-driven, against the real API:** the authenticated workflow runs over Playwright's
  `APIRequestContext` with a real RS256 bearer token. This is still cross-surface — the request
  enters the API process, which makes the real network hop to the Python agent.

## Environment variables

The runner writes these to `test-results/e2e-composed-stack/stack.env`; the spec reads them.

| Variable | Meaning |
|---|---|
| `E2E_BASE_URL` | **web origin** — the real Vite app (e.g. `http://127.0.0.1:5173`). One of the two self-skip gates. |
| `E2E_API_BASE_URL` | **API origin** — the real ASP.NET Core app (e.g. `http://127.0.0.1:5091`). The other self-skip gate. |
| `E2E_IDP_BASE_URL` | the stub issuer origin; the spec mints its token at `{E2E_IDP_BASE_URL}/token` |
| `E2E_STATE_FILE` | path to the seeder's JSON (carries `organizationId`, `customerId`, `bearerToken`, `apiKey`, `clerkId`) |
| `E2E_CLERK_SUB` | the Clerk-shaped `sub` to mint for; must match the seeded `Users.ClerkId` |
| `E2E_ORG_ID`, `E2E_CUSTOMER_ID`, `E2E_USER_ID` | fallbacks when no state file is given |
| `E2E_API_KEY` | an `X-Api-Key` secret with `conversations:view`, for the gap spec only |

Two origins on purpose: the plan's `E2E_BASE_URL` names the web origin, and quietly pointing the
API leg at the web origin would be a wrong-target bug rather than a loud skip. The spec is
**skipped** — never passed — unless both `E2E_BASE_URL` and `E2E_API_BASE_URL` are set.

## Seeding

`scripts/seed-e2e-composed-stack.sh` is idempotent and safe to run twice. It establishes, in
order:

1. **The `Users` row** — direct SQL (`insert … on conflict ("ClerkId") do nothing`). There is no
   API path that creates a user without a Clerk webhook signature: `POST /api/v1/webhooks/clerk`
   is Svix-signed and fails closed with 503 when `Clerk:WebhookSecret` is absent. The INSERT is
   derived from the real entity configuration
   (`Aveline.Api/Infrastructure/Data/Configurations/UserConfiguration.cs`) and the migrated table
   (`\d "Users"`), not guessed.
2. **The `Organizations` row and the owner's membership** — through the **real API**:
   `POST /api/v1/orgs` with the minted bearer token. The route is only
   `.RequireAuthorization()` (any authenticated caller) and
   `OrganizationService.CreateOrganizationAsync` creates the `OrganizationMemberships` row with
   `org:boutique_owner` / `Active`. It answers 404 when the `Users` row is missing, which is why
   step 1 comes first.
3. **The `Customers` row** — direct SQL, for the same reason as step 1 (no route creates a
   customer without a tenant session).
4. **An `X-Api-Key` credential** — direct SQL, because `POST …/api-keys` is entitlement-gated
   (`api.access`, Rose/Enterprise) and answers 403 on a Seed-plan organisation. The row is
   inserted with the exact shape and hashing the API's own `ApiKeyCredentials` uses
   (`avl_test_<32 base62>`, prefix = first 16 chars, hash = lowercase hex SHA-256). Set
   `E2E_SEED_API_KEY=0` to skip it.

Output: `test-results/e2e-composed-stack/e2e-composed-stack.json` (mode 0600; it carries secrets)
and a redacted copy of the same object on stdout. The plaintext API key is kept across re-runs in
`test-results/e2e-composed-stack/e2e-api-keys.json` (mode 0600).

## Running it

```bash
# 1. bring up Postgres, Redis, the stub issuer, the API and the agent
scripts/e2e-composed-stack.sh up

# 2. seed (re-runnable)
scripts/e2e-composed-stack.sh seed

# 3. the real web app (only the browser leg needs it)
scripts/e2e-composed-stack.sh web

# 4. the walk
cd frontend/web
set -a; . ../../test-results/e2e-composed-stack/stack.env; set +a
E2E_BASE_URL="$E2E_WEB_BASE_URL" E2E_API_BASE_URL="$E2E_API_BASE_URL" \
E2E_IDP_BASE_URL="$E2E_IDP_BASE_URL" \
E2E_STATE_FILE="$E2E_STATE_DIR/e2e-composed-stack.json" \
  bun run test:e2e -- tests/e2e/integration/

# 5. stop everything (idempotent; safe on partial state)
scripts/e2e-composed-stack.sh down
```

`scripts/e2e-composed-stack.sh run` does `up → seed → web → Playwright` in one command for local
use, and leaves the stack up afterwards. Playwright browsers come from
`bun run test:e2e:install`; do not invoke `playwright test` directly (the package script sets
`NODE_PATH` and `PLAYWRIGHT_BROWSERS_PATH`).

### Options

| Variable | Default | Purpose |
|---|---|---|
| `E2E_STATE_DIR` | `test-results/e2e-composed-stack` | state, logs, seed output |
| `E2E_API_PORT` / `E2E_AGENT_PORT` / `E2E_WEB_PORT` | 5091 / 8000 / 5173 | host ports |
| `E2E_CLERK_SUB` | `user_e2e_cross_surface` | token subject / seeded Clerk id |
| `E2E_INTERNAL_TOKEN` | `.env`'s `INTERNAL_API_TOKEN`, else generated | shared API↔agent secret; must not be a weak value (see below) |
| `E2E_WAIT_TIMEOUT` | 180 | per-service wait, in seconds |
| `E2E_SKIP_BUILD` | 0 | set to 1 to reuse an existing API build |
| `E2E_DOWN_INFRA` | 0 | set to 1 so `down` also stops the docker containers |

`up` also works with **no repo `.env`**: it generates and exports `POSTGRES_PASSWORD`,
`POSTGRES_EXPORTER_PASSWORD`, `METRICS_SCRAPE_TOKEN` and `CREDENTIALS_ENCRYPTION_KEY` (all four
are `${VAR:?}` in `docker-compose.yml`, which compose resolves even when only
`postgres`/`redis` are selected).

### Two boot requirements that bite

* **The internal token.** The API sends `X-Internal-Token` carrying `AgentService:InternalToken`;
  the agent reads the same header and **refuses weak values**
  (`_WEAK_INTERNAL_TOKENS` in `agent-service/app/core/config.py` lists `change-me` and
  `change-me-internal-token`). `appsettings.Development.json` ships
  `change-me-internal-token`, so an explicit override is mandatory on both sides: the runner sets
  `AgentService__InternalToken` and `INTERNAL_API_TOKEN` to exactly the same strong value. The
  default it generates is `aveline-e2e-internal-token-<16 hex>`, which is not in the weak list.
* **The agent needs `DATABASE_URL`.** The agent's readiness check and checkpointer paths use
  `app/core/config.py`'s `database_url` (default `""`, i.e. unreachable). Without it
  `/health/ready` is 503, which then makes the API's own `/health/ready` report
  `agent-service: Unhealthy`. The runner passes
  `DATABASE_URL=postgresql+asyncpg://<user>:<pass>@<host>:<port>/<db>` — the same Postgres the API
  uses. `/health/live` is the only health route that is unconditionally 200; `/health` and
  `/health/ready` run every registered check, which is what a CI health gate should poll.
* `Media__Provider=database` is forced for the stack: the composed local stack has no Cloudinary
  credential and the walk attaches nothing, while a shell that inherits `.env`'s
  `Media__Provider=cloudinary` makes the API refuse to boot without `Media__SigningKey`.

## The API<->agent seam is exercised end to end

The walk passes with the agent on the deterministic rule-based path
(`AGENT_LLM_ENABLED=false`), which is what makes it fast and reproducible. Both directions of the
internal seam are now real and observed in `logs/agent.log`:

| Endpoint the agent calls | Result |
|---|---|
| `GET /internal/conversations/{id}/messages` | **200** — the workflow reads a real transcript window ("no transcript window" warnings: 0) |
| `POST /internal/usage/record` | **201** |
| `POST /internal/agent-runs` | **200** / **201** |
| `GET /internal/customers/{id}/brief`, `/consent` | **200** |
| `POST /internal/customers/memories/search` | **200** |
| `POST /internal/visual/inventory/search` | **200** |

`API_BASE_URL` must be the API **root** (`http://127.0.0.1:5091`), matching `docker-compose.yml`.
The internal routes are mapped at the root, not under the `/api/v1` group, so a `/api/v1` suffix
makes every internal call land on an unmapped path under the versioned group, where the group's
authorization answers **401** rather than 404. That bit during development: with the suffix, the
agent logged "Failed to load conversation history; continuing without a transcript window" and
silently dropped usage/run telemetry while the walk still passed. That is exactly the kind of
degradation a cross-surface walk is supposed to expose, so the runner asserts the correct root
value and this table is the evidence that the seam is live.

### Two residual, unrelated issues observed (not covered by the walk)

* **`GET /api/v1/orgs/{org}/catalog/suppliers` still 401s.** `ToolRegistry.get_suppliers`
  (`agent-service/app/tools/registry.py:520`) picks a *staff* route whenever an `org_id` is
  present and sends the internal token, which that route does not accept. The tool catches the
  error and returns `[]`, so the visual agent silently loses partner-atelier scraping. The
  internal alternative exists (`/internal/visual/suppliers`); the path choice, not the transport,
  is the defect. It is the only 401 left in the agent log and it does not affect any assertion in
  this walk.
* **`POST /internal/customers/memories/search` 500s when the embeddings key is absent.** With
  `Embeddings__ApiKey` unset the handler throws `Embeddings:ApiKey is not configured` and the
  agent logs "Semantic memory retrieval failed; continuing without context". The runner wires
  `Embeddings__ApiKey`/`BaseUrl`/`Model` through from the environment (matching compose), so a
  key-carrying machine gets the real retrieval path.
* **Missing daily telemetry partition.** `ApiRequestLogs` is partitioned by day; the migration
  creates today + tomorrow and `ApiRequestLogPartitionJob` adds tomorrow once a day at 00:05 UTC.
  A database that outlives a day therefore has no partition for the current date and every
  request's telemetry write raises `23514: no partition of relation "ApiRequestLogs" found for
  row` (surfaced as a 500 by some handlers). The seeder calls the migration's own idempotent
  `aveline_ensure_api_request_log_partition(today)` so a long-lived local volume cannot turn into
  a false failure. CI starts from a fresh database, where the migration already covers today.

## CI

`up` leaves the background processes running and writes:

* `test-results/e2e-composed-stack/stack.env` — the origins and Postgres values for the next step
* `test-results/e2e-composed-stack/idp.json` — the stub issuer's origin and port
* `test-results/e2e-composed-stack/e2e-composed-stack.json` — the seed
* `test-results/e2e-composed-stack/logs/{api,agent,web,stub-issuer,docker-compose-up,dotnet-build}.log`

Upload `test-results/e2e-composed-stack/` on failure: the API and agent logs are what distinguish
"the agent never answered" from "the answer never persisted".
