# SE3110 Compliance and Tool Integration Report

**Subject:** Aveline (SE3090 SEF integrated system) — readiness for SE3110 Assignment 2 and integration of K6, Selenium and Grafana
**Date of evidence:** 2026-09-18
**Repository:** `aveline` @ `12b04b5b5303fdbbf2514c51c4f7085a20abc481` (branch `feature/mobile-app-design-v1`)
**Deadline in scope:** 2026-10-05 (17 days from the evidence date)
**Status:** Developer-readiness report. Not a submission artifact.

---

## 1. Answer first

**You are substantially closer to compliance than the brief assumes, but you cannot score well today, and one mandatory rubric line is currently at zero.**

Confirmed by execution and file inspection:

- The engineering evidence base is real and large: **1240 .NET tests pass** (`dotnet test`, 4 m 31 s, exit 0), **381 Python tests** are collected with **378 passing / 1 failing**, and **158 web tests pass**. Coverage is enforced in CI for .NET, Python and web.
- The single blocking factual problem in the test suites is a **reproduced failure**: `agnet-service/tests/test_config.py::test_defaults_are_sane` fails because the default LLM provider is now `deepseek` but the test asserts `openai`. This fails the CI `test-python` job.
- **The mandatory non-functional requirement is not met.** The assignment states "Performance and security testing are required". Security testing exists (Trivy, ZAP baseline, `dotnet list --vulnerable`, `bun audit`), but there is **no performance or load testing anywhere in the tracked repository** — no k6, JMeter, artillery, NBomber or BenchmarkDotNet. The repo's own docs concede the harness "is deferred. There is no load runner in CI" (`docs/backend/README.md:297`). This is the highest-value gap to close and it is exactly what K6 addresses.
- **At least one complete integrated workflow test across components is required and does not exist.** Every web and mobile test is isolated; no test drives React UI → API → agent service → DB → back to a client. The strongest existing workflow test (`FullAuthFlowIntegrationTests`) is backend-only with stub servers.
- **The web coverage number is misleading**, not merely good. `frontend/web/vite.config.ts:20-29` excludes `src/components/**`, `src/contexts/**` and `src/routes/**` from the coverage denominator. The enforced 80% gate therefore measures **238 lines across 16 `lib`/`types` files**, while **111 excluded UI files holding ~16,946 lines** are unmeasured. `docs/tests/README.md:219-224` presents "91.59% lines" as a strength.
- **Grafana is genuinely absent and genuinely needs building.** The API already exposes a Prometheus scrape endpoint (`Aveline.Api/Program.cs:138`, `ObservabilityConfiguration.cs:31`), but `docker-compose.yml` has no Prometheus and no Grafana, and `otel-collector-config.yaml` has a **traces-only pipeline** (`:21-26`). Metrics are produced and never stored.
- **Selenium adds the single largest new capability available to you**: real-browser DOM interaction. No web test in the repo renders to a DOM, fires an event, or asserts a redirect — all 7 render tests use `renderToString` from `react-dom/server`.
- **`docs/tests/README.md` is materially stale** and should not be presented: it claims 569 backend cases (actual 1240), a single Flutter widget test (actual 88 test files), and 3 Docker-dependent classes (actual 13).

**Confidence.** Everything above is **Confirmed** — read or executed in this session — except the Grafana/Prometheus integration design, which is **Inferred** from the verified configuration surface, and the Flutter suite pass/fail status, which is **Unverified** (see §3, evidence limit).

---

## 2. Scope and method

**Read:** the assignment PDF (all 5 pages, extracted with `pdftotext -layout`); `README.md`; `docs/tests/README.md`; `docs/reports/README.md`; `docs/ai-usage/README.md`; `.github/workflows/ci.yml` (all 443 lines); `.github/dependabot.yml`; `docker-compose.yml`; `otel-collector-config.yaml`; `frontend/web/package.json`; `frontend/web/vite.config.ts`; `frontend/aveline_mobile/pubspec.yaml`; `Aveline.Api/Program.cs`, `Configurations/ObservabilityConfiguration.cs`, `Configurations/AuthenticationConfiguration.cs`, `Configurations/AuthorizationConfiguration.cs`; `Infrastructure/RateLimiting/*`; `Infrastructure/Integrations/ScrapeTokenAuthenticationHandler.cs`, `InternalTokenAuthenticationHandler.cs`; `Modules/ApiAccess/Authentication/ApiKeyAuthenticationHandler.cs`; `Modules/SystemHealth/Endpoints/HealthEndpoints.cs`; `Endpoints/WebhookEndpoints.cs`.

**Ran (read-only, in this session):**

| Command | Result |
|---|---|
| `dotnet test Aveline.Api/Aveline.Api.sln -c Release --collect:"XPlat Code Coverage"` | `Passed! - Failed: 0, Passed: 1240, Skipped: 0, Total: 1240, Duration: 4 m 31 s` |
| Cobertura parse of the above | `line-rate=0.9256 branch-rate=0.6616 lines=81723/88288 = 92.6%` |
| `frontend/web` → `vitest run --coverage` | `Test Files 25 passed (25) / Tests 158 passed (158)`; All files 89.24% stmts / 91.59% lines / 72.94% branch |
| `frontend/web` → JSON reporter | `numTotalTestSuites: 60, numTotalTests: 158, numPassedTests: 158, success: true` |
| `agnet-service/.venv/bin/python -m pytest tests/ -q` (delegated) | `1 failed, 378 passed, 2 skipped` in 45.07 s |
| Flutter probe (`flutter test`) | **Blocked** — Flutter SDK cache at `/home/kavindu/Development/Flutter/flutter/bin/cache` is read-only under this sandbox |
| Prometheus scrape probe (throwaway app mirroring `ObservabilityConfiguration.cs`) | `/metrics` served `HTTP 200`; **no `aveline_events_*` series exported** |

**Could not cover, and what would resolve it:**

- **Flutter suite pass/fail.** `flutter test` cannot run here (read-only SDK cache). Run it outside the sandbox: `cd frontend/aveline_mobile && flutter test --coverage`. All Flutter numbers below are static counts, not executed results.
- **Whether CI has ever gone green.** No run logs are in the repository. Resolve with `gh run list --workflow=ci.yml --limit 30`. This matters: `test-python` would fail on the `test_config.py` defect today.
- **Whether the `/tmp`-based web/Flutter CI steps behave as written on the runner.** Not verifiable locally.

**What would change the report's conclusions:** if a k6 script, Selenium suite or Grafana dashboard exists on an unmerged branch. I searched the tracked tree and the untracked `.merge-work*/` scratch trees; the only load-testing file found is `./.merge-work-262/tests/load/k6-telemetry-overhead.js`, which is **untracked** (not in git) and outside the active tree. If your team treats that scratch file as a starting point, say so and I can assess it — but as committed today, there is no performance testing.

---

## 3. Assignment requirements checklist

Extracted verbatim-in-substance from the PDF. "Evidence today" cites the repository.

### 3.1 Testing scope — required areas

