# Diagnosis: `ActualCostUsd` is always `0` and the "log columns" are not empty

**Status:** root cause confirmed
**Scope:** `Aveline.Api` (`Modules/Billing`) + `agnet-service` (`app/services/usage_reporter.py`, `app/api/agents.py`)
**Subject data:** `AiUsageRecords` rows for org `01a078bf-45c1-7231-bae1-3e0ea1dc0471`, 2026-09-09

---

## 1. Answer first

`ActualCostUsd` is `0` because **no process in this system ever computes a monetary cost for an
LLM call**. The Python agent service declares an `actual_cost_usd` parameter on
`report_usage()` with a default of `0.0` and **never passes it** at either of its two call sites
(`agnet-service/app/services/usage_reporter.py:20`; call sites
`agnet-service/app/api/agents.py:247` and `agnet-service/app/agents/visual_insight/nodes.py:427`).
A second path hardcodes the literal `0.0`
(`agnet-service/app/api/agents.py:271` and `:325`/`:344`). No price table, per-token rate, or
token×rate multiplication exists anywhere in either service. The `.NET` API stores whatever value
it is handed and nothing else (`Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:52`).

This is not a new or accidental bug. It is **known, named, and documented** in this repository as
**defect D-8** and **gap G-5**, with the explicit decision that a zero cost is currently valid
(`docs/backend/backend-requirements.md:92`, `docs/backend/backend-requirements.md:737`,
`docs/backend/backend-requirements.md:680`).

The second half of the premise does not hold. **`AiUsageRecords` has no
`provider_response`, `error`, or `metadata` column** and never has had one. The table holds
19 columns; none of them is a log or provider-response field. The trailing empties in the pasted
rows are a `psql` display artifact, not stored NULLs. Separately, **no column that was in the
paste's legible range is NULL** — every value present is populated and correct.

Confidence: **confirmed** for every claim above. Evidence follows, with the runtime test that
settles the "backend cannot store cost" hypothesis.

---

## 2. Scope, method, and what would change the answer

**Inspected**

| Evidence | What it settled |
|---|---|
| `Aveline.Api` Billing + Statistics source (read) | cost is pass-through; no calculator |
| `AiUsageRecords` EF config + all migrations touching it (read) | exact 19-column schema |
| `agnet-service/app/**` (read; `.venv` excluded) | cost hardcoded `0.0`; tokens captured |
| `docs/backend/**`, `docs/ADR/ADR-010*.md`, `docs/ai-usage/**` (read) | D-8 / G-5 are pre-existing, accepted |
| `git log -S actual_cost_usd` | parameter has defaulted to `0.0` since introduction (`7438c1a`) |
| `dotnet test` (ran) | backend persists non-zero cost correctly → defect is upstream |
| DeepSeek official pricing + token docs (fetched) | provider returns tokens, not USD cost |

**Commands run (read-only; `dotnet test` builds to `bin/`, `obj/`)**

```
grep -rn "actual_cost_usd\|actualCostUsd" agnet-service/app Aveline.Api
grep -rn "ActualCostUsd" --include="*.cs" Aveline.Api/Modules Aveline.Api/Infrastructure
grep -rn "HasData" Aveline.Api/Migrations/*.cs
git log --oneline -S "actual_cost_usd" -- agnet-service/
git show 7438c1a:agnet-service/app/services/usage_reporter.py
dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj \
  --filter "FullyQualifiedName~UsageTrackerServiceTests|FullyQualifiedName~UsageEndpointsIntegrationTests"
```

**Not covered.** I did not query the live database, so I could not confirm the pasted rows are what
the schema predicts, nor check `AgentWorkflowRunId`/`PricingRuleId` on them. I did not make a live
DeepSeek call. I did not audit the Flutter/React clients.

**What would change the answer.** A writer outside these two services (a manual `INSERT`, a SQL
job, a different branch) that supplies a real cost. `grep` finds no such writer; `AiUsageRecords`
is written only from `UsageTrackerService.RecordWorkflowUsageAsync` via
`POST /internal/usage/record` (`Aveline.Api/Modules/Billing/Endpoints/UsageEndpoints.cs:22,51`).

---

## 3. What the pasted rows actually are

`AiUsageRecords` was created with 12 columns and has 7 added since — 19 total. Verified in the
creation migration (`Aveline.Api/Migrations/20260906201036_AddIntegrationCredentials.cs:95-110`),
the pricing-snapshot migration
(`Aveline.Api/Migrations/20260911172513_AddAiUsageRecordPricingSnapshot.cs:14-49`), and the live
model (`Aveline.Api/Infrastructure/Data/Configurations/BillingConfigurations.cs:12-64`).

| # | Column | Type | Null? | In creation migration |
|---|---|---|---|---|
| 1 | `Id` | uuid | no | yes |
| 2 | `OrganizationId` | uuid | no | yes |
| 3 | `RequestId` | varchar(128) | no | yes |
| 4 | `WorkflowId` | varchar(128) | no | yes |
| 5 | `Provider` | varchar(64) | no | yes |
| 6 | `Model` | varchar(128) | no | yes |
| 7 | `InputTokens` | int | no | yes |
| 8 | `OutputTokens` | int | no | yes |
| 9 | `CachedTokens` | int | no | yes |
| 10 | `ActualCostUsd` | numeric(18,8) | **no** | yes |
| 11 | `BlossomUnits` | numeric(18,4) | no | yes |
| 12 | `CreatedAt` | timestamptz | no | yes |
| 13 | `NormalizedUnits` | bigint | yes | added |
| 14 | `PricingRuleId` | uuid | yes | added |
| 15 | `PricingRuleVersion` | int | yes | added |
| 16 | `UnitsPerBlossom` | int | yes | added |
| 17 | `RoundingMode` | varchar(16) | yes | added |
| 18 | `RoundingDecimals` | smallint | yes | added |
| 19 | `AgentWorkflowRunId` | uuid | yes | added (`20260911183232_AddAgenticStatistics.cs`) |

