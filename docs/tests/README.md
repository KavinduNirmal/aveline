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

569 test cases across unit and integration test suites. Two distinct approaches:

- **Unit tests** — exercise a single class in isolation, with collaborators mocked
  via **Moq** (e.g. `OrganizationServiceTests`, `CredentialEncryptionServiceTests`,
  `VisionServiceTests`, `EventingRedisTests`).
- **Integration tests** — boot the real app with
  `WebApplicationFactory<Program>` against an **in-memory EF Core database**
  (`UseInMemoryDatabase`), with external dependencies replaced by stub servers
  and fake stores (see §5).

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

Vitest (node environment, no jsdom) across 13 files covering the `lib/` service
layer and React contexts. Component/context rendering uses `renderToString` from
`react-dom/server`; heavy dependencies (`@clerk/react`, `sonner`,
`@microsoft/signalr`) are stubbed with `vi.mock` + `vi.hoisted`.

### Run

```bash
bun run test            # vitest run
bun run test:coverage   # with coverage thresholds
```

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

## 7. Coverage

Each stack instruments coverage with its own tooling and enforces a threshold in CI:

| Layer | Tool | Command / Config | Threshold |
|---|---|---|---|
| **Backend** | Coverlet (coverage.cobertura.xml) | `dotnet test --collect:"XPlat Code Coverage"` + ReportGenerator (HTML) | line ≥ **30%** (CI gate) |
| **Agent Service** | `pytest-cov` | `pytest --cov=app --cov-report=xml --cov-report=term --cov-fail-under=90` | **90%** |
| **Web** | `@vitest/coverage-v8` | `vitest run --coverage` (config in `vite.config.ts`) | lines 80 / functions 70 / branches 70 / statements 80 |
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
  scaffolding; the agent service (90%) and web (80%) gates are strict and enforced
  on every PR.

---

## 8. CI Integration

`ci.yml` runs each suite in its own job:

| Job | Steps |
|---|---|
| `build-api` | build solution → `dotnet test` → collect coverage → enforce ≥30% line → ReportGenerator HTML → upload `aveline-api-coverage` |
| `test-python` | `ruff check` → `pytest --cov --cov-fail-under=90` → upload `aveline-agent-coverage` |
| `test-web` | `oxlint` → `bun run test:coverage` (thresholds) → upload `aveline-web-coverage` → build |
| `test-flutter` | `flutter analyze` → `flutter test --coverage` → upload `aveline-mobile-coverage` → build APK |

All four coverage artifacts are uploaded as GitHub Actions artifacts for inspection.

---

## 9. Related Documentation

- [ADR-007: Clerk Authentication & JWT Validation Strategy](../ADR/ADR-007-clerk-authentication.md)
- [Authentication Flow — Architecture](../architecture/authentication.md)
- [CI/CD Specification](../../spec/spec-process-cicd-ci.md)
- [SE3090 Assignment Reports](../reports/README.md)