| #   | Required area                                                                                                                                                                          | Required tool class                                                               | Status in Aveline                          | Evidence today                                                                                                                                                                                                                                                                                                        |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- | ------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Backend / API** — unit, service/business-logic, validation, controller, authn/authz, API integration                                                                                 | xUnit, NUnit, MSTest, Moq, `WebApplicationFactory`, Postman/Newman                | ✅ **Strong**                               | 1240 passing tests; 103 unit files, 40 `WebApplicationFactory<Program>` files, 13 Testcontainers files; real JwtBearer pipeline via a stub OIDC/JWKS Kestrel server (`Aveline.Api.Tests/StubServers.cs:16-70`)                                                                                                        |
| 2   | **Database** — integration, constraints, relationships/integrity, migrations, transactions                                                                                             | xUnit + PostgreSQL, Testcontainers, EF Core                                       | ✅ **Strong, above the bar suggested**      | 13 Testcontainers classes against `pgvector/pgvector:pg16`; `Database.MigrateAsync()` in all; partial-migration + backfill (`BillingMigrationBackfillTests.cs:41,84`); CHECK/FK/partition/index assertions; **pgvector cosine ordering** (`CustomerMemoryRepositoryPostgresTests.cs:97-140`); 31 migrations committed |
| 3   | **React Web** — component, form-validation, protected-route, API-integration, UI-state/error-state                                                                                     | Vitest/Jest, React Testing Library, MSW, Playwright                               | ⚠️ **Partial — and the gap is structural** | 25 files / 158 tests pass, but `environment: 'node'` and `renderToString` only; **no DOM, no `fireEvent`, no `act`, no router**; dependencies `@testing-library/react`, `jsdom`, `msw`, `playwright` all **absent**; routes and auth guards **untested**                                                              |
| 4   | **Flutter Mobile** — unit, widget, form-validation, navigation, API-integration                                                                                                        | `flutter_test`, `integration_test`, mocktail/Mockito                              | ⚠️ **Large but unaware-of-rubric**         | 88 test files, ~800 static call sites; `integration_test` and all mocking packages **absent**; form validators untested; `lib/app.dart` router never instantiated in a test                                                                                                                                           |
| 5   | **Integration / E2E** — API integration, cross-component, complete business workflow, cross-platform                                                                                   | Playwright, Postman/Newman, Flutter `integration_test`, or another justified tool | ❌ **Not met**                              | No cross-component or cross-platform test exists. `FullAuthFlowIntegrationTests` is backend-only over stub servers                                                                                                                                                                                                    |
| 6   | **Non-Functional** — performance, load, stress, security, usability, accessibility, compatibility, reliability, recovery                                                               | k6, JMeter, OWASP ZAP, Lighthouse, axe, Playwright                                | ❌ **Mandatory half missing**               | Security: Trivy, ZAP baseline, `dotnet list --vulnerable`, `bun audit`, Dependabot. **Performance/load/stress: absent.** Accessibility: absent. Compatibility: absent                                                                                                                                                 |
| 7   | **Agentic AI** — task-completion, agent-selection, tool-selection, structured-output, business-rule compliance, prompt-injection, approval-enforcement, failure-recovery, safe-failure | xUnit/pytest, promptfoo, DeepEval, schema validation                              | ⚠️ **Mixed**                               | Tool-selection, schema conformance, business-rule and **failure-recovery** are tested; **prompt-injection, approval-enforcement, guardrails, red-teaming and eval harness are all absent**; the commerce approval agent is a **stub** (`app/workflows/concierge_workflow.py:206-225`)                                 |

**Two hard requirements to hold against all of the above:**

> "Performance and security testing are **required**." — since security is present and performance is not, this line is currently failed.
>
> "At least one test must cover a complete integrated workflow across the relevant components of the system." — currently unsatisfied.

### 3.2 Documents to prepare

| Document | Minimum content | Status today |
|---|---|---|
| Test Plan | Scope, objectives, testing areas, tools/frameworks, environment, responsibilities, schedule | ⚠️ `docs/tests/README.md` is a runbook, not a plan. It has no objectives, no schedule, no per-student responsibilities, no risk analysis — and its counts are stale |
| Test Case Document | Test case ID, feature, preconditions, steps/input, expected, actual, Pass/Fail | ❌ **Absent** as a document. 158+1240+381 executed tests exist with no ID/feature/precondition mapping |
| Defect / Bug Report | Defect ID, description, severity/priority, steps to reproduce, evidence, status, retest result | ❌ **Absent**. One real defect is currently live (§5.1) and undocumented |
| Test Execution Summary | Tests executed/passed/failed, defects identified/fixed, conclusion | ❌ **Absent**. CI artifacts (coverage uploads) are the closest thing |
| Tool-Generated Evidence | Automated reports, coverage, performance results, security scans, logs, AI eval outputs | ✅ **Good for coverage and security**; ❌ **no performance evidence exists to collect** |

### 3.3 Deliverables

Software Testing Report (PDF) · Test Case Document · Defect Report with retest evidence · tool evidence (screenshots, generated reports, logs, exports) · **automated test source code for the applicable areas** · GitHub repository link + contribution/commit evidence · reproducer configuration.

### 3.4 Marking rubric

**Total 100 = Group 40 + Individual 60.** The assessment is explicitly demonstration-based:

- **GROUP Testing Strategy & Coverage (10)** — scope relevant, covers important components/workflows/risks with suitable tools.
- **GROUP Integrated & Non-Functional Testing Demonstration (15)** — meaningful integration/E2E *and* non-functional testing with suitable tools; results interpreted and connected to the actual SE3090 system.
- **GROUP Overall Test Results, Defects & Documentation (15)** — traceable results, defects recorded, important fixes shown with retesting, complete and consistent documents/evidence.
- **INDIVIDUAL Testing Tool / Framework Demonstration (15)** — personally and confidently demonstrates tool(s), explains selection, configuration/setup, and use on the actual system.
- **INDIVIDUAL Test Implementation & Execution (15)** — personally implemented tests executed successfully, with normal / invalid / boundary-edge / failure cases and meaningful assertions.
- **INDIVIDUAL Results, Defects & Retesting (10)** — interprets own results, identifies meaningful defects, explains cause/fix, demonstrates retesting or verification.
- **INDIVIDUAL Technical Contribution (5)** — ownership visible through test code, Git history, related fixes.
- **INDIVIDUAL Viva & Technical Understanding (15)** — strong understanding of own approach, code, tools, results, system behaviour; can run, explain, modify or troubleshoot a test on request.

**Also mandatory:** "Each student must personally demonstrate and explain at least one meaningful tool/framework-based testing contribution." Individual mark is 60 of 100, so tool ownership must be *allocated*, not shared vaguely.

---

## 4. Current system overview (what actually exists)

**Four independently tested codebases**, one per stack:

