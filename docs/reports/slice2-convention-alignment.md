# Slice 2 (Visual Intelligence & Sourcing) — Convention Alignment Report

> To: Student 2 (Dilud) · From: Student 1 (Kavindu)
> Reference implementation (source of truth): **AVA — Customer Memory Agent, Slice 1**

## Purpose

Slice 2 should follow the same architectural, AI, and testing conventions as AVA so the two
slices compose cleanly in the shared monolith + agent service. This report lists each convention
that AVA establishes, where the current Visual Intelligence code diverges, and the concrete
change to align it. Where you disagree with a convention, raise it via the ADR process rather than
silently diverging.

---

## 1. Single modular monolith — no separate solution

**Convention (AVA).** Everything lives in the one `Aveline.Api` project, organised as
`Aveline.Api/Modules/<Domain>/{Models, Repositories, Services, DTOs}`, with DI registered in a
`<Domain>Module.cs` and routes in `Endpoints/<Domain>Endpoints.cs` (minimal APIs).

**Current (Slice 2).** A second clean-architecture solution `backend/Aveline.sln`
(`Aveline.Domain` / `Aveline.Application` / `Aveline.Infrastructure`) plus an MVC
`VisualController`.

**Action.** Fold the Domain entities, Application services and Infrastructure repository into
`Aveline.Api/Modules/VisualIntelligence/{Models, Repositories, Services, DTOs}`. Delete
`backend/`. Register DI via a `VisualIntelligenceModule.cs` and expose minimal-API endpoints
(drop the MVC controller).

## 2. One shared DbContext and one migration stream

**Convention (AVA).** Slice 1 entities are `DbSet`s on the single
`Aveline.Api/Infrastructure/Data/AppDbContext` with EF `IEntityTypeConfiguration`s; migrations
are generated under `Aveline.Api/Migrations` (e.g. `AddCustomerConciergeEntities`) and applied by
the existing startup `MigrateAsync()`.

**Current (Slice 2).** A separate `AvelineDbContext` (only `InventoryItems`), registered with its
own migration assembly, **no migrations generated**, and startup only migrates `AppDbContext` —
so the `InventoryItems` table is never created against real Postgres.

**Action.** Add the Slice 2 entity `DbSet`s to `AppDbContext`, generate a migration in
`Aveline.Api/Migrations`, and remove `AvelineDbContext`.

## 3. Two runtimes, one contract — `ToolRegistry` + `InternalApiClient`

**Convention (AVA).** The Python agent never touches the DB; it calls internal endpoints via
`ToolRegistry` (thin wrappers over `InternalApiClient`), which attaches the `X-Internal-Token`
header (ADR-009) and targets `api_base_url`.

**Current (Slice 2).** A parallel `VisualTools` adapter (its own `httpx` client, an extra
`X-Internal-Key` header, hardcoded `localhost:5000` default) **and** `ToolRegistry` visual methods
that point at the wrong paths (`/api/internal/analyze-image`, `/api/internal/inventory/search`,
etc.).

**Action.** Delete `VisualTools`. Extend `ToolRegistry` with correctly-pathed visual methods that
reuse `InternalApiClient`. Revert the `X-Internal-Key` change to
`InternalTokenAuthenticationHandler` — the only service-to-service header is `X-Internal-Token`.

## 4. Internal endpoint path and auth convention

**Convention (AVA).** Internal (agent-only) routes live under `/internal/<domain>` — **no `/api`
prefix** — e.g. `/internal/customers`, guarded with
`.RequireAuthorization(InternalServicePolicy)`, and the org is carried explicitly
(`organizationId`) for tenant scoping.

**Current (Slice 2).** `/api/internal/visual/...` (extra `/api`), exposed via an MVC controller;
`VisualEndpoints.cs` (minimal API) exists but is never mapped (dead code).

**Action.** Use `/internal/visual/...` (or `/internal/inventory/...`) as minimal APIs, registered
once. Remove the unused `VisualEndpoints.cs` duplication.

## 5. Field naming

**Convention (AVA).** camelCase, and consistently `organizationId`, `customerId`, `fullName`,
`phoneNumber`.

**Current (Slice 2).** Mixed `orgId` in DTOs, routes and tools.

**Action.** Standardise on `organizationId`.

## 6. AI readiness — LLM, usage reporting, runtime validation

