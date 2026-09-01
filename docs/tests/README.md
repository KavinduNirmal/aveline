# Tests — Aveline

This folder documents the testing strategy and how to run the test suites for the
Aveline platform.

---

## 1. Test Layers

| Layer | Scope | Tools | Location |
|---|---|---|---|
| **Unit** | JWT validation rules, role normalization, authorization policies, permission handler, CORS, internal-auth handler, typed agent client, DI registration | xUnit (C#) | `Aveline.Api.Tests/` |
| **Unit** | Internal-token middleware (valid / missing / invalid / unconfigured), `/agents/ping`, user-context logging | pytest + FastAPI TestClient | `agnet-service/tests/` |
| **Integration** | Full auth flow: frontend JWT → backend validation (real JwtBearer + JWKS pipeline) → internal-token call to agent → response + identity propagation | xUnit + WebApplicationFactory + stub servers | `Aveline.Api.Tests/FullAuthFlowIntegrationTests.cs` |

> The Python-side internal-token enforcement and the backend→agent contract are also
> exercised end-to-end manually against the live dev stack (see `docs/ai-usage/` and
> the ADRs).

---

## 2. Backend Tests (`Aveline.Api.Tests/`)

### Run

```bash
dotnet test Aveline.Api/Aveline.Api.sln
```

### Coverage

```bash
dotnet test Aveline.Api/Aveline.Api.sln --collect:"XPlat Code Coverage"
```

Reports are written to `Aveline.Api.Tests/TestResults/` (gitignored).

### Files

| File | Covers |
|---|---|
| `JwtValidationTests.cs` | `AuthenticationConfiguration.BuildTokenValidationParameters` — valid, wrong key, expired, not-yet-valid, wrong issuer, missing `aud`, mismatched audience, missing role claims |
| `RoleClaimNormalizerTests.cs` | `RoleClaimNormalizer.PromoteRoleClaims` — `user_role`/`org_role` promotion, no-role, empty value, no duplication, null identity |
| `AuthorizationPolicyTests.cs` | `AuthorizationConfiguration` role policies (`Associates`/`Managers`/`Owners`) and permission policies (`approvals:approve`, `payments:refund`, `catalog:view`, `settings:manage`) — allowed + denied cases |
| `AuthenticationConfigurationTests.cs` | Bearer scheme registration; throws when `Clerk:Authority` missing |
| `CorsConfigurationTests.cs` | CORS policy origins allow-list; throws when origins missing/empty |
| `ServiceToServiceAuthTests.cs` | `InternalServiceAuthHandler` adds `X-Internal-Token` (+ throws when unset); `AgentServiceClient` path routing; `AddAgentServiceClient` registration |
| `FullAuthFlowIntegrationTests.cs` | **Integration** — full flow described below |

---

## 3. Python Tests (`agnet-service/tests/`)

### Run (from `agnet-service/` or repo root)

```bash
pytest agnet-service/tests/ -v
```

Configuration lives in `agnet-service/pyproject.toml` (`[tool.pytest.ini_options]`:
`testpaths`, `pythonpath`, `asyncio_mode=auto`).

### Files

| File | Covers |
|---|---|
| `test_internal_auth.py` | `/health` public; `/agents/ping` missing/invalid token → 401, valid → 200 + echo; logs forwarded user context; dependency-level valid/missing/invalid; unconfigured token → 500 |

---

## 4. Integration Test — Full Auth Flow (`FullAuthFlowIntegrationTests`)

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

---

## 5. Coverage Targets

- Core auth code (validation rules, role normalization, policies, permission handler,
  CORS, internal-auth handler, client config, DI wiring, app startup) is at **100%**
  line coverage (the integration tests exercise `Program` and all configuration end-to-end).
- Full-project coverage is lower because feature modules are still scaffolding.

---

## 6. CI Integration

`ci.yml`:
- `build-api` builds the **solution** (API + tests) and runs `dotnet test`.
- `lint-python` runs `ruff check agnet-service/app/` (tests not yet executed in CI —
  planned under issue #23).

---

## 7. Related Documentation

- [ADR-007: Clerk Authentication & JWT Validation Strategy](../ADR/ADR-007-clerk-authentication.md)
- [Authentication Flow — Architecture](../architecture/authentication.md)
- [CI/CD Specification](../../spec/spec-process-cicd-ci.md)