| Layer | Stack | Test tooling actually installed | Executed result (this session) |
|---|---|---|---|
| Backend API | ASP.NET Core 10, minimal-API endpoints (30 `*Endpoints.cs`, 15 modules; **0 controllers**) | xUnit, Moq, FluentAssertions, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql 4.15.0`, coverlet | **1240 passed / 0 failed**, 92.6% line, 66.2% branch |
| Agent service (`agnet-service/`) | Python 3.12, FastAPI, LangGraph | pytest, pytest-asyncio, pytest-cov, respx, fakeredis, pytest-httpx, ruff | **378 passed / 1 failed / 2 skipped** |
| Web dashboard | React 19, Vite 8, TS, React Router 7, Clerk, axios, SignalR | Vitest 4.1.11 + `@vitest/coverage-v8` **only** | **158 passed / 0 failed** over a 238-line measured surface |
| Mobile app | Flutter, Dart 3.13.1, Provider, go_router 16, Dio, `clerk_flutter` | `flutter_test`, `flutter_lints` **only** | **Not executed** (sandbox blocked); 88 test files / ~800 static cases |

**Runtime topology** (`docker-compose.yml`): `postgres` (pgvector pg16, :5432), `redis` (7-alpine, :6379), `api` (:5091→8080), `agent` (internal-only :8000), `otel-collector` (:4318), `jaeger` (:16686 UI, :4317). **Six services; no Prometheus, no Grafana.**

**Quality gates that do exist** (`.github/workflows/ci.yml`, 8 jobs): `hygiene`, `build-api`, `test-python`, `test-web`, `test-flutter`, `security-scan`, `zap-baseline`, `release-android`.

| Gate | Threshold | Line |
|---|---|---|
| .NET line coverage | ≥ 30% (fails build) | `ci.yml:79-92` |
| Python coverage | `--cov-fail-under=90` | `ci.yml:141` |
| Web coverage | lines 80 / funcs 70 / branches 70 / stmts 80 | `vite.config.ts:30-35` via `ci.yml:174` |
| Flutter coverage | **none** (artifact only) | `ci.yml:223` |
| Trivy CRITICAL/HIGH | `exit-code: "1"` | `ci.yml:310-321` |
| .NET vulnerable packages | fails build | `ci.yml:293-301` |
| `bun audit --audit-level high` | fails build | `ci.yml:308` |
| ZAP baseline | **`continue-on-error: true` — non-gating** | `ci.yml:338` |

**Observability that does exist:** the API **already serves Prometheus text at `/metrics`** (`Program.cs:138`, `.RequireAuthorization(AuthorizationConfiguration.MetricsPolicy)`), with a Prometheus exporter registered in `ObservabilityConfiguration.cs:31`. An `otel-collector` receives OTLP and forwards **traces only** to Jaeger (`otel-collector-config.yaml:21-26`). Nothing scrapes `/metrics`; nothing stores it.

**Authentication surface** (material for K6 and Selenium design):
- Clerk JWT bearer is the default scheme; `Clerk:Authority` is required or startup throws (`AuthenticationConfiguration.cs:29-30`).
- Three non-Clerk schemes exist: `InternalToken` (`X-Internal-Token`, `InternalTokenAuthenticationHandler.cs:9`), `ScrapeToken` (`Authorization: Bearer <token>`, `ScrapeTokenAuthenticationHandler.cs:37`), and `ApiKey` (`X-Api-Key`, `ApiKeyAuthenticationHandler.cs:41`).
- Org-scoped policies accept **either** a Clerk bearer token **or** an API key (`AuthorizationConfiguration.cs:93-99`).
- `/metrics` accepts the internal service token or a configured scrape token (`AuthorizationConfiguration.cs:118-124`).
- `/health`, `/health/live`, `/health/ready` are anonymous (`HealthEndpoints.cs:15,23,24`).
- Anonymous, rate-limited and HMAC-signed public surface exists at `POST /webhooks/whatsapp/{organizationId}` (`WebhookEndpoints.cs:60-115`).

---

## 5. Gap analysis

### 5.1 Defects and integrity issues found (fix these first — they are cheap and they are rubric-visible)

| # | Severity | Finding | Evidence | Impact |
|---|---|---|---|---|
| **D-1** | **Critical** | Default LLM provider changed to `deepseek`; test asserts `openai`. Reproduced: `1 failed, 378 passed` | `agnet-service/tests/test_config.py:16`; run output | **Fails CI `test-python`.** Blocks every PR. Also a ready-made "Defect ID → fix → retest" artefact for the assignment |
| **D-2** | **Critical** | Web coverage gate excludes all UI source, so the enforced 80% measures 238 lines while ~16,946 UI lines are unmeasured | `frontend/web/vite.config.ts:20-29`; measured: 16 files / 238 lines; excluded: 111 files / 16,946 lines | Group Strategy & Coverage (10) and Results/Documentation (15) are exposed. Any examiner who opens `vite.config.ts` sees the exclusion |
| **D-3** | **High** | Mandatory performance/load testing absent repo-wide | `grep -riE "k6|jmeter|artillery|NBomber|BenchmarkDotNet"` → only untracked `.merge-work-262/` scratch; `docs/backend/README.md:297` | **Group Integrated & Non-Functional (15)** partially unreachable. Mandatory requirement failed |
| **D-4** | **High** | No cross-component / cross-platform E2E test | No web or mobile test imports a router, `App`, or the real API; `integration_test/` does not exist | **Group Integrated & Non-Functional (15)** and the explicit PDF requirement |
| **D-5** | **High** | `docs/tests/README.md` materially stale | `:24` 569 backend cases (actual 1240); `:151` "A single widget test" (actual 88 files); `:45-48` 3 Docker classes (actual 13); `:28` Moq usage wrong (4 files use it) | Documentation criterion (15). Presenting stale counts in a viva is a credibility loss |
| **D-6** | **Medium** | ZAP baseline is non-gating and scans **unauthenticated** routes only | `ci.yml:338` `continue-on-error: true`; boots API then targets `/` with no credentials (`ci.yml:361-383`) | Security evidence is thin: the entire authenticated API surface is unscanned |
| **D-7** | **Medium** | 13 Testcontainers classes hard-fail without Docker; no skip/trait mechanism exists | All 13 call bare `await _postgres.StartAsync()` with no guard; `grep -rn "catch" Aveline.Api.Tests/` → **0 hits**; no `[Trait]`/`xunit.runner.json` | Latent: a developer without Docker cannot get a green suite. Poor demo experience |
| **D-8** | **Medium** | Web `RequireAdmin` guard is dead code but documented as active | Imported nowhere in `src` (only its own definition); `frontend/web/README.md:66` claims it guards admin routes | Would be noticed in E2E/route-gap work; also a valid defect entry |
| **D-9** | **Medium** | `pnpm-lock.yaml` present and untracked; hygiene job rejects it | Simulated CI checkout of `HEAD` → hygiene **passes** (the file is untracked). But it is one `git add` from failing every job | Operational fragility during the 17-day crunch |
| **D-10** | **Low** | Uncommitted `.gitignore` adds a bare `test` pattern | `git diff .gitignore` → `+test`; `git check-ignore` shows nothing currently ignored | Latent trap: a future `frontend/*/test/` path could be silently untracked |
| **D-11** | **Low** | Python eval/golden harness partially dead: `tests/golden_cases_visual.py` exists but is **not collected** (name does not match `test_*.py`) | `pyproject.toml:12-15` `python_files` default | AI evaluation evidence understated |
| **D-12** | **Low** | `aveline.events.*` instruments likely never exported | Meter is `"Aveline.Api.Eventing"` (`EventBusMetrics.cs:26`) but only `AddMeter("Aveline.Api")` is registered (`ObservabilityConfiguration.cs:15,28`). My scrape probe produced **no `aveline_events_*` series**; the probe's control counter also did not export, so the mechanism is **Inferred, not Confirmed** | Grafana's business dashboards would have no event-bus data until fixed |

### 5.2 Coverage gap matrix

| Testing area | Required test types | Have | Missing | Tooling gap | Rubric exposure |
|---|---|---|---|---|---|
| **Backend / API** | unit, service, validation, controller, authn/authz, integration | unit, service, validation, endpoint integration, authn/authz, rate-limit, idempotency, resilience | controller tests (no controllers exist — N/A, justify in writing) | none material | Low |
| **Database** | integration, constraints, relationships, migrations, transactions | all five, incl. partial-migration backfill, partitioning, pgvector | — | none | Low |
| **React Web** | component, form-validation, protected-route, API-integration, UI/error state | API-client, error mapping, pure logic, SSR string snapshots | **DOM interaction, form validation, protected routes/redirects, route-level loading/empty/error, real HTTP** | **`jsdom`/`happy-dom`, `@testing-library/*`, `msw`, Selenium/Playwright** | **High** |
| **Flutter** | unit, widget, form-validation, navigation, API-integration | unit, widget, controller, fake-adapter API contracts | **form validation, real-router navigation, `integration_test` E2E, golden/visual** | **`integration_test`, `mocktail`** | **Medium-High** |
| **Integration / E2E** | API integration, cross-component, complete business workflow, cross-platform | backend-only workflow over stubs | **all cross-component and cross-platform** | **Selenium (web), Flutter `integration_test`** | **High** |
| **Non-Functional** | performance, load, stress, security, usability, accessibility, compatibility, reliability, recovery | security (Trivy/ZAP/vulnerable-deps/audit/Dependabot), reliability/recovery (partial) | **performance, load, stress, accessibility, compatibility, usability** | **K6 (perf/load/stress), axe (a11y), Playwright device profiles** | **High (mandatory)** |
| **Agentic AI** | task-completion, agent-selection, tool-selection, structured-output, business rules, prompt-injection, approval-enforcement, failure-recovery, safe-failure | tool-selection, structured-output, business rules, failure-recovery, safe-failure | **prompt-injection, approval-enforcement, guardrails, red-teaming, eval harness** | **promptfoo or DeepEval; the commerce approval node must stop being a stub** | **Medium-High** |

---

## 6. Tool integration plans

Each plan is grounded in verified repo facts and states exactly what to add. K6 and Selenium are **new** capabilities. Grafana is a **new deployment** whose data source already exists.

### 6.1 K6 — performance, load and stress testing

**Why K6 and not JMeter.** K6 scripts are JavaScript, so they live beside the repo's existing `bun`/Node tooling and can be reviewed like code; it ships a single static binary usable in GitHub Actions without a JVM; and its **thresholds decide the process exit code**, which is what makes a performance gate real in CI. The last property is the decisive one — see the threshold semantics below.

#### 6.1.1 What to test, and what to authenticate as

Weight the scenarios toward the workflows the assignment cares about, and choose the identity mechanism per scenario. Three schemes already exist, which removes the hardest obstacle (acquiring Clerk tokens in a load generator):

| Scenario | Endpoint | Auth | Why this scenario |
|---|---|---|---|
| `smoke` | `/health`, `/health/live`, `/health/ready` | anonymous (`HealthEndpoints.cs:23-24`) | Cheapest gate; catches a dead deploy immediately |
| `read-hot` | `GET /metrics` | `Authorization: Bearer <Metrics:ScrapeToken>` (`ScrapeTokenAuthenticationHandler.cs:37`) | Exercises the **observability path itself** under load — and is the K6→Prometheus bridge (§6.3) |
| `catalog-browse` | `GET /api/v1/orgs/{orgId}/catalog` | `X-Api-Key` (`ApiKeyAuthenticationHandler.cs:41`) | The dominant owner-dashboard read; org-scoped policy accepts API keys (`AuthorizationConfiguration.cs:93-99`) |
| `webhook-ingest` | `POST /webhooks/whatsapp/{orgId}` | anonymous + HMAC signature + `IRateLimiter` | High-volume inbound; **tests rate limiting and signature verification under load**, and a threshold failure here is a genuine finding |
| `agent-query` | `POST /agents/query` on the agent service | `X-Internal-Token` | The LangGraph path — likely your slowest route and your best stress target |
| `checkout-workflow` | approval + sign-off routes | Clerk JWT (pre-minted, long-TTL) | The revenue path; use for the E2E-shaped load profile |

**Critical caveat to state in the report:** the `GET /metrics` and `X-Api-Key` options exist precisely because Clerk is the awkward case. For the Clerk-authenticated scenarios, mint a small pool of long-lived test-user tokens once (outside K6), inject as a `__ENV`/secret, and reuse them — and say in the report that token minting is out of band, because it is. Do not let a reviewer think K6 is exercising the Clerk sign-in flow; it is not.

#### 6.1.2 Metrics and thresholds

Use `http_req_duration` percentiles, `http_req_failed` rate, `checks` rate, plus custom `Trend`/`Rate`/`Counter` instruments. k6's documented aggregation per metric type is: Counter → `count`/`rate`; Gauge → `value`; Rate → `rate`; Trend → `avg`, `min`, `max`, `med`, `p(N)` ([k6 Thresholds](https://grafana.com/docs/k6/latest/using-k6/thresholds/)).

Proposed baseline thresholds (tune after a first measurement run; a threshold you cannot meet is worse than none):

```javascript
export const options = {
  thresholds: {
    http_req_failed:   ['rate<0.01'],          // < 1% errors
    http_req_duration: ['p(95)<500', 'p(99)<1500'],
    'http_req_duration{scenario:catalog-browse}': ['p(95)<300'],
    'http_req_duration{scenario:agent-query}':    ['p(95)<8000'], // LLM-bound, generous
    checks:            ['rate>0.99'],
  },
  scenarios: {
    smoke:    { executor: 'constant-vus', vus: 1,  duration: '30s', tags: { scenario: 'smoke' } },
    load:     { executor: 'ramping-vus', startVUs: 0,
                stages: [{ duration: '1m', target: 20 }, { duration: '3m', target: 20 }, { duration: '1m', target: 0 }] },
    stress:   { executor: 'ramping-vus', startVUs: 20, startTime: '6m',
                stages: [{ duration: '2m', target: 80 }, { duration: '2m', target: 0 }] },
  },
};
```

**Non-obvious but load-bearing facts** (verified against k6 docs, and easy to get wrong):

- **`checks` do not fail a test run.** Only `thresholds` change the exit code. If you gate CI on checks alone you have no gate at all — this is the most common k6 mistake ([k6 Thresholds](https://grafana.com/docs/k6/latest/using-k6/thresholds/)).
- A failed threshold makes `k6 run` exit non-zero, which is what makes the CI step meaningful.
- To stop a destroying test early, use the long threshold form (`{ threshold: 'p(99) < 10', abortOnFail: true, delayAbortEval: '10s' }`) — and know that `abortOnFail` is evaluated every 60 s in Grafana Cloud, so it can lag.

#### 6.1.3 CI/CD integration

Add a job that runs **after** `build-api`, as a new gate plus a scheduled soak:

```yaml
  k6-performance:
    name: K6 Performance Test
    runs-on: ubuntu-latest
    needs: [hygiene, build-api]
    steps:
      - uses: actions/checkout@<pin>
      - uses: grafana/setup-k6-action@<pin>          # installs the k6 binary
      - name: Boot the API against ephemeral Postgres/Redis
        run: docker compose up -d postgres redis api  # compose has no compose-in-CI today; see §6.1.4
      - name: Run smoke gate
        run: k6 run tests/load/k6-smoke.js
      - name: Run load profile and push results to Prometheus
        env:
          K6_PROMETHEUS_RW_SERVER_URL: http://localhost:9090/api/v1/write
          K6_PROMETHEUS_RW_TREND_STATS: p(95),p(99),min,max
        run: k6 run -o experimental-prometheus-rw --tag testid=${{ github.run_id }} tests/load/k6-load.js
      - uses: actions/upload-artifact@<pin>
        if: always()
        with: { name: k6-results, path: '*.json' }
```

Keep `k6 run --out json=results.json` as the artifact so a reviewer can see raw numbers, and use `--tag testid=...` so runs are separable in Grafana.

**Flag two honest risks in the plan:** (a) `experimental-prometheus-rw` is an **experimental** k6 module — the docs warn breaking changes are possible before it graduates; the JSON artifact is therefore the durable evidence and Prometheus is the convenience. (b) Today CI uses no `docker compose` at all (`grep -n "docker\|compose" .github/workflows/ci.yml` → no matches), so booting the stack is new CI work, not a one-line change.

#### 6.1.4 What K6 will actually find

Given a modular monolith over one Postgres instance: contention at the Postgres connection pool under the `read-hot` and `webhook-ingest` scenarios; rate-limiter behaviour as a deliberate pass/fail boundary (`DistributedRateLimiter` over Redis, `DistributedRateLimiter.cs:12`); and — the interesting one — **agent-query latency being LLM-bound and therefore bimodal**, which is a genuinely good viva talking point if you measure both the rule-based intent-gate path and the LLM path separately.

---

### 6.2 Selenium — real-browser web UI automation

**Why Selenium is the highest-value addition.** Today the web suite has **no DOM**. All 7 render tests call `renderToString` (`Composer.test.tsx:2` and 6 others) and assert `expect(html).toContain(...)`. Searches for `createRoot`, `react-dom/client`, `act(`, `fireEvent`, `screen.`, `user-event`, `MemoryRouter` across `frontend/web/src/**/*.test.*` return **zero hits**. Selenium is the only way, within the tools the assignment names, to test real events, real routing and real redirects. It directly converts three currently-zero rubric items (component interaction, form validation, protected routes) into scored evidence.

**Why not Playwright, stated honestly.** Playwright would be the better engineering choice (auto-waiting, first-class Clerk support, built-in parallelism) and is the more likely thing an examiner uses daily. But the assignment names Selenium, and the constraint is that the tools integrate with the existing stack. Recommendation: **Selenium as the named deliverable tool; Playwright optional and explicitly justified as a deliberate comparison** if time allows — do not substitute silently.

#### 6.2.1 What to test, mapped to the rubric's own words

The assignment lists exactly five web areas. Cover each:

| Required area | Selenium test | Covers today? |
|---|---|---|
| Component testing | Assert `AvelineChatLauncher` opens its panel on click; `Composer` submits on Enter; message bubbles render agent activity | ❌ none |
| Form-validation testing | `SignInForm` empty-submit → inline error (`SignInForm.tsx:68,99-102,120`); password min-length (`SignUpForm.tsx:238,256`); boutique phone validation (`BoutiqueDetailsStep.tsx:39-42`, `isValidLkPhone` in `boutique.ts:55`) | ❌ none — validators are hand-rolled and untested |
| Protected-route testing | `ProtectedRoute` redirect to `/sign-in` (`ProtectedRoute.tsx:14-16`); `RedirectIfAuthenticated` (`:18-20`); `RequireAccountState` (`:20-26`); 403 → `/suspended` / `/onboarding` / `/forbidden` (`AuthApiBridge.tsx:30-43`) | ❌ none |
| API-integration testing | Log in for real, let the interceptor attach `Authorization: Bearer` (`api.ts:60-68`), assert data renders from the live API | ❌ none — no web test opens a socket |
| UI-state and error-state | Route-level loading (`PageLoader`), empty inbox, 500 → error surface | ⚠️ partial (content blocks only) |

Plus the integrated workflow that the PDF mandates: **one Selenium test that spans React → ASP.NET API → Postgres and back**: sign in → land on `/app` → open conversations → assert a thread renders → (optionally) trigger an approval and verify it appears in the owner dashboard.

#### 6.2.2 Configuration and setup

- **Driver management:** no driver binaries to install. **Selenium Manager** has been built into Selenium since 4.6 and resolves the correct driver automatically; Selenium Grid is only needed for scale-out ([Introducing Selenium Manager](https://www.selenium.dev/blog/2022/introducing-selenium-manager/), [Grid getting started](https://www.selenium.dev/documentation/grid/getting_started/)). This removes the single most common reason Selenium setups rot.
- **Language choice:** use **C# + xUnit**, not a new Node/Python harness. It reuses `Aveline.Api.Tests` conventions, the existing coverage pipeline and the existing `dotnet` CI job — "integrates with the existing stack" in the literal sense. A separate `Aveline.E2E.Tests` project is the cleanest container.
- **App under test:** CI builds the SPA and uploads `dist` today (`ci.yml:190-194`) but **never serves it** (`grep -n "preview" .github/workflows/ci.yml` → nothing). Add a serving step (`vite preview` or an `nginx` container) before the Selenium job; this is a real prerequisite, not an afterthought.
- **Authentication — the one hard problem.** Clerk's own testing guidance is to use **test API keys** (`pk_test_*`/`sk_test_*`), call a bot-protection bypass token setup **before navigating to auth pages**, and persist auth via storage state to avoid UI sign-in per test ([clerk-testing skill, Clerk](https://clerk.com/docs/guides/development/testing/playwright/test-helpers)). Note that `setupClerkTestingToken()` is a Playwright/Cypress helper and has **no official Selenium equivalent**, so you have a real decision here:

  | Option | Mechanism | Trade-off |
  |---|---|---|
  | **A. Real UI sign-in (recommended)** | Selenium drives the actual `<SignIn/>` against a dedicated Clerk **development** instance with seeded test users; reuse the session cookie across tests | Truest E2E and honest; slower, and bot-protection may need `data-clerk-component` waits or a test-mode instance |
  | **B. Storage-state replay** | Sign in once in a setup fixture, persist the browser profile/cookies, reuse | Fast and stable; masks sign-in regressions |
  | **C. API-key principal for API calls** | Use `X-Api-Key` for the API portion, Selenium only for UI | Sidesteps Clerk entirely; proves less about the real user journey |

  Recommendation: **A for the one mandated integrated workflow; B for the rest of the suite**, and state this tradeoff in the report — it is exactly the kind of configuration reasoning the Individual Tool Demonstration criterion (15) rewards. The repo already carries a dev-instance key on disk (untracked `frontend/web/.env.local`, decoding to `inspired-warthog-8208.clerk.accounts.dev`, the same instance the ZAP job hardcodes as `Clerk__Authority` at `ci.yml:352`), so a dev/test instance is already the environment.
- **Cross-browser:** run Selenium Grid in CI with a **Chrome + Firefox** matrix. Keep it to 2 browsers; the assignment rewards justified coverage, not browser count.
- **Parallelism:** xUnit parallelises by collection by default, but the existing suite has **no `[Collection]` and no `xunit.runner.json`**, so add an explicit collection per Selenium fixture and cap parallel workers to the Grid capacity. Do not let browser sessions collide with the 1240 existing tests.

#### 6.2.3 What Selenium requires before it works

1. A served `dist` (new CI step).
2. A seeded test org + test user in the Clerk dev instance (new fixture/seed data).
3. The API running with `Clerk:Authority` pointed at that instance (the ZAP job already demonstrates the pattern: `ci.yml:352,361-362`).
4. Explicit `[Collection]` isolation so browser tests do not race the existing suite.

#### 6.2.4 Selenium's limits — say these out loud

Selenium gives you **no** coverage measurement over React code (V8/Vitest does that), it **cannot** test Flutter, and it is the slowest suite you will own. It is justified here *specifically* because the DOM interaction, form-validation and route-redirect gaps are otherwise unreachable with the installed tooling — which is a strong, evidence-backed justification for the viva.

---

### 6.3 Grafana — monitoring, visualisation and alerting

**Why this is unusually cheap here.** The API already exposes Prometheus text at `/metrics` with a `Prometheus.AspNetCore` exporter registered (`Program.cs:138`, `ObservabilityConfiguration.cs:26-31`), and that endpoint is already auth-protected and already covered by a test (`MetricsEndpointAuthTests.cs:57`). You are adding a scraper and a dashboard, not an instrumentation layer. That reframing is worth stating in the report: Grafana is the *evidence surface* for work already done, not a new subsystem.

#### 6.3.1 Services to add to `docker-compose.yml`

```yaml
  prometheus:
    image: prom/prometheus:latest
    container_name: aveline_prometheus
    command: ["--config.file=/etc/prometheus/prometheus.yml",
              "--web.enable-remote-write-receiver"]   # needed for k6 experimental-prometheus-rw
    volumes:
      - ./observability/prometheus.yml:/etc/prometheus/prometheus.yml:ro
    ports: ["9090:9090"]

  grafana:
    image: grafana/grafana:latest
    container_name: aveline_grafana
    environment:
      GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_ADMIN_PASSWORD:?required}
      GF_AUTH_ANONYMOUS_ENABLED: "false"
    volumes:
      - ./observability/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./observability/grafana/dashboards:/var/lib/grafana/dashboards:ro
    ports: ["3000:3000"]
    depends_on: [prometheus]