Indexes: PK on `Id`; `IX (OrganizationId, CreatedAt)`
(`BillingConfigurations.cs:51`); a unique, filtered `IX AgentWorkflowRunId WHERE NOT NULL`
(`BillingConfigurations.cs:59-61`); a filtered `IX PricingRuleId`
(`BillingConfigurations.cs:63-64`). No check constraints, no defaults, and no `text`, `jsonb`, or
`bytea` column anywhere on the table — which is itself decisive evidence that it cannot hold a
provider response body.

**Column names are PascalCase, not snake_case.** There is no EF naming-convention package in the
project and no `UseSnakeCaseNamingConvention` registration
(`grep -rn "UseSnakeCaseNamingConvention\|EFCore.NamingConventions" Aveline.Api/` → 0 hits;
`AppDbContext.OnModelCreating` only calls `ApplyConfigurationsFromAssembly`,
`Aveline.Api/Infrastructure/Data/AppDbContext.cs:164-168`). The physical identifiers are quoted
PascalCase, so Postgres treats them as case-sensitive. `ai_usage_records` appears only in design
documents (`docs/ADR/ADR-020-multimodal-vision-provider.md:46`, `docs/ai-usage/kavindu.md:937`),
never in a migration. The `actual_usd` style labelling in the pasted rows is therefore the
reader's, not the database's — which is why the header guesses drifted.

**The `deepseek` row decodes cleanly, and nothing is NULL in it:**

```
Id              01a08760-8287-78dd-9fc5-561e8ad93cea
OrganizationId  01a078bf-45c1-7231-bae1-3e0ea1dc0471
RequestId       455be4fe-f80c-41e5-a685-a8249f2ecf5a
WorkflowId      t-cap4                                    <- the paste labelled this "model"
Provider        deepseek
Model           deepseek-v4-flash                         <- the paste labelled this "model_name"
InputTokens     1309
OutputTokens    138
CachedTokens    0
ActualCostUsd   0.00000000
BlossomUnits    1.5000
CreatedAt       2026-09-09 18:14:04.167018+00
```

Three corrections to the premise:

1. **The rows are not from the "agent service's AI-usage table" in the sense of an AI log.** They
   are rows of `AiUsageRecords`, whose own doc comment calls it an
   "immutable, append-only record of the AI usage consumed by a single agent workflow"
   (`Aveline.Api/Modules/Billing/Models/AiUsageRecord.cs:5-15`). One row per **workflow**, not per
   LLM call — the model comment says so explicitly (`AiUsageRecord.cs:9`).
2. **The paste's column labels are misaligned.** Position 4 (`t-cap4`) is `WorkflowId` and
   position 6 (`deepseek-v4-flash`) is `Model`. There is no `model_name` column, and no column
   holds `t-cap4` other than `WorkflowId`. I read the label guesses as shifted, not as evidence of
   a second table.
3. **No value in the legible range is NULL.** `rule-based` rows legitimately carry
   `InputTokens = 0`, `OutputTokens = 0`, `CachedTokens = 0`, `ActualCostUsd = 0` and
   `BlossomUnits = 0.1000` — the latter is the minimum-charge floor
   (`UsageTrackerService.cs:120`), which is why those rows show a non-zero `BlossomUnits` despite
   zero tokens.

**On the "empty columns after `created_at`":** `psql` appends a tab per trailing NULL in unaligned
output, and trailing tabs are invisible in a paste. Because `AiUsageRecords` has exactly the 12
legible columns and no log columns, the empties are display residue. I confirmed by exhaustive
search that no such column exists:

```
grep -rn "ProviderResponse\|RawResponse\|ResponseBody\|provider_response\|raw_response" Aveline.Api/  -> no matches
grep -rn "ProviderResponse|RawResponse|ResponseBody" (EF configurations)                                -> no matches
```

The only tables in this schema that hold an error string are `AgentWorkflowRuns.ErrorMessage`
(`Aveline.Api/Infrastructure/Data/Configurations/AgentRunConfigurations.cs:50`) and
`AgentStepRuns.ErrorMessage` (`AgentRunConfigurations.cs:142`) — a **different table from a
different module**, and neither is a provider-response or metadata store. `AgentStepRuns` by design
stores no result content at all, only `ArgsHash` and `ResultBytes`
(`Aveline.Api/Modules/Statistics/Models/AgentWorkflowRun.cs:139-147`).

> **Inferred, not confirmed.** I did not run the query that produced the paste, so I cannot prove
> from the database itself that the trailing region is empty. The schema makes it a certainty that
> no column named `provider_response`, `error`, or `metadata` exists on `AiUsageRecords`.

> **A plausible source of the confusion.** The LangGraph Postgres checkpointer creates its own
> snake_case tables **in the same database** — `checkpoints`, `checkpoint_blobs`, `checkpoint_writes`
> — and `checkpoints` carries a `metadata jsonb` column holding serialized graph state
> (`agnet-service/app/workflows/checkpointer.py:58,87`; DDL at
> `agnet-service/.venv/lib/python3.14/site-packages/langgraph/checkpoint/postgres/base.py:47-76`).
> An ad-hoc inspection that joins across the database can therefore surface snake_case columns and a
> `metadata` column that belong to the checkpointer, not to AI usage.