**Convention (AVA).** The LLM is gated by `AGENT_LLM_ENABLED` + `LLM_API_KEY`/`LLM_MODEL`
(`app/llm/runtime.py::memory_llm_or_none`), with a deterministic fallback; every completed run
reports usage to `/internal/usage/record` (ADR-010); and the composed output is validated against
the Pydantic schema in the running path (`coerce_output`, `extra="forbid"`).

**Current (Slice 2).** `VisualInsightAgent` accepts an `llm` but **never uses it**;
`build_visual_graph(registry)` is not passed an LLM; `VisualService` returns hardcoded data; there
is no usage reporting and no runtime schema validation.

**Action.** Thread the LLM through `build_visual_graph`, add a deterministic fallback, report
usage per run, and validate the output against `VisualAgentOutput` at runtime.

## 7. No invented/mock "intelligence" in the service layer

**Convention (AVA).** Deterministic rules + real backend facts (brief/preferences/events); the
agent never fabricates.

**Current (Slice 2).** `AnalyzeImageAsync`, `GetCustomerMatchesAsync`,
`GenerateCustomerMatchesAsync`, `GetSupplierCatalogAsync` and `CreateSourcingRequestAsync` return
hardcoded/mock results (fake customers, fake supplier catalog, fake image attributes; sourcing
requests are not persisted).

**Action.** Read real data from the repositories/DB. For image analysis, call a real vision
provider behind an abstraction (the same pattern as `IEmbeddingService`). Where a provider isn't
wired yet, mark it explicitly as a stub and do not surface fabricated data to the Salon.

## 8. Complete the entity set

**Convention (AVA).** Slice 1 delivered all 7 entities (Customers, Preferences, Events, Memory,
Interactions, Consent, Tags).

**Current (Slice 2).** Only `InventoryItem` exists. `Inventory_Images`, `Outfit_Compositions`,
`Sourcing_Requests`, `Suppliers`, `Customer_Matches` are DTOs only — no entities, no tables, no
migrations.

**Action.** Implement and migrate the remaining five entities.

## 9. Testing conventions

**Convention (AVA).** .NET in-memory + Testcontainers Postgres (pgvector); Python `pytest` against
a stubbed `ToolRegistry` (dependency-injected, no live backend/LLM). Documented in
`docs/tests/README.md`.

**Current (Slice 2).** `tests/tools/test_visual_tools.py` depends on `pytest-httpx`, which is not
installed in the agent-service environment (12 fixture errors). No Testcontainers coverage.

**Action.** Adopt the AVA stub-`ToolRegistry` test pattern. Either add `pytest-httpx` to the
agent-service dev dependencies or drop it in favour of the stub pattern. Add Testcontainers tests
for inventory persistence/search.

## 10. Persona key and Salon rendering

**Convention (AVA).** Canonical persona keys are `aveline` / `ava` / `elle` / `lina`
(`AgentKeys.cs`). The publisher maps the concierge `visual` field to the **`elle`** persona via
`build_elle_blocks`, which renders `suggestion`/`items`/`looks` blocks when `status != "stub"`.

**Action.** Keep the visual output `status` in the allowed set so `build_elle_blocks` renders it,
and consider extending `build_elle_blocks` to render `sourcing_request` / `image_attributes`
(currently dropped).

## 11. Documentation and ADRs

**Convention (AVA).** `docs/architecture/customer-memory.md`, ADR-017 (pgvector decision), ADR
index + `docs/tests/README.md` updates, and an `docs/ai-usage/` log.

**Action.** Add `docs/architecture/visual-intelligence.md` and an ADR for any new decision (e.g.
the vision provider choice), update the ADR index and test matrix, and keep the AI-usage log.

---

## Priority action checklist

1. Delete `backend/`; move into `Aveline.Api/Modules/VisualIntelligence`.
2. Move Slice 2 entities onto `AppDbContext` + generate a migration.
3. Remove `VisualTools` and the `X-Internal-Key` handler change; fix `ToolRegistry` visual paths to
   `/internal/visual/...`; use minimal APIs (delete the MVC controller / dead `VisualEndpoints`).
4. Wire the LLM + usage reporting + runtime schema validation.
5. Replace hardcoded mocks with real repository reads / a real vision call.
6. Implement and migrate the remaining five entities.
7. Fix the test approach (stub `ToolRegistry` or add `pytest-httpx`) and add Testcontainers tests.
8. Add the architecture doc + ADR + test-matrix/log updates.