```

`--web.enable-remote-write-receiver` is required for k6's remote-write output to land in Prometheus ([k6 Prometheus remote write](https://grafana.com/docs/k6/latest/results-output/real-time/prometheus-remote-write/)).

`observability/prometheus.yml`:

```yaml
global: { scrape_interval: 15s }
scrape_configs:
  - job_name: aveline-api
    metrics_path: /metrics
    authorization: { credentials_file: /etc/prometheus/scrape_token }
    static_configs: [{ targets: ['api:8080'] }]
  - job_name: k6
    honor_labels: true
    static_configs: [{ targets: ['prometheus:9090'] }]
```

**Verified detail:** `/metrics` requires the `MetricsPolicy`, satisfied by `InternalToken` or `ScrapeToken` (`AuthorizationConfiguration.cs:118-124`); `ScrapeToken` reads `Metrics:ScrapeToken` and expects `Authorization: Bearer <token>` with a fixed-time comparison (`ScrapeTokenAuthenticationHandler.cs:13,26,37`). **`Metrics:ScrapeToken` is not currently set anywhere** — not in `appsettings.json`, `appsettings.Development.json`, `.env.example` or `docker-compose.yml`. Set it for the scrape, and note that `/metrics` is otherwise reachable only by the internal service token, whose compose default is the literal `change-me-internal-token` (`docker-compose.yml:80`). **Flag this in the report as the security finding it is**: an observability endpoint protected by a known default secret.

#### 6.3.2 Dashboard design

Provision dashboards as code (`provisioning/datasources/prometheus.yml` + `provisioning/dashboards/*.yaml`) so they are reproducible evidence rather than hand-built screenshots. Four dashboards:

1. **System / RED** — request rate, error rate, duration percentiles from `http_server_request_duration` (ASP.NET Core instrumentation is registered, `ObservabilityConfiguration.cs:29`), plus `/metrics` scrape health.
2. **Business / event bus** — `aveline.events.published|received|failed` and `aveline.events.publish_latency_ms` (`EventBusMetrics.cs:27-30`, tagged `event_type`). **Caveat: these likely do not export today** (D-12) — verify with `curl -H "Authorization: Bearer <scrape-token>" localhost:5091/metrics | grep aveline.events` before promising this dashboard.
3. **K6 performance** — import the pre-built **k6 Prometheus** dashboard (Grafana ID **19665**) rather than authoring one; filter by the `testid` tag you pass on the k6 command line ([k6 Prometheus remote write](https://grafana.com/docs/k6/latest/results-output/real-time/prometheus-remote-write/)).
4. **Database** — Postgres exporter metrics (new: `postgres_exporter` sidecar). `OpenTelemetry.Instrumentation.EntityFrameworkCore` is registered for traces only, so DB latency/connection-pool panels need the exporter; without it, this dashboard will be empty. Either add it or scope the dashboard to traces-in-Jaeger and say so.

#### 6.3.3 Feeding Selenium results into Grafana

K6 has first-class Prometheus output; **Selenium does not.** Be explicit about the mechanism rather than hand-waving. Two workable options:

- **Preferred:** have the Selenium runner push a JSON summary to the **Prometheus Pushgateway**, scraped by Prometheus. Metrics: `aveline_e2e_tests_total{suite,browser,status}`, `aveline_e2e_duration_seconds`, `aveline_e2e_retries_total`. This gives a genuine pass/fail panel per browser.
- **Simpler fallback:** publish the Selenium HTML/JSON report as a CI artifact and a static HTML panel, and state plainly that Grafana visualises K6 + API metrics while Selenium reports are artifact-based. Honesty here scores better than a fake integration.

#### 6.3.4 Alerting

Grafana unified alerting, provisioned as code:

| Alert | Condition | Severity |
|---|---|---|
| API error rate | `rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]) > 0.01` | high |
| API latency | `histogram_quantile(0.95, ...) > 0.5` for 10 m | medium |
| K6 threshold breach | k6 threshold series non-zero after a run | high |
| `/metrics` scrape down | `up{job="aveline-api"} == 0` for 3 m | high |
| Agent service unhealthy | `/health/ready` failing (probe `AgentServiceHealthCheck.cs:21`) | high |

Route alerts to a webhook (Slack/email) so the alerting is demonstrably live, not just configured. Careful: with a modular monolith on one host, an error-rate alert will fire during every stress test — set a `testid`-aware inhibition rule or exclude the stress window, and note that decision.

#### 6.3.5 Honest limits

Grafana provides **no marks for test coverage** — it is evidence of the *Integrated & Non-Functional* criterion and a strong Viva aid. It is also a **deployment dependency**: two new containers, new secrets, and a new failure mode. Do not start it in the final 48 hours.

---

## 7. Mapping to the marking rubric

### 7.1 Group (40 marks)

| Criterion (marks) | What is already there | What the plan adds | Residual risk |
|---|---|---|---|
| **Testing Strategy & Coverage (10)** | Four suites, 1779 executed/collected tests, real coverage gates, DB testing beyond the suggested bar | §9 test plan; §8 documents; fix D-5 stale docs; document the missing-DOM rationale | Medium — a weak written strategy understates a strong suite. The plan is the fix |
| **Integrated & Non-Functional Demonstration (15)** | Security scanning (Trivy, ZAP, vulnerable-deps, audit, Dependabot); reliability/idempotency | **K6** (closes the mandatory perf gap), **Selenium** integrated workflow, Grafana as the evidence surface | **Highest-value work.** Today this criterion is materially unreachable |
| **Overall Test Results, Defects & Documentation (15)** | 1240+378+158 executed; coverage artifacts; CI gates | **D-1 as a documented defect→fix→retest cycle**; defect report; execution summary; traceable test-case IDs | Low — this is mostly writing, and the defects already exist |

### 7.2 Individual (60 marks) — the allocation that matters

Rubric weight is 60/100 individual and each student must personally demonstrate a tool. **Allocate ownership explicitly and early.**

| Student | Primary tool ownership | Secondary | Natural domain evidence |
|---|---|---|---|
| **S1** | **K6** — scenario design, thresholds, CI gate, Grafana's K6 dashboard | Grafana alerting pair | Slice 1 (Customer Concierge & Memory): `webhook-ingest` and semantic-memory latency are S1's domain |
| **S2** | **Selenium** — driver setup, Clerk strategy, cross-browser Grid, the integrated workflow test | Accessibility (axe) | Slice 2 (Visual Intelligence): catalog browse/match flows are S2's domain |
| **S3** | **Grafana** — provisioning-as-code, dashboards, Prometheus wiring, alert routing | Python AI-safety tests (prompt-injection, approval-enforcement) | Slice 3 (Commerce/approvals): the approval-state-machine and sign-off workflow are S3's domain |

Why this split works: each tool maps to the slice whose workflow it stresses, so the individual demonstration is naturally tied to owned code — which is exactly what the Technical Contribution criterion (5) and Viva criterion (15) ask for.

| Criterion (marks) | How the allocation satisfies it |
|---|---|
| **Tool/Framework Demonstration (15)** | Each student owns one tool end to end, including *why selected* and *how configured* — the criterion's exact wording |
| **Test Implementation & Execution (15)** | Each student authors scripts with normal/invalid/boundary/failure cases; K6 gives thresholds, Selenium gives validation + boundary inputs |
| **Results, Defects & Retesting (10)** | **D-1 is the training defect**: reproduced failure → fix → retest. Assign D-1 to S3 and D-2 to S2 so each demonstrates the cycle |
| **Technical Contribution (5)** | Test code committed under the owner's name; Selenium/K6 in separate paths from the existing suites |
| **Viva & Technical Understanding (15)** | §10 gives each student a runnable, modifiable artefact and the questions they must be able to answer |

---

## 8. Proposed test plan and documentation templates

### 8.1 Test Plan (structure, with content already knowable)

1. **Scope** — the four codebases and the integrated workflow; what is explicitly out of scope (e.g. WhatsApp provider sandbox, production Clerk instance).
2. **Objectives** — verify business rules; verify authn/authz and tenant isolation; verify the integrated workflow; measure performance against thresholds; discover and fix defects.
3. **Testing areas / types** — the seven-area table from §3.1, each with its tool.
4. **Tools & frameworks** — with the *justification per tool* (this is where K6/Selenium/Grafana are argued, and it is directly marked).
5. **Environment** — `docker-compose` topology; Clerk **development** instance; seeded org/user; Postgres via compose (E2E) and Testcontainers (unit/DB).
6. **Responsibilities** — the §7.2 table.
7. **Schedule** — 2026-09-19 → 2026-10-05, in five phases (§9.1).
8. **Risks & mitigations** — §9.
9. **Exit criteria** — all suites green; no open Critical/High; coverage gates met; k6 thresholds pass; performance evidence captured.

### 8.2 Test Case Document

Align the ID scheme with existing module names so traceability is mechanical. Columns per the PDF: `Test Case ID | Feature | Preconditions | Steps/Input | Expected Result | Actual Result | Pass/Fail`.

Proposed ID scheme: `TC-<AREA>-<NNN>`, e.g. `TC-API-014`, `TC-DB-022`, `TC-WEB-031`, `TC-MOB-008`, `TC-E2E-003`, `TC-NFR-007`, `TC-AI-012`.

**Two pragmatic notes:** (a) do not transcribe 1240 tests by hand — reference the automated report by fully-qualified name and hand-write the ~60–90 cases that carry the narrative; (b) each case must include at least one invalid/boundary/failure variant, because the Individual Implementation criterion explicitly names "normal, invalid, boundary/edge and/or failure cases".

### 8.3 Defect / Bug Report template

`Defect ID | Title | Description | Severity/Priority | Environment | Steps to Reproduce | Expected | Actual | Evidence | Root Cause | Fix | Retest Result | Status`

**Seed it with the defects already found** (§5.1). D-1 and D-2 are ideal: real, verifiable, with a real fix and a real retest command.

### 8.4 Test Execution Summary

`Suite | Tool | Tests executed | Passed | Failed | Skipped | Coverage | Duration | Defects raised | Defects fixed | Conclusion`

Fill from this session's real numbers: .NET 1240/0/0 at 92.6% in 4 m 31 s; Python 381 collected, 378 pass/1 fail/2 skip; web 158/0/0 over a 238-line measured surface; Flutter — *to be run outside the sandbox*.

### 8.5 Tool-Generated Evidence

Already available: Cobertura XML + ReportGenerator HTML, `coverage/lcov.info`, Trivy SARIF, ZAP JSON, `dotnet list --vulnerable` output. To add: k6 JSON/HTML summary + Grafana dashboard screenshots, Selenium HTML reports + per-browser screenshots on failure, the Grafana alert-firing screenshot.

---

## 9. Recommendations, risks and mitigations

### 9.1 Prioritised action list (17 days)

| P | Action | Where | Effort | Rubric impact |
|---|---|---|---|---|
| **P0** | **Fix D-1** (`test_config.py` default provider) and record it as defect + retest evidence | `agnet-service/tests/test_config.py:16` | 15 min | Unblocks CI; seeds the defect cycle |
| **P0** | Add **K6** smoke + load scripts and the CI job with thresholds | new `tests/load/`, `ci.yml` | 1–2 days | Mandatory perf requirement; Group Non-Functional (15) |
| **P0** | Fix the **web coverage exclusion** (D-2) or explicitly document and defend it | `vite.config.ts:20-29` | hours–1 day | Group Coverage (10) / Documentation (15) |
| **P1** | Add **Grafana + Prometheus** to compose with dashboards-as-code; set `Metrics:ScrapeToken` | `docker-compose.yml`, new `observability/` | 1 day | Evidence surface for perf + viva |
| **P1** | **Selenium** project: served `dist`, Clerk dev-instance sign-in, form-validation + protected-route tests, one integrated workflow | new `Aveline.E2E.Tests/`, `ci.yml` | 2–4 days | The mandated integrated workflow; 3 zeroed web areas |
| **P1** | Write the three documents (Test Plan, Test Case Doc, Defect Report) | `docs/reports/` | 1–2 days | Group Documentation (15) directly |
| **P2** | Refresh `docs/tests/README.md` counts (D-5) or delete the stale numbers | `docs/tests/README.md:24,45-48,151` | 30 min | Credibility in viva |
| **P2** | Add accessibility testing (axe via Selenium or Playwright) | as part of Selenium work | 0.5 day | NFR coverage breadth |
| **P2** | Add AI-safety tests: prompt-injection + approval-enforcement | `agnet-service/tests/` | 1 day | AI Testing area |
| **P2** | Verify/fix the eventing meter registration (D-12) | `ObservabilityConfiguration.cs:28` | 30 min | Unlocks the business dashboard |
| **P3** | Add Testcontainers skip guard (D-7); collect `golden_cases_visual.py` (D-11); resolve D-9/D-10 hygiene | test project, `.gitignore` | 2 hours | Demo robustness |

### 9.2 Risks

| Risk | Likelihood | Impact | Mitigation | Signal it has materialised |
|---|---|---|---|---|
| **Selenium × Clerk auth eats 3+ days** | High | High | Timebox to 1 day: if UI sign-in is not working, fall back to storage-state replay, then to the API-key path for the API half. Ship the form-validation and redirect tests first — they need no auth at all | No green Selenium test by day 4 |
| **K6 finds a real bottleneck you cannot fix** | Medium | Medium | This is a **good outcome**: report it as a defect with the measurement. The rubric rewards interpretation, not a green number. Do not lower a threshold to hide a finding | A threshold fails repeatedly across runs |
| **Grafana becomes a yak-shave** | Medium | Medium | Provision as code, import dashboard 19665 for K6, keep the API dashboard to RED metrics only. Do not add Loki/Tempo | No dashboard by day 7 |
| **Flutter `integration_test` is hard on CI** | High | Medium | Flutter E2E needs a device/emulator — do **not** put it on the critical path. Add `integration_test` as dev-dep and run widget-integration tests headless; justify the limitation in writing | Emulator flakiness in CI |
| **AI approval-enforcement cannot be tested because the node is a stub** | **Certain** | Medium | The commerce approval node returns `needs_approval: False` as an explicit stub (`concierge_workflow.py:206-225`) and the commerce agent directory holds only a README. Either implement it or **test the stub's contract and state the limitation** — do not claim approval testing you cannot do | Any claim of approval testing against a stub |
| **Privileged-surface risk in ZAP/crawler scope** | Low | Low | ZAP currently scans only unauthenticated routes. If you add an authenticated scan, keep it against the dev instance, never production Clerk keys | Production `pk_live_`/`sk_live_` keys appear anywhere in the repo |
| **17 days is tight for 3 tools + 5 documents + 3 viva preps** | High | High | Frozen split (§7.2), one tool per student, P0 first. Cut P3 before P1 | Any P0 item still open at day 10 |

### 9.3 Additional tooling — justified, not decorative

| Tool | Verdict | Reason |
|---|---|---|
| **Testcontainers** | ✅ Already present and well used | Do not add more; the gap is the missing skip guard |
| **`@testing-library/react` + `jsdom`** | ✅ **Add** | Cheapest way to get DOM interaction for component/form tests at unit speed, leaving Selenium for the E2E layer |
| **MSW** | ✅ **Add** | The web suite fakes axios adapters and `vi.mock`s the client; MSW gives realistic network-boundary tests for error/loading states |
| **Playwright** | ⚠️ Optional | Better than Selenium technically, and the right tool for cross-browser + a11y; add only if Selenium stalls, and justify the choice explicitly |
| **axe-core** | ✅ **Add** | Accessibility is a named NFR type and currently absent |
| **promptfoo / DeepEval** | ✅ **Add** | AI Evaluation is a scored area with no harness; promptfoo's prompt-injection tests also close an AI-safety gap |
| **Flutter `integration_test` + `mocktail`** | ✅ **Add** | Closes the Flutter E2E and mocking gaps; keep off the CI critical path |
| **BenchmarkDotNet / NBomber** | ❌ Skip | K6 covers the rubric's performance requirement; a second perf tool splits attention and adds no marks |

---

## 10. Viva demonstration guide

Each student needs a **runnable, modifiable artefact** and the ability to answer *why*, *how configured*, *what the result means*, and *how the system improved*. The rubric explicitly says a student may be asked to **run, modify or troubleshoot a test live**.

### S1 — K6 (+ Grafana alerting pair)

- **Demonstrate:** `cd tests/load && k6 run k6-smoke.js` (fast, passes) then `k6 run k6-load.js` with Grafana's k6 dashboard on screen via the `testid` filter.
- **Explain why K6:** JS scripts reviewable as code; no JVM; **thresholds control the exit code**, which is what makes a gate real.
- **Explain configuration:** scenarios and executors; the threshold expressions and their aggregation types; `K6_PROMETHEUS_RW_*` env vars and why remote-write is *experimental*; `--tag testid` for run separation.
- **Explain a result:** read p(95)/p(99) and the error rate, and **separate the LLM-bound `agent-query` path from the rule-based path** — bimodal latency is the interesting finding.
- **Be able to modify live:** change `p(95)<500` to a failing value and show the non-zero exit; add a `checks` assertion and explain why it alone would not gate anything.
- **Trap to expect:** "Why isn't the Clerk login flow under load?" → because token minting is out of band and deliberately so; the API-key and scrape-token paths are the honest measurable surface.

### S2 — Selenium (+ accessibility)

- **Demonstrate:** run the form-validation and protected-route tests (no auth needed — safest live demo), then the authenticated integrated workflow.
- **Explain why Selenium:** the existing suite has **no DOM** — `renderToString` only — so events, validators and redirects were untestable; Selenium is the named tool that closes that.
- **Explain configuration:** **Selenium Manager** (no driver binaries since 4.6); Grid for the browser matrix; the `[Collection]` isolation added so browser sessions do not race the 1240 existing tests; the Clerk dev-instance + storage-state strategy and its trade-off.
- **Explain a result:** point at a screenshot-on-failure and the exact assertion that caught a redirect regression.
- **Be able to modify live:** change an expected redirect target and show the test fail with a clear message.
- **Trap to expect:** "Why not Playwright?" → give the honest answer: Playwright is better engineering and would be the choice on a real team; Selenium is what the assignment names and what the team standardised on, and the DOM-interaction gap is what mattered. Then mention you can show the same test in Playwright if asked.

### S3 — Grafana (+ AI-safety tests)

- **Demonstrate:** `docker compose up grafana prometheus`, open Grafana, show the provisioned dashboards (RED, event bus, K6), trigger a threshold failure and show the alert firing.
- **Explain why Grafana:** the API **already** exposes `/metrics` with a Prometheus exporter and an auth policy — so Grafana is the surface that makes the rest of the testing **visible and reviewable**, not a new instrumentation project.
- **Explain configuration:** provisioning-as-code (dashboards committed, not clicked); the scrape job's `Bearer` scrape token against `MetricsPolicy`; `--web.enable-remote-write-receiver` for k6; the k6 dashboard import (19665).
- **Explain a result:** read a latency histogram and connect it to the K6 finding; show an alert rule and its evaluation interval.
- **Be able to modify live:** add a panel or change an alert threshold and show it take effect.
- **Trap to expect:** "Do these metrics actually exist?" → now you must know D-12: `aveline.events.*` likely does **not** export because the meter `"Aveline.Api.Eventing"` is not registered with `AddMeter`. Answering that correctly is a strong viva moment; bluffing is a fatal one. Also be ready on the `change-me-internal-token` default protecting `/metrics`.

### Cross-cutting viva preparation

- Every student must be able to name their **own** tests by file and method, and state the **normal / invalid / boundary / failure** case each covers.
- Assign **D-1** (Python default provider) and **D-2** (web coverage exclusion) as the two worked defect→fix→retest stories; keep the retest command and its output.
- Rehearse the **cross-platform workflow** verbally: Flutter/React → API → LangGraph agents → Postgres/pgvector → back. The rubric rewards connecting results to the actual SE3090 system.
- Declare AI assistance per the repo's own format (`docs/ai-usage/README.md` — Tool / Task / Prompts / Output / What I changed / Reflection). The assignment requires declaration under the CLEAR framework and prohibits submitting work you cannot explain.
- **Do not present `docs/tests/README.md` numbers** (569 cases, one widget test) — they are stale (D-5). Use the executed figures in §4.

---

## 11. Open questions

These need a human decision; research cannot settle them.

1. **Tool ownership.** Is the §7.2 allocation (S1=K6, S2=Selenium, S3=Grafana) accepted? The individual mark is 60/100 and each student must demonstrate personally.
2. **Selenium vs Playwright.** Selenium is what was asked and what the rubric names; Playwright is technically stronger and better supported for Clerk. Confirm the named tool, or authorise a documented comparison.
3. **Clerk test-instance credentials.** Is a dedicated dev instance with seeded test users available, and who owns the keys? Selenium's authenticated workflow depends on it.
4. **Grafana deployment target.** Local `docker-compose` only, or a hosted instance for CI-visible dashboards? This changes the CI design.
5. **The `.merge-work*/` trees.** They are untracked and contain a k6 file. Are they intended deliverables, or scratch? If the k6 script is real work, it should move into the tracked tree.
6. **Appetite for implementing the commerce approval agent.** Approval-enforcement testing is impossible while the node is a stub. Implement, or test the stub's contract and document the limitation?
7. **Whether to raise the .NET coverage gate.** Current line coverage is 92.6% against a 30% gate. Raising it is free, visible rigour — but only if the team can keep it green under deadline pressure.

---

## Appendix A — Reproducer commands

```bash
# Backend: 1240 tests + coverage
dotnet test Aveline.Api/Aveline.Api.sln -c Release --collect:"XPlat Code Coverage" --results-directory TestResults