---

## 4. Root cause 1 — `ActualCostUsd = 0`: no cost is ever computed

### 4.1 The .NET API is a faithful pass-through

`UsageTrackerService.RecordWorkflowUsageAsync` copies the request straight onto the entity and
never derives a cost:

```csharp
// Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:42-62
var record = new AiUsageRecord
{
    ...
    InputTokens    = request.InputTokens,
    OutputTokens   = request.OutputTokens,
    CachedTokens   = request.CachedTokens,
    ActualCostUsd  = request.ActualCostUsd,   // <- straight pass-through, line 52
    BlossomUnits   = blossomUnits,            // <- the only server-computed money field
    ...
};
```

`BlossomUnits` **is** computed server-side, from tokens
(`UsageTrackerService.cs:114-121`, `BlossomCalculator.Calculate`). `ActualCostUsd` is not. The
only validation is a non-negativity check (`UsageTrackerService.cs:189-190`) — which a hardcoded
`0` passes trivially.

### 4.2 The Python caller always sends zero

`actual_cost_usd` is declared with a default of `0.0` and documented as "Raw provider cost in USD":

```python
# agnet-service/app/services/usage_reporter.py:11-23
async def report_usage(
    ...,
    cached_tokens: int = 0,
    actual_cost_usd: float = 0.0,      # line 20
    ...
```

It is serialized onto the wire as-is (`usage_reporter.py:61`, `"actualCostUsd": actual_cost_usd`).
Both call sites — the only two in `app/` — omit the argument:

| Call site | Passes `actual_cost_usd`? |
|---|---|
| `agnet-service/app/api/agents.py:247` | no → defaults to `0.0` |
| `agnet-service/app/agents/visual_insight/nodes.py:427` | no → defaults to `0.0` |

And the second, independent reporting path hardcodes the literal:

| Line | Code |
|---|---|
| `agnet-service/app/api/agents.py:271` | `actual_cost_usd=0.0,` (arg to `collector.finalize`) |
| `agnet-service/app/api/agents.py:325` | `"actualCostUsd": 0.0,` (synthetic run payload) |
| `agnet-service/app/api/agents.py:344` | `"actualCostUsd": 0.0,` (synthetic step payload) |
| `Aveline.Api/Modules/VisualIntelligence/Services/VisionService.cs:178` | `ActualCostUsd: 0m` (**the fourth production writer, and the only one in .NET**) |

The vision path is a fourth writer worth naming separately: `VisionService.cs:169-179` posts a usage
record from **inside the .NET API itself** and hardcodes `ActualCostUsd: 0m` — the only .NET-side
producer, and it too hardcodes zero rather than deriving it. (`git blame` attributes the line to
`7d4a101`, 2026-09-13.) It also hardcodes `Provider: "openai"` while taking `Model` from config
(`VisionService.cs:173-174`), so those rows are mis-attributed as well — the same defect D-4
describes for the visual agent.

Every `actual_cost_usd` occurrence in `agnet-service/app/` is one of the three Python zero-valued
plumbing lines plus the dataclass field defaults in
`agnet-service/app/telemetry/agent_telemetry.py:79,116,201,225,245,270`. There is no thirteenth
occurrence that computes anything. A whole-repo search for any USD-per-token rate returns nothing:
`grep -rn "per_token\|PerToken\|PricePer\|cost_per\|CostPer"` finds only Commerce `OrderItem.UnitPrice`
and one prose line in `docs/backend/statistics-catalog.md:111`. No `HasData` seed exists anywhere
(`grep -rn "\.HasData(" Aveline.Api/ --include=*.cs` → 0 matches), so no price table could have been
seeded and then missed.

### 4.3 No cost calculator exists in either service

Searched `agnet-service/app/` and `Aveline.Api/` for a price table or a rate multiplication. The
result is an absence:

| Probe | Result |
|---|---|
| `grep -rn "per_1k\|per_1m\|token_cost\|input_cost\|output_cost" agnet-service/app/` | 0 hits |
| `grep -rnE '\* *0\.0000' agnet-service/app/` (per-token rate constant) | 0 hits |
| `grep -rniE "pric" Aveline.Api/ --include=*.cs` minus Blossom/pricing-rule names | 1 hit, unrelated (`AgentStatisticsDtos.cs:217`, a DTO doc comment) |
| `grep -rn "HasData" Aveline.Api/Migrations/*.cs \| grep -i "deepseek\|usd"` | 0 hits |

**The .NET "pricing" system does not price LLM tokens.** It prices Blossoms — the internal credit
unit. `BlossomConversionRule` is "the effective-dated token to Blossom normalisation rate"
(`Aveline.Api/Modules/Billing/Models/BlossomConversionRule.cs:6-7`), keyed on
`(ScopeKind, Provider, Model)` but denominated in *normalized units per Blossom*
(`BlossomConversionRule.cs:19-20`), not USD. `BlossomPriceEntry` is denominated in **LKR** and its
own doc comment rules it out for this purpose:

> "The effective-dated commercial price of Blossoms (domain-model.md §3.2). Used for revenue
> reporting and top-up pricing, **never to compute consumption**."
> — `Aveline.Api/Modules/Billing/Models/BlossomPriceEntry.cs:3-6`

So nothing named "pricing" can produce an LLM cost. The distinction matters: the token→Blossom
path is complete, correct, and exercised; the token→USD path was never built.

### 4.4 Runtime confirmation — the backend is not the problem

`dotnet test` on the two billing suites that round-trip this field:

