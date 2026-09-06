# ADR-010: Blossom Usage Tracking Architecture

## Status
Accepted

## Context
The Aveline pricing model (`docs/architecture/pricing_plan.md`) defines a Blossom credit
system where every AI workflow consumes a normalized unit of AI work. To support future
billing, plan enforcement, and usage analytics, the platform must record the token usage
and Blossom consumption of every AI workflow from the moment the system goes live.

This ADR covers the initial recording and monitoring infrastructure only. Payment
processing, subscription enforcement, and billing cycle management are deferred.

## Options Considered

### Where should usage be persisted?

1. **Record in the .NET API via an internal HTTP call from the agent service.**
   - Pros: Single EF Core migration owner (ADR-001, ADR-003). Agent service already calls
     the .NET API for auth (ADR-009). No new framework dependency. Future gRPC migration
     (when LangGraph tools adopt it) maps cleanly to the same logical contract.
   - Cons: One extra HTTP round-trip after each workflow.

2. **Record directly from the Python agent service (SQLAlchemy / asyncpg).**
   - Pros: Fewer hops.
   - Cons: Two writers to the same PostgreSQL schema. Migrations must be coordinated
     across two services. Violates the single EF Core owner principle.

### How should usage be structured?

1. **Single append-only table (`AiUsageRecord` only).**
   - Simpler. Running totals derived by aggregation at query time.
   - Cons: Expensive queries at scale; cannot record plan limits without joining.

2. **Two-table design: immutable log (`AiUsageRecord`) + mutable ledger (`UsageAccount`).**
   - `AiUsageRecord`: append-only, one row per workflow — full audit trail.
   - `UsageAccount`: one row per org per billing period — fast running total reads.
   - Cons: Write must update both within a transaction.

### How should Blossom units be calculated?

1. **Token-based normalization (initial):**
   `blossom_units = ceil((input_tokens + output_tokens + cached_tokens) / 1000, 1 dp)`
   Minimum increment: 0.1 Blossom.

2. **Cost-based normalization:** Map actual USD cost → Blossom value.
   Better long-term but requires real production cost data first.

## Decision

1. **Usage recording is owned by the .NET API.** The Python agent service calls
   `POST /internal/usage/record` using `httpx` and the `X-Internal-Token` header
   (ADR-009) after each LangGraph workflow completes. This is not a LangGraph node;
   it is a post-execution call in the workflow's terminal step. When LangGraph tools
   migrate to gRPC in a future slice, this HTTP contract will map cleanly to a gRPC
   service definition.

2. **Two-table design.** `AiUsageRecord` is the immutable audit log. `UsageAccount`
   is the mutable billing-period ledger. Both are written atomically in a single
   database transaction per workflow completion.

3. **Token-based Blossom normalization for the initial implementation.**
   Formula: `ceil((input + output + cached) / 1000, 1 dp)`.
   Minimum charge: 0.1 Blossom. BlossomUnits are calculated server-side in
   `UsageTrackerService` and must never be accepted from the caller.

4. **Cost-based normalization is deferred.** A future ADR will revisit the formula
   after real production usage and cost data has been collected.

5. **Active customer definition (not enforced this slice):** A customer is considered
   active if they have had an interaction, order, or profile update within the last
   90 days. Historical records remain accessible without counting toward the plan limit.
   This definition will be enforced when the subscription/enforcement slice lands.

6. **No enforcement in this slice.** `blossom_remaining` is tracked and persisted but
   no request is blocked or throttled. Enforcement is deferred to the
   subscription/payment slice.

## Consequences

- The `.NET API` remains the single writer of all business data to PostgreSQL, consistent
  with ADR-001 and ADR-003.
- A `Billing` vertical-slice module (`Modules/Billing/`) is introduced, following the
  established module pattern from ADR-001.
- Every AI workflow in the agent service must call `report_usage()` after completion.
  This is a non-fatal, best-effort call — a failure to record usage logs a warning but
  does not fail the workflow response to the end user.
- The `UsageAccount` ledger provides O(1) reads for the current Blossom balance without
  aggregating the full `AiUsageRecord` log.
- An internal cost anomaly threshold (`Billing:AbnormalCostThresholdUsd`, config-driven)
  emits a structured `[ABNORMAL_USAGE]` warning log when a single workflow's actual cost
  exceeds the threshold. This protects against runaway agent loops and model pricing
  changes without blocking customer requests.
- The HTTP endpoint contract (`POST /internal/usage/record`) is designed to be a direct
  predecessor of a future gRPC service definition, minimizing migration effort.
