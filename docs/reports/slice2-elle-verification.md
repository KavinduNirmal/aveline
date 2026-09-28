# Slice 2 (Elle) — Verification Report

> Verified: teammate branch `origin/feature/visual-insight-agent` @ `4b6b2e1`
> ("feat(visual): align Slice 2 with Slice 1 conventions, wire AI intelligence, real
> repositories & fix bugs 1 & 2"), against `origin/development` @ `a2adc22`.
> Method: fresh detached worktrees, real builds, real test runs (Testcontainers + Postgres),
> and a trial merge into current `development`.

## Verdict

**Partially fulfilled.** The structural/architectural alignment is genuinely done and the two
reported bugs are fixed. But the slice is **not fully delivered**: the "AI wiring" is dead in the
running path, the .NET suite is **red (2 failures)** — one of which exposes a real
data-loss bug — and the branch is built on a **stale `development`** that no longer matches the
Slice 1 source of truth, so it does not merge cleanly.

| Area | Result |
|---|---|
| Convention alignment (11 items) | ~8 fully done, 3 partial |
| Bug 1 — Elle replies in a Suggestion box to staff | **Fixed** |
| Bug 2 — Salon card rendered in Elle's colour | **Fixed** |
| Python suite | 298 passed / 2 skipped / 1 env-only failure (was 10–12 errors) |
| .NET suite | **503 passed / 2 failed** (505) |
| Frontend | `tsc -b` clean, vitest 8/8, `vite build` OK |
| Merge into current `development` | **3 conflicts** |

---

## 1. What was verified as fixed

- `backend/` deleted; code folded into `Aveline.Api/Modules/VisualIntelligence/{Models,Repositories,Services,DTOs}` with `VisualIntelligenceModule.cs` and minimal APIs.
- One `AppDbContext`: 7 entities, 7 EF configurations, and migration `20260910064834_AddVisualIntelligenceEntities` under `Aveline.Api/Migrations`.
- `VisualTools` deleted; `ToolRegistry` visual paths corrected to `/internal/visual/...`; `InternalApiClient` reused.
- `X-Internal-Key` fallback removed from `InternalTokenAuthenticationHandler` (now `X-Internal-Token` only).
- All 7 entities exist, one class per file (`OutfitItem.cs`, `Supplier.cs` split out).
- Sourcing / outfit / match now persisted through repositories.
- Runtime schema validation added: `coerce_visual_output` → `VisualAgentOutput` (`extra="forbid"`).
- Tests: `pytest-httpx` declared **and** `test_visual_tools.py` migrated to `respx` (no `httpx_mock` remains → the 12 errors are gone). Testcontainers Postgres suite added.
- `docs/architecture/visual-intelligence.md` + `docs/ai-usage/Dilud.md` added; duplicate `slice2-convention-alignment (1).md` removed.

### Bug 1 — staff Suggestion box (fixed)
`run_visual_agent` now derives `direction`/`staff_query` (`concierge_workflow.py:128-143`); the
`compose_looks` / `check_sourcing` nodes return `text`/`summary` and `suggestion=None` for staff
(`nodes.py:246,269,305,316`); `build_elle_blocks` emits a `text` block
(`block_builders.py:89-91`). Two new tests cover it
(`test_visual_insight_graph_staff_query_emits_text_summary_no_suggestion`,
`test_elle_blocks_staff_query_emits_text_block_no_suggestion`). Traced to the API: staff notes go
through `TriggerAgentAsync`, whose `org_context` has **no `direction`** → `staff_query=True`
(`ConversationService.cs:410`). Correct.

### Bug 2 — Salon card colour (fixed)
`SuggestionBlock` and `LookBlock` now colour from the authoring persona
(`blocks.tsx:192-224`), and `MessageBubble` passes `persona` to `BlockList`
(`MessageBubble.tsx:105`). Ava's drafts are now `memory`-coloured, Elle's `visual`, Lina's
`commerce`, default `primary`. Theme tokens exist. No new frontend test was added for the colour
mapping (`blocks.test.tsx` unchanged).

---

## 2. Blocking issues (must fix)

### B1. The LLM is still not wired in the running path (`concierge_workflow.py:132`)
`build_visual_graph(registry, llm=None)` is now *capable* of taking an LLM and `compose_looks`
uses it — but the only production call site still passes no LLM:

```python
# agent-service/app/workflows/concierge_workflow.py:132
graph = build_visual_graph(registry)      # llm is never passed
```

Consequences: the AI look commentary never runs; `prompt_tokens`/`completion_tokens` stay 0; the
ADR-010 `report_usage` call (`nodes.py:372-388`) is **unreachable**; no `AGENT_LLM_ENABLED`
gating. The new test `test_visual_insight_graph_uses_llm_for_look_commentary` passes only because
it calls `build_visual_graph(registry, llm=mock_llm)` directly — it does not prove production
wiring. This branch also has no `app/llm/runtime.py` (`memory_llm_or_none`), because it predates
development's LLM wiring; the call site should use the same helper once merged.

### B2. .NET suite is red — 2 new Postgres tests fail
Run: `dotnet test Aveline.Api.Tests` → **Failed: 2, Passed: 503, Total: 505**
(`--filter "FullyQualifiedName~Visual"` → 49 passed / 2 failed, not the "42/42" claimed in the
AI-usage log).

1. `SourcingRequestAndSupplier_CanPersistAndQuery_InRealPostgres` — `Category` comes back `""`.
   **This is a real product bug, not just a test bug.** `CreateSourcingRequestAsync` sets
   `Category`/`Color` (`VisualService.cs:185-186`) and echoes them in the response DTO, but
   `SourcingRequestConfiguration.cs:15-16` marks `Category` and `Color` as `builder.Ignore(...)`.
   The values are silently dropped on persist; the API response looks correct, any subsequent
   read returns empty. (`Description`/`TargetPrice` survive only because their alias setters write
   to the mapped `ItemDescription`/`ProposedPrice`.)
2. `CustomerMatchRepository_CanPersistAndRetrieveMatches_InRealPostgres` — FK violation
   `FK_customer_matches_inventory_items_ItemId`: the test inserts a match with a random `ItemId`
   without seeding an inventory item (`VisualIntelligencePostgresTests.cs:134-146`). Test bug
   (the FK from the migration is legitimate), but the suite is red either way.

### B3. Branch is based on stale `development` → does not merge cleanly
First-parent chain: `4b6b2e1 → cc73a67 → 0de511a → 2850b7c → 2d55785`, merge-base with
`development` = `2d55785`. The branch therefore lacks development's `#160` (Gemini embeddings),
`#161/#162` (shared customer resolution + `choice` block) and the LLM runtime. A trial merge
`development ← feature/visual-insight-agent` yields **3 conflicts**:
`agent-service/tests/test_concierge_workflow.py`,
`frontend/web/src/components/conversation/MessageBubble.tsx`,
`frontend/web/src/components/conversation/blocks.tsx` (the visual `persona` prop vs development's
`onSelectCustomer`/`ChoiceBlock`). Post-merge, `run_memory_agent` gets an LLM but
`run_visual_agent` still does not.

### B4. Vision provider is unconfigured and undocumented
`VisionService` is a genuine OpenAI-compatible multimodal client with a deterministic fallback
(good pattern), but no `Vision:ApiKey` / `Vision:BaseUrl` / `Vision:Model` appears in
`appsettings*.json` or `.env.example`. With no key, **every** image analysis takes the
URL-filename heuristic fallback. The vision call also records no ADR-010 usage, so its spend is
untracked.

---

## 3. Remaining gaps (not blocking, but open)

- **Supplier catalog still fabricated.** `GetSupplierCatalogAsync` now reads the supplier record,
  but the two catalog items are hardcoded with the supplier's name interpolated
  (`VisualService.cs:~219-260`). Convention #7 only partially satisfied.
- **Customer matching is real but rule-based** (Customers + Preferences affinity), not
  embedding/LLM-based. Acceptable functionally; note it in the ADR/architecture doc.
- **Runtime-outfit styling text** is still deterministic rule text.
- **Alias surface retained:** `VisualEndpoints` still registers three route groups
  (`/internal/visual`, `/api/internal/visual`, `/internal/inventory`, `VisualEndpoints.cs:109,135`),
  and every DTO keeps an `OrgId` alias next to `OrganizationId`.
- **Dead code:** `VisualIntentGate` is exported but unused; `VisualInsightAgent.run()`/`_handle_*`
  are bypassed by the graph nodes.
- **Docs:** no ADR for the vision-provider decision; `docs/tests/README.md` test matrix was not
  updated with the Slice 2 rows.
- **Assertion mismatch:** the AI-usage log claims `~Visual` 42/42 and pytest 299 passed; the
  verifiable numbers are 49/2 failing for `~Visual` and 298/2 skipped (+1 env-only failure).

---

## 4. Deliverables against the README matrix

| Deliverable | State |
|---|---|
| Entities `Inventory_Items`, `Inventory_Images`, `Outfit_Compositions`, `Sourcing_Requests`, `Suppliers`, `Customer_Matches` | **Done** — 7 entities + migration |
| Product image attribute extraction | **Partial** — real vision client exists, but unconfigured → heuristic fallback in practice |
| Customer-to-item matching | **Done (rule-based)** — real customers/preferences, no AI |
| Outfit composition | **Done** — real inventory search + persisted compositions |
| Sourcing requests | **Buggy** — persisted, but `Category`/`Color` silently lost |
| Supplier catalogs | **Partial** — supplier record real, catalog items hardcoded |

---

## 5. Recommended next steps (ordered)

1. Fix `SourcingRequestConfiguration`: map `Category`/`Color` columns (and regenerate the
   migration) instead of `Ignore`; seed an inventory item in the `CustomerMatch` Postgres test.
2. Merge/rebase `origin/development` into the visual branch, resolve the 3 conflicts, then wire
   the LLM at `concierge_workflow.py:132` via `memory_llm_or_none(get_settings())` (or the local
   `create_chat_model` + `AGENT_LLM_ENABLED` gate) and report usage with the real
   provider/model from settings.
3. Document/configure `Vision:*` (appsettings + `.env.example`) and record vision usage; add the
   vision-provider ADR.
4. Replace the hardcoded supplier catalog with repository-backed supplier catalog data (or mark
   it explicitly as a documented stub).
5. Housekeeping: drop the alias route groups and `OrgId` aliases, remove `VisualIntentGate` dead
   code, add the Slice 2 rows to `docs/tests/README.md`.
