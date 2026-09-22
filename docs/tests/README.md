# Tests — Aveline

This folder documents the testing strategy, how to run each test suite, and the
coverage tooling for the Aveline platform.

---

## 1. Test Layers

Aveline tests four independent codebases, one per technology stack:

| Layer | Stack | Tools | Location |
|---|---|---|---|
| **Backend API** | ASP.NET Core 10 (C#) | xUnit + Moq + `WebApplicationFactory` + Coverlet | `Aveline.Api.Tests/` |
| **Agent Service** | Python 3.12 + FastAPI + LangGraph | pytest + `pytest-asyncio` + `pytest-cov` + respx + fakeredis | `agnet-service/tests/` |
| **Web Dashboard** | React 19 + TypeScript (Vite) | Vitest + `@vitest/coverage-v8` | `frontend/web/src/**/*.test.ts(x)` |
| **Mobile App** | Flutter (Dart) | `flutter_test` | `frontend/aveline_mobile/test/` |

---

## 2. Backend Tests (`Aveline.Api.Tests/`)

2,298 test cases across unit and integration test suites. Two distinct approaches:

- **Unit tests** — exercise a single class in isolation, with collaborators mocked
  via **Moq** (e.g. `OrganizationServiceTests`, `CredentialEncryptionServiceTests`,
  `VisionServiceTests`, `EventingRedisTests`).
- **Integration tests** — boot the real app with
  `WebApplicationFactory<Program>` against an **in-memory EF Core database**
  (`UseInMemoryDatabase`), with external dependencies replaced by stub servers
  and fake stores (see §6).

Shared test infrastructure:

| File | Purpose |
|---|---|
| `StubServers.cs` | In-process `StubAuthServer` (real OIDC/JWKS discovery on an ephemeral port) and `StubAgentServer` (echoes the internal token), both `IAsyncDisposable` |
| `CapturingHttpMessageHandler.cs` | Captures/asserts outbound HTTP requests made by typed clients |
| `TestInvitationCodeStore.cs` | In-memory fake of the distributed invitation code store |

### Run

```bash
dotnet test Aveline.Api/Aveline.Api.sln
```

Test classes requiring PostgreSQL/pgvector (`CustomerMemoryRepositoryPostgresTests`,
`CustomerConciergeSearchPostgresTests`, `VisualIntelligencePostgresTests`) spin up a disposable
**PostgreSQL container** via `Testcontainers` and run real migrations. These require a Docker daemon
(present on CI's `build-api` ubuntu runners).

### Coverage

```bash
dotnet test Aveline.Api/Aveline.Api.sln --collect:"XPlat Code Coverage"
```

Reports are written to `Aveline.Api.Tests/TestResults/` (gitignored).

### Files (grouped by module)

| Module | Files |
|---|---|
| **Authentication & Authorization** | `JwtValidationTests`, `RoleClaimNormalizerTests`, `AuthorizationPolicyTests`, `AuthenticationConfigurationTests`, `CorsConfigurationTests`, `ServiceToServiceAuthTests`, `FullAuthFlowIntegrationTests` |
| **Organizations & Invitations** | `OrganizationServiceTests`, `OrganizationRepositoryTests`, `OrganizationRecipientResolverTests`, `OrganizationEndpointsIntegrationTests`, `OrganizationAuthorizationIntegrationTests`, `OrganizationInvitationLifecycleTests`, `OrganizationProfileEndpointsIntegrationTests`, `InvitationManagementEndpointsIntegrationTests`, `InvitationAcceptRateLimitIntegrationTests`, `DistributedInvitationCodeStoreTests` |
| **Onboarding** | `OnboardingServiceTests`, `OnboardingMiddlewareTests`, `OnboardingEndpointsIntegrationTests` |
| **Users** | `UserServiceTests`, `UserRepositoryTests`, `UserCacheServiceTests`, `UserEndpointsIntegrationTests` |
| **Commerce / Approvals** | `AdminApprovalFlowIntegrationTests` |
| **Customer Concierge & Memory (Slice 1)** | `CustomerConciergeEntityConfigurationTests`, `CustomerConciergeRepositoryTests`, `CustomerConciergeServiceTests`, `CustomerConciergeEndpointsIntegrationTests`, `CustomerMemoryRepositoryPostgresTests` (Testcontainers), `CustomerConciergeSearchPostgresTests` (Testcontainers) |
| **Visual Intelligence & Sourcing (Slice 2)** | `VisionServiceTests`, `VisualIntelligenceEntityConfigurationTests`, `VisualIntelligencePostgresTests` (Testcontainers), `CustomerMatchRepositoryTests`, `VisualEndpointsIntegrationTests` |
| **Notifications** | `NotificationDispatcherTests`, `NotificationRepositoryTests`, `UserNotificationRepositoryTests`, `ChannelRouterTests`, `EmailServiceTests`, `FcmPushChannelTests`, `LoggingNotificationChannelsTests`, `NotificationHubTests`, `SignalRRealtimeChannelTests`, `NotificationEndpointsIntegrationTests`, `NotificationHubIntegrationTests`, `DeviceTokenEndpointsIntegrationTests`, `DeviceTokenRepositoryTests` |
| **Eventing** | `EventingTests`, `EventingRedisTests` |
| **Integrations** | `IntegrationServiceTests`, `IntegrationEndpointsIntegrationTests` |
| **Usage & Billing** | `UsageTrackerServiceTests`, `UsageEndpointsIntegrationTests` |
| **Security / Resilience** | `CredentialEncryptionServiceTests`, `DistributedRateLimiterTests` |

---

## 3. Python Tests (`agnet-service/tests/`)

381 test functions across agent graphs, tool registries, orchestrators, and schemas.

Approach:

- **Endpoint tests** use `fastapi.testclient.TestClient` against the real FastAPI
  `app` (e.g. `test_internal_auth.py`, `test_agents_warmup.py`).
- **External HTTP** is mocked with `respx` (`test_usage_reporter.py`).
- **Redis** is faked with `fakeredis.aioredis` (`test_event_bus.py`).
- **LangGraph sub-graphs & orchestration** are tested with mock LLMs and simulated tool registries.
- Async tests rely on `pytest-asyncio` with `asyncio_mode = auto`.

### Run

```bash
pytest agnet-service/tests/ -v
```

Configuration lives in `agnet-service/pyproject.toml` (`[tool.pytest.ini_options]`:
`testpaths = ["tests"]`, `pythonpath = ["."]`, `asyncio_mode = "auto"`).

### Files

| File | Covers |
|---|---|
| `test_internal_auth.py` | `/health` public; `/agents/ping` missing/invalid token → 401, valid → 200 + echo; forwarded user context; dependency-level valid/missing/invalid; unconfigured token → 500 |
| `test_agents_warmup.py` | `/agents/warmup` internal-token enforcement (missing/invalid → 401, valid → 200) |
| `test_event_bus.py` | `channel_for`/`pattern_for` channel naming, `EventEnvelope` defaults + snake_case JSON serialization, publish/subscribe over fake Redis |
| `test_usage_reporter.py` | `report_usage` success path and error handling with `respx`-mocked HTTP |
| `test_customer_memory_schemas.py` | Memory-agent Pydantic I/O schemas (intent, memories, events, output) + extra-field rejection |
| `test_customer_memory_agent.py` | Memory sub-graph golden cases against a fake `ToolRegistry` (wedding, revoked consent, missing context, preference extraction) + rule parsing |
| `test_visual_insight_schemas.py` | Visual agent Pydantic schemas (`ImageAttributes`, `PieceItem`, `LookDto`, `SourcingRequestDto`, `VisualAgentOutput`) |
| `test_visual_insight_graph.py` | Visual Insight LangGraph sub-graph execution, conditional look composition vs. sourcing routing |
| `test_visual_intent_gate.py` | Elle visual intent classification and confidence gating |
| `test_visual_routing.py` | Conditional routing after visual agent (commerce transition vs formulate response) |
| `test_inventory_tools.py` | Visual inventory search, stock verification, and image analysis tools |
| `test_visual_tools.py` | Look composition, customer matching, and sourcing request creation tools |
| `test_concierge_workflow.py` | Full multi-agent orchestration (`intent_gate -> customer_resolution -> memory -> visual -> commerce -> formulate`) with LLM commentary and ADR-010 reporting |
| `test_tool_registry.py` | Shared `ToolRegistry`/`InternalApiClient` routing for memory and visual endpoints against a mocked HTTP client |

---

## 4. Web Dashboard Tests (`frontend/web/`)

Vitest across **two projects** declared in one config: a `node` environment for the service layer
(`lib/`, hooks, types) and a `jsdom` environment for component and context rendering, selected by the
`*.dom.test.tsx` filename convention. Heavy dependencies (`@clerk/react`, `sonner`,
`@microsoft/signalr`, `recharts`) are stubbed with `vi.mock` + `vi.hoisted`, and the DOM project
raises the per-test timeout to 20 s because v8 coverage instrumentation plus a 2-core CI runner pushes
the form-driving tests past Vitest's 5 s liveness default.

### Run

```bash
bun run test                      # vitest run
bun run test:coverage             # global run; excludes the admin and tenant subtrees
bun run test:coverage:admin       # the admin subtree only (its own ratchet)
bun run test:coverage:dashboard   # the tenant-dashboard subtree only (its own ratchet)
```

### Three coverage runs, on purpose

The global run deliberately **excludes** `src/routes/**`, `src/components/**` and
`src/contexts/**`, because those subtrees carry their own gates. Each subtree run is
`include`-only — a gate added after the work is a gate that measures nothing — writes to its own
reports directory, and carries a **ratchet** that never lowers: it starts at 0 and is raised to the
value actually achieved (the admin ratchet in R0–R6, the tenant one in T7).

| Run | Measures | Config |
| --- | --- | --- |
| `test:coverage` | the shared layer: `src/lib/**`, `src/hooks/**`, `src/types/**`, `src/*` | `vite.config.ts` |
| `test:coverage:admin` | `src/routes/admin/**`, `src/components/admin/**`, `AdminSessionContext` | `vitest.admin-coverage.config.ts` |
| `test:coverage:dashboard` | `src/components/dashboard/**`, `src/components/shared/**`, the tenant `lib/*-api.ts` modules, `useDashboardWindow` | `vitest.dashboard-coverage.config.ts` |

The dashboard run exists because the tenant surface was previously measured by **nothing**: a file
under `src/components/shared/**` fell outside both the global and the admin denominators. All three
runs execute in the `test-web` CI job. See `docs/frontend/tenant-dashboard.md`.

### Mechanical gates in the web suite

Five test files assert rules a linter cannot: they are the reason the two UIs cannot drift into
fabricated numbers or a broken dark mode.

| File | Enforces |
| --- | --- |
| `test/admin-conformance.test.ts` | the admin tree's brand rules (no raw palette/hex/controls, `gap-*` not `space-*`) |
| `test/admin-truthfulness.test.ts` | the console cannot render an invented number |
| `test/tenant-conformance.test.ts` | the same conformance rule set over the tenant dashboard, with an **empty** allow-list |
| `test/tenant-truthfulness.test.ts` | the tenant tree's four literal truthfulness rules (`null` is not `0`; no "demo mode"; recharts only through the chart wrapper) |
| `test/tenant-sections.test.ts` | the nav table and the router agree about the section list and the section gates |

### Files

| File | Covers |
|---|---|
| `lib/auth.test.ts` | JWT payload decoding, `hasAdminRole` role matrix |
| `lib/api.test.ts`, `lib/api-error.test.ts` | HTTP client wrapper + error handling |
| `lib/organizations.test.ts`, `lib/invitations.test.ts`, `lib/onboarding.test.ts` | Slice-specific API helpers |
| `lib/boutique.test.ts`, `lib/integrations.test.ts`, `lib/permissions.test.ts` | Dashboard settings/integrations/permissions helpers |
| `lib/notifications.test.ts`, `lib/notifications-api.test.ts` | Notification client + API |
| `contexts/UserContext.test.tsx`, `contexts/NotificationsContext.test.tsx` | Context providers (default state + misuse guards) |

---

## 5. Flutter Tests (`frontend/aveline_mobile/`)

A single widget test bootstraps the mobile suite.

| File | Covers |
|---|---|
| `test/widget_test.dart` | `AppTheme.light` builds a `MaterialApp` with the expected color scheme |

### Run

```bash
flutter test            # from frontend/aveline_mobile/
flutter test --coverage # emits coverage/lcov.info
```

---

## 6. Integration Test — Full Auth Flow (`FullAuthFlowIntegrationTests`)

Simulates the mandatory cross-platform flow **without external dependencies**:

```
Clerk-style JWT  →  Aveline.Api (real JwtBearer + JWKS pipeline)
                 →  agent service (internal X-Internal-Token)  →  response
```

- A **stub OIDC/JWKS server** (real Kestrel on an ephemeral port) serves the discovery
  document + a test JWKS, so `AddJwtBearer` runs its full discovery → signature →
  issuer → lifetime pipeline offline.
- A **stub agent server** records the `X-Internal-Token` header and echoes the payload,
  mirroring `agnet-service/app/api/agents.py`.
- The API is booted via `WebApplicationFactory<Program>` with `Clerk:Authority`,
  `AgentService:BaseUrl`, and `AgentService:InternalToken` overridden.

Cases:

| Case | Expectation |
|---|---|
| No token | 401 |
| Invalid (garbage) token | 401 |
| Valid token (roles) → `/api/v1/agents/ping` | 200; agent received the internal token; identity (`userId`, `roles`) propagated in the body |
| Valid token, no roles | 403; agent not called |

Other integration suites (`*EndpointsIntegrationTests`) follow the same pattern:
in-memory EF Core DB seeded per test, external HTTP via stub servers or
`CapturingHttpMessageHandler`, Redis via Moq.

---

## 7. End-to-End Tests (`tests/e2e/`)

Playwright walks the two authenticated UI trees in a real browser, from the repository-level
`tests/e2e/` tree rather than inside the web package. Each spec covers the **signed-out** path — the
one walk buildable without a Clerk test session — and asserts both the redirect and the absence of
that tree's own API traffic.

| Spec | Asserts |
|---|---|
| `admin-console/console-access.spec.ts` | signed out, `/admin/{userId}` reaches `/sign-in`, renders no console chrome, and issues **zero** `/api/v1/admin/` requests |
| `tenant-dashboard/signed-out.spec.ts` | signed out, `/app/b/{slug}` and `/app/b/{slug}/{section}` reach `/sign-in`, render no dashboard chrome (`Switch boutique`, `Reporting window`, `Top up`), and issue **zero** `/api/v1/orgs/` requests |

### Run

```bash
bun run test:e2e:install   # chromium into node_modules/.playwright-browsers (git-ignored)
bun run test:e2e           # all specs; starts vite on :5173 unless E2E_BASE_URL is set
```

Run it through the script rather than `playwright test` directly: the specs sit **above** the web
package, so Node cannot resolve `@playwright/test` from their directory, and the script sets
`NODE_PATH=node_modules` for exactly that reason. `E2E_BASE_URL` points the suite at a deployed
origin instead of starting a local server. The suite is run in parallel workers; one admin
assertion waiting on Clerk's first load is timing-sensitive under a cold, loaded server and can
flake, so a red admin run should be re-run in isolation before it is believed.

**Not delivered, and stated as such.** The authenticated walks need a Clerk test session and a
running API, which this environment does not provide. The tenant role matrix is pinned instead by the
backend integration tests plus the DOM tests on the shell's `allowedSections` logic; the console
walks are pinned by the admin DOM tests. `docs/frontend/tenant-dashboard.md` records the same
limitation.

---

## 8. Coverage

Each stack instruments coverage with its own tooling and enforces a threshold in CI:

| Layer | Tool | Command / Config | Threshold |
|---|---|---|---|
| **Backend** | Coverlet (coverage.cobertura.xml) | `dotnet test --collect:"XPlat Code Coverage"` + ReportGenerator (HTML) | line ≥ **30%** (CI gate) |
| **Agent Service** | `pytest-cov` | `pytest --cov=app --cov-report=xml --cov-report=term --cov-fail-under=90` | **90%** |
| **Web (global)** | `@vitest/coverage-v8` | `bun run test:coverage` (config in `vite.config.ts`) | lines 80 / functions 70 / branches 70 / statements 80 |
| **Web (admin)** | `@vitest/coverage-v8` | `bun run test:coverage:admin` (`vitest.admin-coverage.config.ts`) | ratchet: `routes/admin` 80/74/70/79, `components/admin` 82/74/68/80 |
| **Web (tenant dashboard)** | `@vitest/coverage-v8` | `bun run test:coverage:dashboard` (`vitest.dashboard-coverage.config.ts`) | ratchet raised in T7: `components/dashboard` 44/31/42/42, `useDashboardWindow` and most `lib/*-api.ts` modules at 100 |
| **Mobile** | `flutter test --coverage` | `flutter test --coverage` → `coverage/lcov.info` | none (report uploaded only) |

Local coverage reports:

- **Backend**: `Aveline.Api.Tests/TestResults/` (gitignored).
- **Agent Service**: `agnet-service/coverage.xml` (XML) + terminal summary.
- **Web**: `frontend/web/coverage/` (text, JSON summary, and HTML reporters).
- **Mobile**: `frontend/aveline_mobile/coverage/lcov.info`.

### Coverage notes

- Core auth code (validation rules, role normalization, policies, permission handler,
  CORS, internal-auth handler, client config, DI wiring, app startup) is at **100%**
  line coverage — the integration tests exercise `Program` and all configuration
  end-to-end.
- The backend CI gate is deliberately low (30%) because feature modules are still
  scaffolding; the agent service (90%) and the global web run (80 %) are enforced on
  every PR, and the admin and tenant subtrees each carry their own **ratchet** in their
  own run (see §4).

---

## 9. CI Integration

`ci.yml` runs each suite in its own job:

| Job | Steps |
|---|---|
| `build-api` | build solution → `dotnet test` → collect coverage → enforce ≥30% line → ReportGenerator HTML → upload `aveline-api-coverage` |
| `test-python` | `ruff check` → `pytest --cov --cov-fail-under=90` → upload `aveline-agent-coverage` |
| `test-web` | `oxlint` → `bun run test:coverage` (global thresholds) → `bun run test:coverage:admin` → `bun run test:coverage:dashboard` → upload `aveline-web-coverage` → build |
| `test-flutter` | `flutter analyze` → `flutter test --coverage` → upload `aveline-mobile-coverage` → build APK |

All four coverage artifacts are uploaded as GitHub Actions artifacts for inspection.

**The Playwright suite is not a CI step.** It needs a browser download and (for the authenticated
walks) a Clerk test session, neither of which the workflow provides; it is a local and
pre-release check. Adding it is a workflow change with its own cache and secret decisions rather
than a line in this table.

---

## 10. Related Documentation

- [ADR-007: Clerk Authentication & JWT Validation Strategy](../ADR/ADR-007-clerk-authentication.md)
- [Authentication Flow — Architecture](../architecture/authentication.md)
- [CI/CD Specification](../../spec/spec-process-cicd-ci.md)
- [SE3090 Assignment Reports](../reports/README.md)