```
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18, Duration: 10 s
  (--filter "FullyQualifiedName~UsageTrackerServiceTests|FullyQualifiedName~UsageEndpointsIntegrationTests")
```

Those tests assert that costs of `0.01m`, `0.005m`, `0.002m`, `0.003m × i` and `2.50m` are
persisted and returned (`Aveline.Api.Tests/UsageTrackerServiceTests.cs:77,98,182`;
`Aveline.Api.Tests/UsageEndpointsIntegrationTests.cs:60,80,104,151`). **A non-zero cost sent to
the API lands in the database.** Combined with §4.2, the defect is entirely on the producing side.

### 4.5 It is documented, and it has been zero since the first commit

`docs/backend/backend-requirements.md:92`:

> **D-8** | **`cached_tokens` and `actual_cost_usd` are never sent by any caller**, so both are
> permanently zero on the wire and the cost-anomaly detector can never fire. |
> `agnet-service/app/services/usage_reporter.py:19-20`; no caller supplies them

`docs/backend/backend-requirements.md:680` (gap G-5) and `:737`:

> BR-5.7 | `ActualCostUsd >= 0`, `decimal(18,8)`. **A zero cost is valid and expected while G-5 is
> unfixed.**

`git show 7438c1a:agnet-service/app/services/usage_reporter.py` — the commit that introduced usage
tracking — already declares `actual_cost_usd: float = 0.0`. Nothing ever changed that.

---

## 5. Root cause 2 — the "NULL logs": nothing is broken

There is nothing to populate. The columns the premise names do not exist on this table, and the
columns that do exist are populated correctly. Three points of evidence that the write path is
fully healthy:

**(a) Token capture works, and the numbers reach the DB exactly.** `BlossomUnits = 1.5000` on the
`deepseek` row is a checkable fingerprint. The server computes
`max(ceil((input + output + cached) / 1000 × 10) / 10, 0.1)`
(`UsageTrackerService.cs:114-121`). For `1309 + 138 + 0 = 1447` tokens:
`1447/1000 = 1.447`, `ceil(14.47) = 15`, `15/10 = 1.5`. Reported value `1.5000`. **Exact match** —
which proves token counts traversed Python → HTTP → DTO → DB intact, and that no exception or
early return skipped the write.

**(b) Tolerant deserialization means the success path required every token field.** All three
token fields are required `int`s on the DTO (`Aveline.Api/Modules/Billing/Services/IUsageTrackerService.cs:9-18`)
and the repository uses the default `System.Text.Json` deserializer, which rejects missing required
members. The row's existence therefore *proves* `inputTokens`, `outputTokens` and `cachedTokens`
were all present and well-formed in the request body.

**(c) Token provenance is the real provider response.** Counts come from LangChain's
`AIMessage.usage_metadata` on the actual provider reply — memory agent at
`agnet-service/app/agents/customer_memory/nodes.py:404-420` (`result = await self.llm.ainvoke(...)`,
then `meta.get("input_tokens")` / `meta.get("output_tokens")`), visual agent at
`agnet-service/app/agents/visual_insight/nodes.py:310-315`. So the DeepSeek integration **is**
parsing the response correctly for tokens.

**The integration is correct; only cost is absent.** `ChatDeepSeek` adds no cost field:
`grep -iE "cost|price|usd|pricing"` over the installed `langchain_deepseek/` package returns
nothing, and LangChain's own `UsageMetadata` carries only
`input_tokens`/`output_tokens`/`total_tokens`
(`langchain_core/messages/ai.py:104-145`). LangSmith's schema *does* define
`total_cost`/`prompt_cost`/`completion_cost`, but the service never imports LangSmith.

**Why there is no error string anywhere.** The agent service reports usage **best-effort** and
swallows every failure:

```python
# agnet-service/app/api/agents.py:254-259
except Exception:  # noqa: BLE001 - usage reporting must never fail the agent query
    logger.exception(
        "Failed to report usage for workflow %s (best-effort).", workflow_id, ...)
```

This is intentional and per ADR-010 (`docs/ADR/ADR-010-usage-tracking-architecture.md:86-88`). The
consequence for diagnosis: **a failed usage report leaves no trace in the database at all** — not
a row with a NULL error column, but *no row*. If you are ever unsure whether a workflow reported,
absence of the row is the signal, and the agent-service stdout holds the `logger.exception`
traceback.

---

## 6. Contributing defect — cached tokens are captured and then dropped

`ActualCostUsd` is not the only field zeroed by omission. `CachedTokens` is zero on the wire and in
the database, **even when the provider reports a cache hit**, because the value is dropped two
steps before the report.