# Python agent service (currently 1 failure = D-1)
cd agnet-service && ./.venv/bin/python -m pytest tests/ -q
cd agnet-service && ./.venv/bin/python -m pytest tests/ --cov=app --cov-report=term --cov-fail-under=90

# Web (158 tests) and coverage
cd frontend/web && bun run test
cd frontend/web && bun run test:coverage

# Flutter (requires a writable Flutter SDK cache)
cd frontend/aveline_mobile && flutter analyze --no-fatal-infos && flutter test --coverage

# Metrics surface (note: set Metrics:ScrapeToken first)
curl -s -H "Authorization: Bearer $METRICS_SCRAPE_TOKEN" http://localhost:5091/metrics | grep -E '^aveline_'
curl -s http://localhost:5091/health/ready

# CI history (not available locally)
gh run list --workflow=ci.yml --limit 30
```

## Appendix B — Evidence index

| Claim | Source |
|---|---|
| Assignment requirements and full rubric | `SE3110 Assignment.pdf` (pdftotext extraction) |
| 1240 tests pass, 4 m 31 s | `dotnet test` output, this session |
| .NET coverage 92.6% line / 66.2% branch | `TestResults/*/coverage.cobertura.xml`, `line-rate=0.9256 lines=81723/88288` |
| Web 158 tests pass; 91.59% over 238 lines | `vitest run --coverage`, `coverage/coverage-summary.json` |
| Web coverage excludes UI source | `frontend/web/vite.config.ts:20-29`; 111 excluded files / 16,946 lines |
| Python 378 pass / 1 fail / 2 skip | delegated `pytest` run, `tests/test_config.py:16` |
| No performance/load tooling in tracked tree | `grep -riE "k6\|jmeter\|artillery\|NBomber\|BenchmarkDotNet"`; `docs/backend/README.md:297` |
| CI jobs, gates and thresholds | `.github/workflows/ci.yml:20,53,118,153,199,272,334,395`; `:79-92,141,174,223,310-321,338` |
| Prometheus exporter and `/metrics` | `Aveline.Api/Program.cs:138`; `ObservabilityConfiguration.cs:26-31` |
| Collector pipeline is traces-only; no Grafana/Prometheus | `otel-collector-config.yaml:21-26`; `docker-compose.yml` services |
| Eventing meter mismatch | `EventBusMetrics.cs:26` vs `ObservabilityConfiguration.cs:15,28`; scrape probe produced no `aveline_events_*` |
| Scrape/internal/API-key auth surfaces | `ScrapeTokenAuthenticationHandler.cs:13,26,37`; `InternalTokenAuthenticationHandler.cs:9`; `ApiKeyAuthenticationHandler.cs:41`; `AuthorizationConfiguration.cs:93-99,118-124` |
| Health endpoints anonymous | `HealthEndpoints.cs:15,23,24` |
| Web suite renders to string only | `Composer.test.tsx:2` + 6 others; zero hits for `createRoot\|fireEvent\|act(` |
| Web routes and guards untested | `src/App.tsx:35-67`; `ProtectedRoute.tsx:14-16`; `RedirectIfAuthenticated.tsx:18-20`; `RequireAccountState.tsx:20-26` |
| Flutter 88 test files, no `integration_test` | `frontend/aveline_mobile/test/**`; `pubspec.yaml:55-64` |
| Commerce approval node is a stub | `app/workflows/concierge_workflow.py:206-225`; only READMEs in `app/agents/commerce/`, `app/tools/commerce/` |
| Stale test documentation | `docs/tests/README.md:24,28,45-48,151,219-224` |
| k6 threshold/exit-code semantics | [k6 Thresholds](https://grafana.com/docs/k6/latest/using-k6/thresholds/) |
| k6 remote write is experimental; dashboard 19665 | [k6 Prometheus remote write](https://grafana.com/docs/k6/latest/results-output/real-time/prometheus-remote-write/) |
| Selenium Manager removes driver binaries | [Introducing Selenium Manager](https://www.selenium.dev/blog/2022/introducing-selenium-manager/) |
| Clerk testing: test keys, bypass token, storage state | [Clerk test helpers](https://clerk.com/docs/guides/development/testing/playwright/test-helpers) |
