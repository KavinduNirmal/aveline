# Slice-2 Integration Review Report

**Date:** 2026-09-13
**Integration Branch:** `integration/slice-2-to-slice-1`
**Merged Branches:** `Catalog` (#233, `origin/catalog` @ `03421a1`), `Feature/visual insight agent` (#175, `origin/feature/visual-insight-agent` @ `daa945b`), plus `origin/development` @ `62a12fe`
**Final Commit:** `bf1446b` (integration head; this report is a follow-up `docs(reports):` commit on the same branch)
**Reviewer:** DeepSeek Harness coding agent
**Method:** real merges on a real working tree, real builds and real test runs (Testcontainers + PostgreSQL, pgvector/pg16), static analysis and dependency audits. Every claim below cites a file and line or a command output.

---

## 1. Executive Summary

The two slice-2 branches are integrated and the resulting branch is **green across all four test suites**. Slice-2 (Visual Intelligence & Sourcing — "Elle") is substantially implemented for the .NET API, the Python agent service and the React web dashboard.

**Overall assessment: Ready for the next stage, with two must-fix items.**

| Dimension | Verdict |
|---|---|
| Integration | ✅ Complete, conflict-free, all three source branches contained |
| Build | ✅ 0 errors (26 warnings, all pre-existing/benign) |
| Tests | ✅ 1231 (.NET) + 379 (Python) + 158 (Web) + 81 (Flutter) all passing |
| Coverage | ✅ .NET 92.2 % repo-wide / 95.5 % slice-2 module; Python 92.5 %; Web 91.6 %; Flutter 62.1 % |
| Security | ⚠️ 1 High (unauthenticated AI endpoint), several Medium |
| Code integrity | ⚠️ Real but non-blocking: dead mocks, orphan entity, fabricated data |
| Conventions | ⚠️ Error-body contract violated on tenant-facing catalog endpoints |
| Previous findings | ✅ No regressions; all 5 blocking slice-2 items from the previous report are fixed |

**Two must-fix items before merge to `development`:**

1. **`POST /api/v1/orgs/{organizationId}/catalog/analyze-image` is `.AllowAnonymous()`** (`Aveline.Api/Endpoints/CatalogEndpoints.cs:188`). Any unauthenticated caller can invoke the paid Vision AI provider and burn an arbitrary organization's usage budget.
2. **`docker-compose.yml` was unparseable on `development`** (duplicate YAML keys) and the EF snapshot did not match the merged model. Both are fixed on this branch (see §2.3), but the underlying defects should also be fixed upstream on `development` and in PR #233 respectively.

> **Resolution addendum (post-review).** Both must-fix items above have been actioned on this branch, which is the vehicle that carries them upstream into `development`:
>
> - **Item 1 — fixed** in `7bb2a9f`. `.AllowAnonymous()` was removed from `CatalogEndpoints.cs` so the route inherits the group's `BoutiqueAccessPolicy`; 401/403 response metadata was declared, and a regression test (`AnalyzeImage_WithoutAuth_ReturnsUnauthorized`) was added. The test was verified to fail against the previous code with `Expected 401, found 200`, confirming the exposure was real.
> - **Item 2 — fixed** in this branch's integration commits (§2.3). Because PR #233 is superseded and closed, the snapshot correction lands here rather than in #233; once this branch reaches `development`, the duplicate-key defect is resolved upstream as well.
>
> The remaining P1/P2 items in §8 are **not** addressed and stay open for the owning slices.

---

## 2. Integration Details

### 2.1 Deviation from the task brief (documented assumption)

The brief named the integration branch `integration/slice-1-to-slice-2`. **That branch does not exist** locally or on `origin`. The only integration branch is `integration/slice-2-to-slice-1` (the name is reversed). This was raised with the requester, who approved using the existing branch. Per the same decision, **nothing was pushed** — all work is local-only commits.

Because commits are local-only, the catalog merge was amended once (§2.3, item 3); `origin/integration/slice-2-to-slice-1` still points at `902f27f`.

### 2.2 Topology discovered

| Ref | Commit | Relationship |
|---|---|---|
| `origin/development` | `62a12fe` | 1 commit ahead of the integration branch base `a2adc22` |
| `origin/feature/visual-insight-agent` (#175) | `daa945b` | Base = `a2adc22` (stale development) |
| `origin/catalog` (#233) | `03421a1` | **Contains #175 entirely** (`daa945b` is an ancestor) + 8 further commits |
| `integration/slice-2-to-slice-1` (before) | `902f27f` | Based on `a2adc22`; had merged an *older* visual state (`4b6b2e1`) |

Key consequence: **#233 is a superset of #175.** Merging `origin/catalog` brings in the whole visual-insight-agent branch. The subsequent `git merge origin/feature/visual-insight-agent` correctly reported `Already up to date.`, and `git merge-base --is-ancestor origin/feature/visual-insight-agent HEAD` confirms containment. This is a legitimate no-op, not a skipped step.

### 2.3 Steps taken and conflicts resolved

| Step | Result |
|---|---|
| `git checkout integration/slice-2-to-slice-1` | Clean (0 tracked modifications) |
| `git merge origin/development` | 3 conflicts → commit `0b207d1` |
| `git merge origin/catalog` | 4 conflicts → commit `bf1446b` (amended from `8604c78`) |
| `git merge origin/feature/visual-insight-agent` | Already up to date (contained) |

**Conflicts and resolutions**

| File | Nature | Resolution |
|---|---|---|
| `Aveline.Api/Program.cs` (dev merge) | Additive: visual usings vs statistics usings; `MapVisualEndpoints()` vs `MapStatisticsInternalEndpoints()` | Union — kept all four statistics/system-health usings, added `VisualIntelligence`, and mapped visual, statistics-internal **and** controllers |
| `Aveline.Api/Common/Middleware/OnboardingMiddleware.cs` (dev merge) | Both sides widened the "skip onboarding stub" predicate | Union — kept `/api/internal` path check (visual) **and** the `ApiKeyId` claim check (development) |
| `Aveline.Api/Migrations/AppDbContextModelSnapshot.cs` (dev merge) | Generated file; 8 interleaved conflicts splitting mid-entity-block | Not hand-merged. Regenerated from the merged model with EF and verified |
| `.gitignore` (catalog merge) | Additive: `docs/.obsidian` vs `Agents.md` | Union — kept both |
| `Aveline.Api/Program.cs` (catalog merge) | Additive: 5 existing `Map*Endpoints()` vs `MapCatalogEndpoints()` | Union — kept all six |
| `agent-service/tests/test_concierge_workflow.py` (catalog merge) | Duplicate re-ordering of the same test block | Took `origin/catalog` — verified it is a strict superset (20 test functions, contains every test from the other side) |
| `docker-compose.yml` (catalog merge) | `development` block vs catalog's Vision block; auto-merge also duplicated the embeddings+Vision block | Rewrote the region once (see item 2 below) |

**Snapshot procedure (why it is trustworthy).** A naive text union of the snapshot would have produced invalid C#, because the conflict hunks cut through the middle of `modelBuilder.Entity(...)` blocks. Instead the snapshot was regenerated from the merged model using a throwaway migration (`dotnet ef migrations add __Temp…`, then deleting the migration files and keeping the regenerated snapshot). This was validated with:

```
dotnet ef migrations has-pending-model-changes --project Aveline.Api
→ "No changes have been made to the model since the last migration."
```

The `VisualIntelligencePostgresTests` suite also applies `MigrateAsync()` to a fresh `pgvector/pgvector:pg16` container, so the whole migration chain is exercised for real in the green .NET run.

### 2.4 Defects found and fixed during integration

These were **not** merge conflicts; they were found by verification and are fixed on the branch.

1. **EF model/snapshot drift in PR #233 (must-fix, fixed).**
   `SourcingRequestConfiguration.cs:18-22` maps `Category`/`Color`, and `SourcingRequest.cs:23-24` declares them as non-nullable `string` under `<Nullable>enable</Nullable>` — so the model requires them. But commit `d4ef26a` hand-edited the generated artifacts to make them **nullable**: the migration created `nullable: true` columns and the snapshot omitted `.IsRequired()`. `dotnet ef migrations has-pending-model-changes` therefore reported pending changes on `origin/catalog`. A fresh database would have been created with nullable columns while the model expected NOT NULL.
   **Fix:** amended `Aveline.Api/Migrations/20260910064834_AddVisualIntelligenceEntities.cs:70-71` to `nullable: false`, added `.IsRequired()` to the matching `.Designer.cs` block, and regenerated `AppDbContextSnapshot.cs`. The API path is safe because `CreateSourcingRequestDto.Category/Color` are non-nullable with `= string.Empty` (`CreateSourcingRequestDto.cs`), so NULL is never written.
   *Caveat:* any developer who already applied the old visual migration must recreate the slice-2 tables (`dotnet ef database drop`), because an amended migration will not re-run.

2. **`docker-compose.yml` was invalid YAML on `development` (must-fix, fixed).**
   `docker compose config` failed hard: `mapping key "Embeddings__ApiKey" already defined at line 87`. The duplicate already exists on `origin/development` (it defines `Embeddings__ApiKey` twice, lines 87 and 90). The catalog merge then duplicated the embeddings+Vision block a second time. The block was rewritten to a single embeddings definition (matching `.env.example:108-110`, `EMBEDDINGS_BASE_URL=…/v1`) plus the Vision block from catalog. `docker compose config --quiet` now passes.
   *Note:* the `:?EMBEDDINGS_API_KEY is required` guard from development was dropped in favour of the `:-` default, because there is **no** startup guard for embeddings in the API (only `TelemetrySecurityGuard.EnsureIpHashSaltForProduction`) and `.env.example` ships it empty; the `:?` form would have made `docker compose up` fail for every developer. This is a deliberate, documented choice — see Open Questions.

3. **Accidental inclusion of unrelated untracked files (fixed).**
   `git add -A` swept five pre-existing untracked workspace files (`opencode.json`, `session-ses_f782.md`, and three generated Flutter Windows plugin files) into the catalog merge commit. The merge commit was amended to drop them; both merge parents were preserved (`bf1446b` → `0b207d1`, `03421a1`). The files remain on disk, untracked, exactly as before.
   *Follow-up:* these generated Flutter files are neither tracked nor gitignored — a repo-hygiene gap (see §6.3).

### 2.5 Final state

```
git branch --show-current      → integration/slice-2-to-slice-1
git rev-parse HEAD             → bf1446b2262b62669b26a39827b6785bc4f22658
tracked modifications          → none
merged containment             → origin/development, origin/catalog, origin/feature/visual-insight-agent all CONTAINED
```

History clearly shows both merges:

```
bf1446b Merge remote-tracking branch 'origin/catalog' into integration/slice-2-to-slice-1
0b207d1 Merge remote-tracking branch 'origin/development' into integration/slice-2-to-slice-1
62a12fe feat(admin): admin backend API with reconciliation and hardening fixes (#244)
```

---

## 3. Implementation Review Against Slice-2 Docs

**Scope anchor.** The canonical slice-2 specification is `docs/architecture/visual-intelligence.md` ("Visual Intelligence & Sourcing Architecture (Slice 2 — Elle)"), supported by `docs/reports/slice2-convention-alignment.md`, `docs/ADR/ADR-020-multimodal-vision-provider.md`, `docs/ADR/ADR-009`, `docs/ADR/ADR-010` and `docs/api/README.md:660-696`. The large billing/statistics body of `docs/backend/backend-requirements.md`, `domain-model.md` and `statistics-catalog.md` belongs to a **later** slice and is out of slice-2 scope (its only slice-2 touchpoint, defect D-4, is noted below).

Status key: ✅ Implemented · ⚠️ Partial · ❌ Missing · 🚫 N/A

### 3.1 Domain & persistence

| Req | Status | Evidence |
|---|---|---|
| `InventoryItem`, `InventoryImage`, `CustomerMatch`, `OutfitComposition`, `OutfitItem`, `SourcingRequest`, `Supplier` (7 entities, one class per file) | ✅ | `Aveline.Api/Modules/VisualIntelligence/Models/`, `AppDbContext.cs:26-32` |
| EF configurations for all 7 | ✅ | `Aveline.Api/Infrastructure/Data/Configurations/*Configuration.cs` |
| Single migration stream on `AppDbContext` | ✅ | `Aveline.Api/Migrations/20260910064834_AddVisualIntelligenceEntities.cs` (7 `CreateTable`, 7 `DropTable` in `Down()`) |
| `InventoryImage` is actually used | ⚠️ | Entity + config + table exist, but **no** repository, service or endpoint reads/writes it; 0 % coverage (`Models/InventoryImage.cs`) |
| `SourcingRequest.Category`/`Color` persisted | ✅ | `SourcingRequestConfiguration.cs:18-22`; migration `:70-71` (fixed by PR #233 and corrected in §2.4.1) |

### 3.2 Internal service-to-service API (`/internal/visual`)

All **12** documented endpoints are mapped under `/internal/visual` with `.RequireAuthorization(InternalServicePolicy)` (`VisualEndpoints.cs:21-23`). See `docs/architecture/visual-intelligence.md` §2 and `docs/api/README.md:677-688`.

| Req | Status | Evidence |
|---|---|---|
| 12 documented internal endpoints present | ✅ | `VisualEndpoints.cs:25-106` |
| Auth restricted to `X-Internal-Token` (ADR-009) | ✅ | `VisualEndpoints.cs:23`; `X-Internal-Key` fallback removed |
| Canonical `/internal/visual` prefix in Python | ✅ | `agent-service/app/tools/registry.py:165-225` |
| Route surface registered once | ⚠️ | Legacy alias groups retained: `/internal/visual`, `/api/internal/visual`, `/internal/inventory` → 32 mappings for 12 handlers (`VisualEndpoints.cs:21,109,128`) |

### 3.3 Python "Elle" sub-graph

| Req | Status | Evidence |
|---|---|---|
| `build_visual_graph` exists and runs | ✅ | `app/agents/visual_insight/graph.py:15-39` |
| **LLM wired into the production path** (previous blocker B1) | ✅ | `app/workflows/concierge_workflow.py:179-180` |
| Conditional compose-vs-sourcing routing | ✅ | `graph.py:33-38`, `_route_after_looks:44-48` |
| Runtime schema validation (`extra="forbid"`) | ✅ | `app/schemas/visual_insight.py:90`, `coerce_visual_output:113-118` |
| Graph topology matches the documented IntentGate → branches → FormatResponse design | ⚠️ | Implementation is a linear pipeline; no intent-gate node / per-intent branching (`graph.py:21-39`) |
| `VisualIntentGate` used in production | ⚠️ | Defined and exported, but only tests reference it (dead code) |
| Multi-tenant caches wired and invalidated | ⚠️ | `InventorySearchCache` / `ProductAnalysisCache` exist with tests, but **no production caller** in `app/tools/` or `app/agents/visual_insight/` |
| ADR-010 usage reported per visual run (defect **D-4**) | ⚠️ | `nodes.py:377-382` hardcodes `provider="openai"`, `model="visual-llm"` and uses `customer_id` as `request_id` |
| Vision usage tenant-attributed | ⚠️ | `app/tools/inventory/image_tools.py:36-37` does not forward `org_id`; the registry falls back to the all-zero GUID (`registry.py:176-179`) |

### 3.4 Vision / image analysis (ADR-020)

| Req | Status | Evidence |
|---|---|---|
| Centralised `IVisionService` (OpenAI-compatible) | ✅ | `Modules/VisualIntelligence/Services/VisionService.cs` |
| `Vision:BaseUrl` / `Vision:Model` configuration | ✅ | `appsettings.json:76-79`; `.env.example:115-117`; `docker-compose.yml` (added during this integration) |
| Deterministic offline fallback | ✅ | `VisionService.cs:201-257` — verified by `VisionServiceTests` (91-95 % coverage) |
| ADR-010 usage recorded for vision | ✅ | `VisionService.cs:131-151` |
| Python routes vision via `/internal/visual/analyze-image` | ✅ | `registry.py:174-180`; `image_tools.py`; `nodes.py:205-225` |

### 3.5 Tenant-facing Catalog API and React UI

17 routes are mapped under `/api/v1/orgs/{organizationId:guid}/catalog` (`CatalogEndpoints.cs:20-22`, mounted at `Program.cs:168`), and the React dashboard is built on them (`frontend/web/src/lib/catalog-api.ts`, 16 client functions).

| Feature | Status | Evidence |
|---|---|---|
| Inventory list / search / filter | ✅ | `CatalogEndpoints.cs:26-75`; `InventoryTab.tsx` |
| Item create / edit / status | ✅ | `CatalogEndpoints.cs:98-159`; `AddProductModal.tsx` |
| Vision attribute extraction from the UI | ✅ | `CatalogEndpoints.cs:178`; `VisualAttributesBadge.tsx` |
| Customer matches | ✅ | `CatalogEndpoints.cs:196-228`; `CustomerMatchesDrawer.tsx` |
| Lookbooks / outfit composition | ✅ | `CatalogEndpoints.cs:232-261`; `ComposeOutfitModal.tsx`, `LookbooksTab.tsx` |
| Sourcing pipeline | ✅ | `CatalogEndpoints.cs:265-317`; `SourcingTab.tsx` |
| Suppliers | ⚠️ | `CatalogEndpoints.cs:321-351`. `GetSupplierCatalogAsync` returns **hardcoded** items (`VisualService.cs:345-373`) |
| Paginated envelope on list endpoints | ❌ | `GET /items` returns a bare `IReadOnlyList<InventoryItemDto>` (`CatalogEndpoints.cs:52-57`), against `docs/api/README.md:260` "New endpoints never return a bare array" |
| Catalog UI component tests | ❌ | No `components/catalog/*` tests; see §4.1 |

### 3.6 Planner entitlements and rate limiting

| Req | Status | Evidence |
|---|---|---|
| `agents.visual` plan entitlement gating | ❌ | No entitlement reference anywhere in `CatalogEndpoints.cs` or `Modules/VisualIntelligence/**` (docs assign it in `pricing_plan.md:297`, `domain-model.md:422`) |
| Rate limiting on visual/catalog routes | ❌ | None (see §5, M-5) |

Overall: **~85 % of documented slice-2 surface is implemented**, concentrated on the API, agent service and web dashboard. The material gaps are the Flutter client, entitlement gating, cache wiring, and the hardcoded supplier catalog.

---

## 4. Test Coverage Analysis

### 4.1 Results (all suites run on `bf1446b`)

| Suite | Command | Result | Coverage | Gate |
|---|---|---|---|---|
| .NET API | `dotnet test Aveline.Api/Aveline.Api.sln -c Release` | ✅ **1231 passed, 0 failed, 0 skipped** (3 m 26 s) | **92.2 %** repo-wide (77623/84150) | 30 % ✅ |
| Python agent | `pytest tests/ --cov=app --cov-fail-under=90` | ✅ **379 passed, 2 skipped, 0 failed** | **92.49 %** | 90 % ✅ |
| Web | `bun run test:coverage` | ✅ **158 passed / 25 files** | **91.59 % lines** | 80 % ✅ |
| Flutter | `flutter test --coverage` | ✅ **81 passed** | **62.1 %** (819/1319) | none |
| Flutter static | `flutter analyze --no-fatal-infos` | ✅ **No issues found** | — | — |

**Slice-2 .NET module coverage: 95.5 % (4009/4198 lines)** — well above the repo average.

| Slice-2 file | Coverage |
|---|---|
| `Endpoints/CatalogEndpoints.cs` | 92 % (280/304) |
| `Services/VisionService.cs` | 91-95 % |
| `Services/VisualService.cs` | 85 % (199/234) |
| `Repositories/CustomerMatchRepository.cs` | 86 % |
| `Repositories/InventoryRepository.cs` | 89 % |
| `Repositories/OutfitRepository.cs` | 54 % |
| `Models/OutfitItem.cs` | 45 % |
| `Models/InventoryImage.cs` | **0 %** (orphan entity) |
| `DTOs/OutfitCompositionDto.cs`, `DTOs/SupplierDto.cs` | **0 %** |

### 4.2 Methodological caveats (important)

1. **A naive Python run reports a false failure.** The first unscoped run showed `tests/test_config.py::test_defaults_are_sane` failing with `assert 'deepseek' == 'openai'`. Root cause is **ambient sandbox environment variables** (`LLM_PROVIDER=deepseek`, `LLM_BASE_URL`, `LLM_MODEL`, …) which `pydantic-settings` reads ahead of defaults (`app/core/config.py:25,52`). With those variables unset the suite is green (379 passed). CI has no such env block, so **CI is green** — this is an environment artifact, not a branch defect.
2. **Pipeline exit codes were masked.** Both the `.NET` and Python commands were run through `| tail`, so the pipeline's exit code reflects `tail`, not the test runner. Results above come from reading the runner's own summary line, and the Python suite was re-run with `${PIPESTATUS[0]}` captured explicitly.
3. **Web coverage excludes all components.** `frontend/web/vite.config.ts:20-27` sets `coverage.exclude` to include `src/components/**` and `src/contexts/**`. The 91.59 % figure therefore measures `src/lib` + `src/types` only. **The entire new Catalog UI is unmeasured and untested** — the four `components/catalog` regressions possible here would not be caught. `src/lib/catalog-api.ts` is at 87.93 % lines (uncovered: 116, 264-270).
4. **Flutter coverage is not gated** by CI (upload only), so 62.1 % does not fail the build.

### 4.3 Slice-2 test inventory

- **.NET (9 files):** `CatalogEndpointsIntegrationTests`, `VisualEndpointsIntegrationTests`, `VisionServiceTests`, `VisualIntelligencePostgresTests`, `VisualIntelligenceEntityConfigurationTests`, `CustomerMatchRepositoryTests`, `InventoryRepositoryTests`, `InventoryServiceTests` — plus `Aveline.Api.Tests.csproj`.
- **Python (15 files):** `tests/agents/test_visual_insight_{graph,agent,intent_gate}.py`, `tests/test_visual_{agent,routing,insight_schemas}.py`, `tests/tools/test_{visual,inventory}_tools.py`, `tests/test_{block_builders,inventory_search_cache,product_analysis_cache,llm_runtime}.py`, etc.
- **Web (1 file):** `src/lib/catalog-api.test.ts` (13 tests).
- **Flutter:** none for catalog (the catalog screen is a placeholder).

### 4.4 Comparison with previous reports

| Report | Then | Now |
|---|---|---|
| `slice2-elle-verification.md` | .NET **503 passed / 2 failed** | ✅ 1231 passed / 0 failed |
| `slice2-elle-verification.md` | Python 298 passed / 2 skipped / 1 env failure | ✅ 379 passed / 2 skipped / 0 failed |
| `slice2-elle-verification.md` | Frontend vitest 8/8 | ✅ 158 passed |
| `admin-backend-api-*.md` | .NET suite green but coverage unstated | ✅ 92.2 % repo-wide |

Coverage **gaps that remain**: catalog UI components (0 % measured), `InventoryImage` (0 %), `OutfitRepository` (54 %), and no test asserts the ADR-010 usage payload emitted by the visual agent (§3.3, D-4).

---

## 5. Security Assessment

### 5.1 Findings

| # | Severity | Finding | Evidence |
|---|---|---|---|
| **S-1** | **High** | **Unauthenticated paid-AI endpoint.** `POST /api/v1/orgs/{organizationId}/catalog/analyze-image` carries `.AllowAnonymous()`, overriding the group's `RequireAuthorization(BoutiqueAccessPolicy)`. Any anonymous caller can drive the paid Vision provider and consume an arbitrary organization's quota/budget (the `organizationId` comes straight from the route). It also contradicts the class contract ("All routes … guarded by BoutiqueAccessPolicy", `CatalogEndpoints.cs:12-14`). Introduced by `beb3459` (PR #233). No test covers the route. | `Aveline.Api/Endpoints/CatalogEndpoints.cs:178-192` (`.AllowAnonymous()` at `:188`) |
| **S-2** | Medium | **Weak internal-token default shipped in three profiles.** `INTERNAL_API_TOKEN` defaults to `change-me-internal-token`, which the agent service *explicitly rejects* at startup (`validate_startup_settings`) — so a default-configured dev stack is internally inconsistent, and the value is a known shared secret. No startup guard on the .NET side. | `Aveline.Api/appsettings.Development.json:7`; `docker-compose.yml:80,138`; `.env.example:35` |
| **S-3** | Medium | **No application-level rate limiting anywhere.** Zero `AddRateLimiter`/`UseRateLimiter` registrations. Combined with S-1 this makes the AI endpoint trivially abusable for cost/DoS. | repo-wide grep over `Aveline.Api/**/*.cs` |
| **S-4** | Medium | **Unbounded pagination + in-memory paging.** `SearchInventoryDto.PageSize` has no upper clamp, and `SearchAsync` loads **all** matching rows into memory before `Skip/Take`. A client can request an enormous page and force a full-table materialisation. | `DTOs/SearchInventoryDto.cs`; `Repositories/InventoryRepository.cs:59-78` |
| **S-5** | Medium | **WhatsApp webhook replay not enforced at the DB level.** `InboundMessageLog.ExternalId` has no unique index (only `(OrganizationId, ReceivedAt)` and `OrganizationId`), so duplicate provider redeliveries are not deduplicated by constraint, and there is no `DbUpdateException` catch. Rate-limiting-after-signature and constant-time signature compare *are* correctly implemented. Provenance: integrations/development, not the slice-2 PRs. | `Infrastructure/Data/Configurations/InboundMessageLogConfiguration.cs:23-40` |
| **S-6** | Low | **Raw SQL via interpolated strings (EF1002 ×2).** Both sites interpolate server-side computed values only (`rawDays` from configuration; a computed `DateOnly`), so they are **not** currently injectable, but the analyser flags them and they are one refactor away from danger. Provenance: statistics/development. | `Modules/Statistics/Jobs/ApiStatsRetentionJob.cs:42`; `Modules/Statistics/Jobs/ApiRequestLogPartitionJob.cs:40` |
| **S-7** | Low | **User-supplied image URL forwarded to the AI provider.** `AnalyzeImageDto.ImageUrl` has no validation and is passed as `image_url.url` to the Vision provider. The .NET server itself does not fetch it (so this is not a server-side SSRF), but it lets a caller make our provider fetch arbitrary URLs. Amplified by S-1 (no auth). | `DTOs/AnalyzeImageDto.cs`; `VisionService.cs` chat-completions payload |
| **S-8** | Low | **Over-broad exception handling can fabricate data.** `image_tools.py:36-45` catches bare `Exception` and returns heuristic "Curated Piece" attributes, silently converting provider outages into plausible-looking analysis results. | `agent-service/app/tools/inventory/image_tools.py:33-45` |

### 5.2 Positive security observations

- **No secrets are tracked.** `.env`, `.env.local` and `firebase-service-account.json` are all gitignored and absent from `git ls-files`; the CI `hygiene` job checks this and would pass.
- **Tenant scoping is consistently applied** on the catalog surface: every mutating handler overwrites the DTO's org id from the route (`CatalogEndpoints.cs:67,104,122,145,184,220,252,286`), which closes the obvious IDOR vector, and the service/repository layer threads `OrgId` through to the queries (`VisualService.cs:72,106,115`).
- **Internal endpoints are properly protected** by `InternalServicePolicy` (`VisualEndpoints.cs:23`).
- **Migrations are reversible** — the visual migration's `Down()` drops all 7 tables.

### 5.3 Dependency audit

| Ecosystem | Tool | Result |
|---|---|---|
| NuGet (.NET) | `dotnet list package --vulnerable --include-transitive` | ✅ "no vulnerable packages given the current sources" |
| npm/bun (web) | `bun audit` | ✅ "No vulnerabilities found" |
| Python | not run | ⚠️ no `pip-audit` in the environment and `agent-service` pins are unpinned/bare (`langgraph` unpinned) |

---

## 6. Code Integrity & Conventions

### 6.1 Structural integrity

| Area | Assessment |
|---|---|
| Modularity | ✅ Clean slice-2 module: `Modules/VisualIntelligence/{Models,DTOs,Repositories,Services}` + `VisualIntelligenceModule.cs`; programmatic registration in `Program.cs:96-99` |
| Separation of concerns | ✅ Endpoints → `IVisualService` → repositories; the vision client is isolated behind `IVisionService` |
| DRY | ⚠️ `VisualEndpoints` and `CatalogEndpoints` expose overlapping operations against the same services (internal vs tenant) — intentional, but the alias route groups (`/internal/visual`, `/api/internal/visual`, `/internal/inventory`) triple the mapping surface for 12 handlers |
| Error handling | ⚠️ Consistent within slice-2 but see §6.2; the Python vision tool swallows all exceptions (S-8) |
| Logging | ✅ No sensitive data logged in the slice-2 paths; the API's `GlobalExceptionHandler` deliberately keeps exception detail log-only |
| Migrations | ✅ Reversible (`Down()` drops 7 tables) and idempotent via `__EFMigrationsHistory`; the snapshot now matches the model (verified) |

### 6.2 Convention adherence

| Convention | Status | Evidence |
|---|---|---|
| Error body shape | ❌ **Violated.** `docs/api/README.md:201,211` state `404` is `{ "message": "..." }` and that "**Part C endpoints always use `{ "message": ... }`**". `CatalogEndpoints` (a Part C surface) returns `{ "error": "..." }` at 4 sites, contradicting the documented contract. | `CatalogEndpoints.cs:86,126,149,307` |
| **User-visible impact of the above** | ❌ The web client's `extractMessage` reads `message ?? detail ?? title` and never `error` (`frontend/web/src/lib/api-error.ts:33-46`), so catalog 404 text is **silently replaced** by the generic "The requested resource was not found." | `lib/api-error.ts:36` |
| Response envelopes | ❌ `GET /catalog/items` returns a bare array (`CatalogEndpoints.cs:52-57`) against `docs/api/README.md:260` | as cited |
| Naming (C#) | ✅ Module/namespace/type naming matches the rest of `Aveline.Api`; DTOs suffixed `Dto`; async methods suffixed `Async` |
| Naming (TS/React) | ⚠️ `components/catalog/mockData.ts` now holds shared **types** plus dead mock arrays — the name is misleading (see §6.3) |
| Commit messages | ✅ Conventional Commits used consistently across both PRs (`feat(catalog):`, `fix(web):`, `docs(ai-usage):`, …) |
| Linting (.NET) | ✅ Build clean; 26 warnings, all pre-existing (nullable/obsolete/testcontainers) |
| Linting (Python) | ⚠️ `ruff check .` reports **12 errors** (11 auto-fixable) — all in `tests/`: unsorted import blocks (`I001`) and two unused symbols (`F401`, `F841`). CI only runs `ruff check agent-service/app/`, so **CI passes**, but a plain `ruff check .` is red | `tests/tools/test_inventory_tools.py:1`, `tests/test_visual_routing.py:3:8`, `tests/test_golden_cases.py:61`, etc. |
| Linting (Web) | ⚠️ `oxlint`: **31 warnings, 0 errors** on 167 files, including React purity/set-state-in-effect warnings in `components/catalog/AddProductModal.tsx:46` and `:25-27` |
| Linting (Flutter) | ✅ `flutter analyze --no-fatal-infos`: no issues |
| Stale in-repo docs | ⚠️ `components/catalog/Catalog UI.md` documents endpoints that do not exist (`PATCH …/customer-matches/{matchId}` at :198, `PUT …/sourcing-requests/{id}` at :246); `Modules/VisualIntelligence/README.md` describes an abandoned MVC-controller layout; `docs/tests/README.md:70` lists only 5 of the 8 slice-2 .NET test files |
| OpenAPI | ❌ `docs/api/openapi.yaml` contains **none** of the catalog/visual/inventory paths |

### 6.3 Dead code, orphaned data and fabricated values

| Item | Evidence |
|---|---|
| **Dead mock data (web).** `mockData.ts` exports five large `MOCK_*` arrays that no component consumes — components import **types only**. Commit `03421a1` ("remove hardcoded sample data") removed the runtime usage but left the data and the misleading filename. | `frontend/web/src/components/catalog/mockData.ts:100,223,274,339,390`; grep for `MOCK_*` in `*.tsx` returns nothing |
| **Orphan entity.** `InventoryImage` has a table, model, configuration and migration, but no repository/service/endpoint; 0 % coverage. `AnalyzeImageAsync` persists nothing. | `Modules/VisualIntelligence/Models/InventoryImage.cs`; `VisualService.cs:83-89` |
| **Fabricated foreign key.** `ComposeOutfitAsync` sets `CustomerId = Guid.NewGuid()` on the persisted outfit, inventing a customer that is not in the request. | `Services/VisualService.cs:143` |
| **Never-set field.** `OutfitComposition.Name` defaults to `string.Empty` and is never assigned, so lookbooks render blank names. | `Models/OutfitComposition.cs:11`; `VisualService.cs:139-152` |
| **Hardcoded supplier catalog.** | `Services/VisualService.cs:345-373` (`Authentic Pure Zari Silk Saree from {supplier.SupplierName}`) |
| **Unwired caches.** `InventorySearchCache` / `ProductAnalysisCache` have tests and no production callers, so the documented invalidation lifecycle never runs. | `app/services/inventory_search_cache.py`, `product_analysis_cache.py`; no callers in `app/tools/`, `app/agents/visual_insight/` |
| **Dropped request fields.** `CreateSourcingRequestDto.QuantityNeeded`/`Urgency` are echoed in the response but never persisted; `ReferenceImageUrl` is hardcoded to `string.Empty`. | `VisualService.cs:184,203-205` |
| **Untracked generated artefacts not gitignored.** The three Flutter Windows plugin files are neither tracked nor ignored. | `frontend/aveline_mobile/windows/flutter/` |

---

## 7. Previous Report Follow-Up

Previous reports reviewed: `slice1-implementation-progress.md`, `slice2-elle-verification.md`, `slice2-convention-alignment.md`, and the five `admin-backend-api-*` reports. The slice-2-relevant outcomes are below; the admin/billing items are larger-slice work and are summarised separately.

### 7.1 `slice2-elle-verification.md` (the directly relevant predecessor)

| Item | Previous status | **Current status** | Evidence |
|---|---|---|---|
| **B1** LLM not wired in the running path | ❌ Blocking | ✅ **Fixed** | `concierge_workflow.py:179-180` passes `visual_llm_or_none(get_settings())` into `build_visual_graph` |
| **B2a** `SourcingRequest.Category/Color` silently dropped | ❌ Blocking, real bug | ✅ **Fixed**, and corrected further | `SourcingRequestConfiguration.cs:18-22`; migration `:70-71`. The follow-on snapshot drift this fix introduced is corrected in §2.4.1 |
| **B2b** Red .NET suite (Postgres FK test) | ❌ Blocking | ✅ **Fixed** | `VisualIntelligencePostgresTests.cs:134-148` now seeds the inventory item; 1231/0 green |
| **B3** Stale base → 3 merge conflicts | ❌ Blocking | ✅ **Resolved** | This integration (`0b207d1`); the three files merged cleanly here |
| **B4** Vision provider unconfigured/undocumented | ❌ Blocking | ✅ **Fixed** | `appsettings.json:76-79`; `.env.example:115-117`; `docker-compose.yml`; `ADR-020`; usage wired at `VisionService.cs:131-151` |
| Bug 1 — Elle replied in a Suggestion box to staff | ✅ Fixed | ✅ Still fixed | `nodes.py:269,316`; `block_builders.py:89-91` |
| Bug 2 — Salon card rendered in Elle's colour | ✅ Fixed | ✅ Still fixed | `blocks.tsx:252-299`; `MessageBubble.tsx:97,127-132` |
| Gap — supplier catalog fabricated | ⚠️ Open | ❌ **Still open** | `VisualService.cs:345-373` |
| Gap — alias route groups + `OrgId` aliases retained | ⚠️ Open | ❌ **Still open** | `VisualEndpoints.cs:21,109,128` |
| Gap — `VisualIntentGate` dead code | ⚠️ Open | ❌ **Still open** | only tests reference it |
| Gap — `docs/tests/README.md` slice-2 matrix | ⚠️ Open | ⚠️ **Partially fixed** | `:70` lists 5 of 8 .NET files; Python rows `:111-117` added |
| Gap — no frontend test for persona colour mapping | ⚠️ Open | ❌ **Still open** | `blocks.test.tsx` unchanged |
| Assertion mismatch (AI-usage log 42/42 vs actual) | ⚠️ | ⚠️ Still inaccurate | the log understates; verified suite is 45/0 for `~Visual` |

**Regressions: none found.** Every previously-fixed item is still fixed.

### 7.2 Broader integration (development / #244) — out of slice-2 scope but on this branch

A full cross-check of the five admin reports found **61 resolved**, **9 partially resolved**, **34 unresolved** and **12 unverifiable** items, with **no regressions**. The unresolved items that matter most on this branch are:

- **S-2/S-3/S-5 above** (weak token default, no rate limiting, webhook replay index).
- **T-0.1:** `POST /internal/agent-runs/{workflowId}/steps` with `"steps": null` can throw a null-reference → 500; the companion run route is guarded but `AppendStepsAsync` has no `?? []`. (`Modules/Statistics/Services/AgentRunIngestService.cs:97`.)
- **Agentic statistics have no producer:** `agent-runs` appears nowhere in `agent-service/app`, and `dataQuality` flags are hardcoded all-false — the shipped statistics endpoints serve empty series.
- **Billing-statistics family absent**, `recompute` returns 501, and the day-rollup tables/jobs do not exist.
- **Slice-1 gaps:** outbound WhatsApp send is never called, no LangGraph `interrupt`/`Command(resume)` SignOff resume, and `TotalSpent`/`VisitCount`/`LastVisitAt` are never written.

Provenance note: the admin reports describe revisions (`e5a8f34`, `8d4b45d`, `a542a5e`) that are **not ancestors** of this branch; the admin work arrives here as `62a12fe` (#244), so those statuses were re-derived from the current tree rather than replayed.

---

## 8. Recommendations & Next Steps

### P0 — block merge to `development`

1. **Remove `.AllowAnonymous()` from the catalog image-analysis route** (`CatalogEndpoints.cs:188`), or, if anonymous vision is genuinely required, add a dedicated rate limit + quota guard and document the exposure. Add a regression test asserting `401` for an unauthenticated call. *(Owner: PR #233 author / Slice-2.)*
2. **Fix the upstream `docker-compose.yml` duplicate-key defect on `development`** so other branches do not reintroduce it. *(Owner: development maintainers.)*
3. **Land the EF snapshot/migration correction** from §2.4.1 into PR #233 (or note that this integration already carries it), and announce that anyone who applied the old visual migration must recreate the slice-2 tables. *(Owner: PR #233 author.)*

### P1 — correctness / contract

4. Change the four catalog `404` bodies from `{ "error": … }` to the documented `{ "message": … }` (`CatalogEndpoints.cs:86,126,149,307`) so the React client stops discarding them.
5. Return the documented paginated envelope from `GET /catalog/items` and clamp `pageSize` (`SearchInventoryDto.cs`; `CatalogEndpoints.cs:52-57`).
6. Stop fabricating data: remove `CustomerId = Guid.NewGuid()` (`VisualService.cs:143`), set `OutfitComposition.Name`, and either back the supplier catalog with real data or mark it explicitly as a stub.
7. Forward `org_id` in `image_tools.py:36-37` and report the real provider/model in `nodes.py:377-382` to close defect **D-4**.
8. Guard `AppendStepsAsync` against a null `steps` payload (T-0.1).

### P2 — hygiene / coverage

9. Delete the five unused `MOCK_*` arrays and rename `mockData.ts` to `types.ts` (or move types into `types/`).
10. Bring the catalog UI under coverage — either add component tests or narrow the `coverage.exclude` claim in `vite.config.ts` so the reported number is honest; add tests for `InventoryTab`, `SourcingTab`, `AddProductModal`.
11. Remove or document the legacy alias route groups; remove `VisualIntentGate` if unused.
12. Fix the 12 `ruff` errors in `tests/` (all mechanical) and either broaden CI's ruff scope or document that `tests/` is intentionally unlinted.
13. Refresh stale docs: `components/catalog/Catalog UI.md`, `Modules/VisualIntelligence/README.md`, `docs/tests/README.md:70`, and add the slice-2 surface to `docs/api/openapi.yaml`.
14. Add a pytest assertion for the ADR-010 usage payload, which is currently untested.

---

## 9. Appendix

### 9.1 Commands run (abridged)

```bash
# Integration
git fetch --all
git checkout integration/slice-2-to-slice-1
git merge origin/development --no-edit          # 3 conflicts -> 0b207d1
git merge origin/catalog --no-edit              # 4 conflicts -> bf1446b
git merge origin/feature/visual-insight-agent   # Already up to date (contained)

# EF verification
dotnet ef migrations add __TempSnapshotFix --project Aveline.Api   # regenerate snapshot, then delete temp files
dotnet ef migrations has-pending-model-changes --project Aveline.Api
# -> No changes have been made to the model since the last migration.

# Tests
dotnet test Aveline.Api/Aveline.Api.sln -c Release                       # 1231 passed / 0 failed
dotnet test Aveline.Api/Aveline.Api.sln -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
env -u LLM_PROVIDER … .venv/bin/python -m pytest tests/ --cov=app --cov-fail-under=90   # 379 passed / 2 skipped
bun run test:coverage                                                    # 158 passed, 91.59% lines
flutter test --coverage                                                  # 81 passed
flutter analyze --no-fatal-infos                                         # No issues found

# Lint / audit
(cd frontend/web && bun run lint)                                        # 31 warnings, 0 errors
(cd agent-service && .venv/bin/python -m ruff check .)                   # 12 errors (tests/ only)
bun audit                                                                # No vulnerabilities found
NUGET_HTTP_CACHE_PATH="$PWD/.nuget-review-cache/http" \
  dotnet list Aveline.Api/Aveline.Api.csproj package --vulnerable --include-transitive
# -> no vulnerable packages
docker compose config --quiet                                            # fails before fix, passes after
```

### 9.2 Environment notes

- .NET 10.0.302 / EF Core tools 10.0.11; Python 3.14.7; Bun 1.3.14; Node 24.15.1; Flutter SDK on `/home/kavindu/Development/Flutter`.
- `flutter` and `dotnet list package --vulnerable` require write access outside the workspace (Flutter SDK cache; NuGet HTTP cache). The NuGet audit was completed by redirecting `NUGET_HTTP_CACHE_PATH` into the workspace; the Flutter commands were run with a one-off elevated sandbox grant.
- `TestResults/` is gitignored (`[Tt]est[Rr]esult*/`), so coverage artefacts do not pollute the tree.

### 9.3 Conflict resolution snippets

`OnboardingMiddleware.cs` (union of both intents):

```csharp
// Internal service calls (e.g. /internal/usage, /internal/visual) and API-key machine
// clients do not have Clerk user accounts and must not create onboarding stubs.
if (context.Request.Path.StartsWithSegments("/internal") ||
    context.Request.Path.StartsWithSegments("/api/internal") ||
    context.User.IsInRole("InternalService") ||
    context.User.HasClaim(claim =>
        claim.Type == Modules.ApiAccess.Authentication.ApiKeyClaimTypes.ApiKeyId))
```

`Program.cs` (union of endpoint registrations):

```csharp
v1.MapStatisticsEndpoints();
v1.MapCatalogEndpoints();
...
app.MapVisualEndpoints();
app.MapStatisticsInternalEndpoints();
app.MapControllers();
```

### 9.4 Open questions

1. **`integration/slice-1-to-slice-2` vs `integration/slice-2-to-slice-1`.** The brief names a branch that does not exist. Confirmed with the requester to use the existing reversed name; the CI/docs should standardise on one.
2. **Embeddings key requirement in compose.** `development` declares `Embeddings__ApiKey` twice, once with a `:?required` guard and once with a `:-` default. There is no runtime guard for embeddings in the API, so this integration keeps the permissive single definition (matching `.env.example`). If the intent is truly "required", an explicit startup guard should be added rather than relying on YAML.
3. **Should `analyze-image` remain anonymous?** If the anonymous path is a deliberate pre-auth demo, it needs an explicit product decision plus cost controls; otherwise it is a plain access-control defect.
4. **Entitlement gating for `agents.visual`.** Documented as a per-plan entitlement but not enforced anywhere. Is gating in scope for slice 2 or deferred?
5. **Are the slice-2 caches required?** The architecture doc claims automatic invalidation, but no production code path uses them. Do existing tests satisfy the requirement, or is wiring outstanding?
6. **Flutter slice-2 scope.** No slice-2 doc assigns mobile work, yet `lib/features/catalog` and `lib/features/inventory` exist as placeholders. Is mobile catalog in scope for slice 2 or deferred?

---

*Report generated from direct file inspection, git history, and executed test/audit tooling on `integration/slice-2-to-slice-1` @ `bf1446b`. All local commits; nothing was pushed.*