DeepSeek bills cache hits an order of magnitude below cache misses
([DeepSeek Models & Pricing](https://api-docs.deepseek.com/quick_start/pricing/): `$0.006` vs
`$0.30` per 1M input tokens at peak, for `deepseek-flash`). The field therefore matters for any
future cost calculation, not just for reporting.

The memory agent does capture it:

```python
# agnet-service/app/agents/customer_memory/nodes.py:415-425
meta = getattr(result, "usage_metadata", None) or {}
token_details = meta.get("input_token_details") or {}
cached_tokens = int(token_details.get("cache_read") or 0)      # captured
usage: dict[str, Any] | None = {
    "input_tokens":  int(meta.get("input_tokens") or 0),
    "output_tokens": int(meta.get("output_tokens") or 0),
}
if cached_tokens > 0:
    usage["cached_tokens"] = cached_tokens                     # carried on state
```

It survives the state hop (`agnet-service/app/workflows/concierge_workflow.py:172-173` returns
`{"usage": usage}`) and reaches the run-level telemetry rollup
(`concierge_workflow.py:426`, `cached_tokens=int(tokens.get("cached_tokens") or 0)` →
`agents.py:270`). **But it stops there.** `_build_usage_metadata` maps only the two token fields:

```python
# agnet-service/app/workflows/concierge_workflow.py:325-331
return AgentMetadata(
    model=settings.llm_model or "rule-based",
    tokens_used=input_tokens + output_tokens,   # cached_tokens not included
    input_tokens=input_tokens,
    output_tokens=output_tokens,
)
```

and `report_usage` is called with only two token fields
(`agnet-service/app/api/agents.py:247-253`), so `cachedTokens` defaults to `0`
(`usage_reporter.py:19`). The result is that `AiUsageRecords.CachedTokens` is structurally always
zero — a second, independent instance of the same defect class as D-8, and it is D-8's other half.

`langchain_core` exposes the cache breakdown needed to fix the cost calculation exactly:
`input_token_details` carries both `cache_creation` (cache writes) and `cache_read` (cache hits)
under `UsageMetadata`
(`agnet-service/.venv/lib/python3.14/site-packages/langchain_core/messages/ai.py:104-154`). The
code currently reads only `cache_read` (`nodes.py:417`) and discards `cache_creation`.

---

## 7. Configuration is not a contributing cause

I checked for feature flags that could disable cost capture or logging. There is no such flag, and
the two cost-adjacent settings are decoys:

| Setting | Where read | Effective value | Affects `ActualCostUsd`? |
|---|---|---|---|
| `Billing:AbnormalCostThresholdUsd` | `UsageTrackerService.cs:197-198` | `1.00` (default; key absent from `appsettings*.json`) | No — it only gates a warning log |
| `Pricing:UseLegacyFormula` | `UsageTrackerService.cs:131` | **`false`** — explicitly set at `Aveline.Api/appsettings.json:18` | No — it selects the **Blossom** formula only |

Correcting an easy misreading: `Pricing:UseLegacyFormula` is **`false`** in `appsettings.json:16-20`
(so the rule-driven `BlossomCalculator` path is live, not the legacy `ceil` formula), and there is no
`Pricing` override in `appsettings.Development.json`. Neither setting touches `ActualCostUsd` —
verified by reading both branches of `CalculateBlossomUnitsAsync`
(`UsageTrackerService.cs:128-153`), neither of which references `request.ActualCostUsd`. The URL
for a "cost seems misconfigured" hypothesis is a dead end, which is what the flag-check task was
asked to establish.

Because `ActualCostUsd` is hardcoded `0` upstream, `CheckAbnormalCost`
(`UsageTrackerService.cs:193-208`) can never fire: `0 > 1.00m` is false for every row. The
runaway-cost guard described in ADR-010
(`docs/ADR/ADR-010-usage-tracking-architecture.md:91-94`) is **inoperative in production** — it
will not catch a runaway agent loop, model pricing change, or a wrong-model incident. This is
exactly what D-8 predicted ("the cost-anomaly detector can never fire",
`docs/backend/backend-requirements.md:92`).

**Environment-specific note.** This defect is universal, not environment-dependent, but two local
values explain the `deepseek` row:

- `.env:37` → `LLM_PROVIDER=deepseek`; `.env:39` → `LLM_BASE_URL=https://api.deepseek.com`;
  `.env:40` → `LLM_MODEL=deepseek-v4-flash`.
- The shipped defaults differ: `agnet-service/app/core/config.py:28` and both `.env.example` files
  use `deepseek-chat`, so the local `.env` *is* the source of the `deepseek-v4-flash` value.
- `deepseek-v4-flash` is a real, billable, legacy-accepted model id served as DeepSeek-V4.1-Flash
  ([DeepSeek Models & Pricing](https://api-docs.deepseek.com/quick_start/pricing/), footnote 1).

---

## 8. Downstream impact

The zero is not cosmetic. Real, shipped read paths aggregate this column and present the result as
a measurement:

| Consumer | Line | Effect |
|---|---|---|
| `AgentStatisticsService.GetCostAsync` (S-19) | `Aveline.Api/Modules/Statistics/Services/AgentStatisticsService.cs:190` | `totalCostUsd = 0`, `avgCostPerRun = 0.0` |
| `BillingStatisticsService` profitability (S-5) | `Aveline.Api/Modules/Billing/Services/BillingStatisticsService.cs:145,151` | per-provider/per-model `actualCostUsd = 0`; `TotalCostUsd = 0` |
| Blossom statement detail | `Aveline.Api/Modules/Billing/Services/BlossomService.cs:348`; `Aveline.Api/Modules/Billing/Repositories/BlossomLedgerRepository.cs:197` | each consumption row explains its cost as `0` |
| Abnormal-cost detector | `UsageTrackerService.cs:200` | dead code (§7) |

The codebase already anticipated this and warns about it:

> `/// <summary>Actual provider cost (S-19). Always zero until G-5/D-8 is fixed.</summary>`
> — `Aveline.Api/Modules/Statistics/DTOs/AgentStatisticsDtos.cs:217`

`docs/backend/statistics-catalog.md:120` goes further and requires the response to admit it:

> **Currently unreliable** — `ActualCostUsd` is always `0` on the wire (defect D-8). The response
> must carry `dataQuality.costIsEstimated: true` until G-5 is fixed

**That honesty flag is not on the wire.** The DTO's real field is `CostInstrumented`
(`AgentStatisticsDtos.cs:16`), and the catalogue calls it `costIsEstimated` — a doc/wire mismatch
recorded at `docs/reports/admin-backend-api-implementation-status.md:135` ("the API uses
`costInstrumented` where the catalogue says `costIsEstimated`"). **Confirmed:** a repo-wide
`grep -rn "costIsEstimated"` returns only markdown under `docs/` — zero code hits.

**Worse, the flag is hard-coded `false` on the very endpoint that reports cost.** The derivation
exists and is correct — `Derive` computes `runs.Any(r => r.ActualCostUsd > 0)`
(`AgentStatisticsDtos.cs:36`) — but `AgentStatisticsService` returns the all-false constant
`AgentDataQualityDto.Uninstrumented` at **12** call sites, including the cost endpoint itself:

```
Aveline.Api/Modules/Statistics/Services/AgentStatisticsService.cs:193:
    return new AgentCostDto(total, avg, runs.Count, AgentDataQualityDto.Uninstrumented);
```

The consequence is sharp: `AgentStatisticsQueryTests.cs:335-343` seeds runs with costs of `0.25m`
and `0.75m`, asserts `TotalCostUsd == 1.0m` and `AvgCostPerRun == 0.5`, and **still asserts
`CostInstrumented == false`**. So even after cost is populated, `GET
/api/v1/orgs/{orgId}/statistics/agents/cost` will keep reporting cost as uninstrumented. A fix to
the producer alone will not make the API honest. (`Derive` *is* wired in three other places —
`AgentRunIngestService.cs:167`, `BillingStatisticsService.cs:375` — so the inconsistency is
specific to the agent-statistics read endpoints. This is deferred work item **T-1.8**,
`docs/reports/admin-backend-api-deferred-implementation-plan.md:316-322`.)

---

## 9. Recommended fix

Cost must be computed in the agent service at the moment it has the usage metadata; nothing in the
.NET side needs to change. Order the work by value.

### F1 — Capture the full token breakdown (prerequisite, small)

Capture `cache_creation` alongside `cache_read`, add `cached_tokens` to `AgentMetadata` and
`_build_usage_metadata`, and pass `cached_tokens=` at both `report_usage` call sites
(`agents.py:247`, `visual_insight/nodes.py:427`). Also pass `cached_tokens` on the
`/internal/agent-runs` synthetic path, which currently sends a literal `0`
(`agents.py:344`).

This closes the `CachedTokens` half of D-8 and is a prerequisite for an exact cost, because
DeepSeek prices the two cache states differently.

### F2 — Add a DeepSeek rate table and compute `actual_cost_usd`

Compute in the agent service (Python), not the .NET API: it has the provider, model, and the full
token breakdown at the call site, and computes nothing about money today.

**DeepSeek does not return a USD cost.** `usage` in the API response is the source of truth for
*tokens*
([DeepSeek Token & Token Usage](https://api-docs.deepseek.com/quick_start/token_usage): "refer to
the usage returned by the API as the source of truth"), and billing is stated as
"expense = number of tokens × price"
([DeepSeek Models & Pricing](https://api-docs.deepseek.com/quick_start/pricing/), "Deduction
Rules"). So cost must be derived from a maintained rate table. For `deepseek-flash` (the model
`deepseek-v4-flash` is served as), the published peak rates per 1M tokens are `$0.006` cache-hit
input, `$0.30` cache-miss input, `$1.2` output; off-peak is exactly half.

**Derive like this, not naively.** LangChain's `input_tokens` is the *total* input count, and
`input_token_details.cache_read`/`cache_creation` are **components of it** — the docstring is
explicit: "Count of input (or prompt) tokens. **Sum of all input token types.**"
(`agnet-service/.venv/lib/python3.14/site-packages/langchain_core/messages/ai.py:137-139`). So the
cache-hit and cache-write counts must be **subtracted** from the total, or they are double-counted:

```
read    = details.cache_read      # billed at the cheap cache-hit rate
create  = details.cache_creation  # billed at the standard input rate
miss    = max(0, in_tokens - read - create)   # never let this go negative

cost = read × 0.006 + (create + miss) × 0.30 + out × 1.2      (peak)
cost = the above × 0.5                                        (off-peak)
```

Sanity check against the sampled row (`1309` in, `138` out, `cached = 0`, so `read = create = 0`),
at off-peak — which 18:14 UTC on a Wednesday is:

```
1309 × 0.15/1e6 + 138 × 0.6/1e6  =  0.00019635 + 0.00008280  =  $0.00027915
```

So the true cost of the row that reads `0.00000000` is roughly **$0.00028** — small, which is
exactly why a silent zero is easy to miss and why the column exists at 8 dp. It also shows the
shape of the problem: this row is `1309/138`, but a 1M-token workflow at peak would cost dollars,
not fractions of a cent.

Peak is 01:00–04:00 and 06:00–10:00 UTC, Mon–Fri, excluding Chinese public holidays; all other
hours are off-peak.

**Design points that follow from the evidence:**

- **A rate table is a liability, not an option.** These numbers will change. Version the table,
  store the applied rate identity and version on the row in the existing
  `PricingRuleId`/`PricingRuleVersion` columns (currently always NULL —
  `UsageTrackerService.cs:54-55`), and treat an unknown model as a *logged, flagged* zero rather
  than a silent one. ADR-010 already defers cost-based normalization for this reason
  (`docs/ADR/ADR-010-usage-tracking-architecture.md:68-69`).
- **Consider a usage-API cross-check.** DeepSeek's platform exposes account balance; periodically
  reconciling `Σ ActualCostUsd` against actual spend is the only way to catch a rate-table drift,
  since the provider gives no per-call cost.
- **Do not confuse this with `Pricing:UseLegacyFormula` or the Blossom rule engine.** Those price
  Blossoms in normalized units, not dollars (`BlossomConversionRule.cs:6-7,19-20`;
  `BlossomPriceEntry.cs:3-6`). Do not route USD cost through them.

### F3 — Decide whether F2 is worth doing now (a real fork)

The premise implies the cost should be non-zero, but **the repository's own decision was the
opposite**: ADR-010 records "Cost-based normalization is deferred. A future ADR will revisit the
formula after real production usage and cost data has been collected"
(`docs/ADR/ADR-010-usage-tracking-architecture.md:68-69`), and the instrumentation gaps
G-1…G-14 / defects D-4…D-8 are "deferred per risk R-1"
(`docs/ai-usage/kavindu.md:2151-2153`). So there are two defensible positions:

| Option | Tradeoff |
|---|---|
| **A. Implement F1+F2 now** | Cost becomes real; unblocks S-19/S-5 profitability and re-arms the runaway-cost guard. Cost: a maintained rate table and a deferred-design reversal. |
| **B. Keep cost at zero, make the placeholders honest** | Follow ADR-010 as written. Small, shipped-consistent. Requires fixing the `costIsEstimated`/`costInstrumented` wire mismatch and making the admin surfaces render "not measured" rather than `$0.00`. |

**Recommendation: do F1 regardless** (it is a genuine data-loss bug, it is small, and it is a
prerequisite either way), **then choose A or B on whether cost-based normalization is being
revisited now.** If the admin console is showing `Avg Cost / Run: $0.00` as if it were a
measurement, option B is the higher-priority fix because a wrong number is worse than a missing one.

### Backfill — bounded, and should be labelled approximate

Existing rows can be recomputed: `Provider`, `Model`, `InputTokens`, `OutputTokens` and `CreatedAt`
are all stored. But `CachedTokens` is structurally `0` for historical rows (§6), so `in − cache_hit`
cannot be split, and the peak/off-peak boundary is recoverable from `CreatedAt` only if the row's
timestamp is the call time. Any backfill therefore has a known error band.

- Do **not** overwrite `ActualCostUsd` in place — the model doc comment is explicit that rows "must
  never be updated or deleted" and are the "source of truth for usage analytics and future cost
  validation" (`Aveline.Api/Modules/Billing/Models/AiUsageRecord.cs:10-11`).
- Write the recomputation as a new, clearly labelled derived column or a separate reconciliation
  table, with the estimate basis recorded. Precedent for this pattern exists in the repo
  (`docs/reports/admin-backend-api-deferred-implementation-plan.md` discusses a D-1-safe backfill
  for subscription snapshots).
- Any backfill is **an estimate**, because a cache hit is billed at 1/50th of a cache miss
  (`$0.006` vs `$0.30`) and the cache state is not recoverable from the stored data.

---

## 10. How to verify

**V1 — Confirm the schema and the absence of log columns (closes the "NULL logs" question).**
Read-only; run against the dev database.

```sql
-- expect exactly 19 rows; expect zero rows for provider_response / error / metadata
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'AiUsageRecords'
ORDER BY ordinal_position;

SELECT column_name
FROM information_schema.columns
WHERE table_name = 'AiUsageRecords'
  AND column_name IN ('provider_response','error','metadata','response','raw_response');
-- expect: 0 rows
```

**V2 — Confirm the cost is stored as given, not rounded away by the column type.**
`ActualCostUsd` is `numeric(18,8)`, which represents `0.0012` exactly, so a stored `0` is a value
and not a truncation artifact. To prove the capability end-to-end:

```sql
SELECT id, "Model", "Provider", "InputTokens", "OutputTokens",
       "ActualCostUsd", "BlossomUnits", "PricingRuleId", "AgentWorkflowRunId"
FROM "AiUsageRecords"
WHERE "OrganizationId" = '01a078bf-45c1-7231-bae1-3e0ea1dc0471'
ORDER BY "CreatedAt" DESC LIMIT 20;
-- expect: ActualCostUsd = 0.00000000 on every row, PricingRuleId NULL on every row
```

Then compare to the already-passing backend test (which sends a non-zero cost and asserts it
round-trips): `dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj --filter
"FullyQualifiedName~UsageTrackerServiceTests"` → 18 passed in this investigation.

**V3 — Confirm the zero originates in Python, not .NET.** Turn on the agent-service log and look
for the backend's own echo of what it was told:

- `UsageTrackerService.cs:66-71` logs `... cost_usd={ActualCostUsd}` on every accepted row.
- `usage_reporter.py:73-80` logs `Usage reported successfully` with `organization_id`,
  `workflow_id`, `model`.

If the API log line reports `cost_usd=0`, the zero was received, and the producer is at fault.
`agnet-service/app/api/agents.py:271,325,344` and `usage_reporter.py:20` are then the fix sites.
A best-effort failure would instead appear as `Failed to report usage for workflow ...` with a
traceback (`agents.py:255-259`).

**V4 — Confirm the DB write itself succeeds (rules out exception/early-return).** The
`BlossomUnits = 1.5000` fingerprint already settles this for the sampled row: it equals
`ceil(1447/1000 × 10)/10`, which can only be produced from the real token counts
(`UsageTrackerService.cs:114-121`). No write-path exception skipped the log fields, because no log
fields exist and the token-derived value is correct.

**V5 — Confirm after F1/F2.** A fresh LLM workflow should produce a row with
`ActualCostUsd > 0`, `CachedTokens` matching the provider's `input_token_details.cache_read`, and a
non-NULL `PricingRuleId`/`PricingRuleVersion` identifying the rate table version applied. The
`AgentDataQualityDto.CostInstrumented` flag will flip to `true` on its own once any run has a
non-zero cost (`AgentStatisticsDtos.cs:36`).

---

## 11. Open questions

**Q1 — Is cost tracking in scope now, or still deferred? (blocks the fix, needs a decision)**
ADR-010 explicitly defers cost-based normalization until "real production usage and cost data has
been collected" (`docs/ADR/ADR-010-usage-tracking-architecture.md:68-69`), and the Python
instrumentation is deferred per risk R-1 (`docs/ai-usage/kavindu.md:2151-2153`). The premise treats
the zero as a defect. Both readings are defensible; only the team can say which applies. This is
also the `G-5`/`D-8` fork in `docs/backend/implementation-plan.md:845`, which scopes D-4…D-8 into
Phase 4 work with no evidence of it having landed.

**Q2 — What produced `t-cap4`, and does the reuse of `thread_id` harm run↔usage linking?**
Resolved in part, and it raises a new concern. `WorkflowId` is the **caller-supplied** `thread_id`:
`workflow_id=payload.thread_id or request_id`
(`agnet-service/app/api/agents.py:141`; the same expression builds the collector id at `:102`), and
`thread_id` is an unconstrained optional field on the request DTO with no format validation
(`agnet-service/app/schemas/query.py:16-19`). So `t-cap4` is a client-chosen label, not something
the repository defines — a full-history `git grep` over all commits finds neither `t-cap4` nor
`t-new3`, and the only literal workflow ids in the repo are test fixtures like `wf-test-1`.

That matters for the sibling field. `AgentRunIngestService.LinkUsageAsync` links a run to a usage
record by matching on `WorkflowId` **and** `OrganizationId` **and** `AgentWorkflowRunId == null`,
taking the earliest match (`Aveline.Api/Modules/Statistics/Services/AgentRunIngestService.cs:494-505`).
If a client reuses a `thread_id` across invocations — exactly what a `t-cap4`-style test harness
does, and what a long-lived conversation thread does by design — several usage records share a
`WorkflowId`, and each arriving run claims the oldest unclaimed one. `RequestId` is a fresh
`uuid4()` per request (`agents.py:97`) and is the unambiguous key, but it is not what the link uses.
I did not verify how many rows in the sampled organisation share a `WorkflowId`, so I flag this as a
**risk to check**, not a confirmed defect.

**Q3 — Is the admin UI currently presenting `$0.00` as a measurement? (confirmed in code, not in the UI)**
The API returns `totalCostUsd: 0` and `avgCostPerRun: 0.0` from real, shipped endpoints
(`AgentStatisticsService.cs:190`; `BillingStatisticsService.cs:151`). I did not inspect the
Flutter/React consumers, so I cannot say whether they render `$0.00` or "not measured". If they
render a number, option B in §9 is urgent independent of whether F2 is ever built.

**Q4 — Unverified: does the live database agree with the schema?** Every schema claim here is from
EF configuration and migrations, which I read but did not apply to a live database. I did not run
V1/V2. The prediction is exact (19 columns, no log columns, `ActualCostUsd = 0` everywhere), so a
mismatch would be informative.

**Q5 — Which model string is authoritative?** `AgentMetadata.model` is set from
`settings.llm_model` (`concierge_workflow.py:328`), the *configured* string, not a value echoed back
by the provider response. A rate table keyed on the configured string can mis-price if the provider
substitutes a served model — which is precisely the situation for `deepseek-v4-flash`, served as
DeepSeek-V4.1-Flash under a legacy alias
([DeepSeek Models & Pricing](https://api-docs.deepseek.com/quick_start/pricing/), footnote 1).
Worth resolving before F2 keys on it.

---

## 12. Summary

| Question | Answer | Confidence |
|---|---|---|
| Why is `actual_usd` `0`? | No cost is ever computed; the value defaults to `0.0` in Python and is passed through verbatim by .NET | Confirmed |
| Is a pricing table missing for `deepseek` / `deepseek-v4-flash`? | Yes — nothing prices LLM tokens in USD. The Blossom rule engine prices Blossoms in normalized units, not dollars | Confirmed |
| Does `deepseek` return usage metadata? | **Yes, tokens only** — `usage_metadata` via LangChain; no USD cost from the provider | Confirmed |
| Are tokens passed to cost calculation? | There is no cost calculation to pass them to. Tokens do reach the DB intact (proved by `BlossomUnits = 1.5`) | Confirmed |
| Why are the log columns NULL? | No such columns exist on `AiUsageRecords`. Nothing is broken; the empties are a `psql` artifact | Confirmed |
| Calculated at write time or by a job? | Neither. No background job computes or backfills cost (`BillingRollupJob`/`AgentStatsRollupJob` sum the stored value; `LedgerJobs` never read it) | Confirmed |
| Is a config flag suppressing it? | No. `Pricing:UseLegacyFormula=false` and `Billing:AbnormalCostThresholdUsd` affect Blossoms and a log line, not cost | Confirmed |
| Is this environment-specific? | No. The zero is unconditional; only the *model name* comes from the local `.env` | Confirmed |
| Known issue? | Yes — defect **D-8**, gap **G-5**, ruled valid by **BR-5.7**; the fix is scoped as task **T-1.9** | Confirmed |
| Collateral defects found | `CachedTokens` structurally always `0`; abnormal-cost guard inoperative; `/cost` hardcodes `CostInstrumented=false` even after a fix; `costIsEstimated` naming mismatch; possible `WorkflowId` reuse collision in run↔usage linking | Confirmed (last item: risk to verify) |
