# Customer Memory Agent (AVA) — Architecture

> Slice 1 · Owner: Student 1 (Kavindu) · Domain: Customer Concierge & Memory

This document describes how Aveline "knows" its customers. The Customer Memory Agent is the
brain of the boutique's relationships: it turns raw WhatsApp/Instagram messages into a living
customer profile and prepares staff for every interaction.

## Overview

Inbound customer messages arrive via the WhatsApp gateway, are routed by the **Intent Gate**,
and reach the **Customer Memory Agent**. The agent identifies the customer, checks consent,
retrieves relevant semantic memories, records new preferences and events, and drafts a
staff-reviewed reply. All business logic and persistence live in the ASP.NET Core API; the
Python agent orchestrates via internal endpoints.

```
WhatsApp message
      │
      ▼
Intent Gate (agnet-service/app/gate.py)
      │  intent_type + routing
      ▼
Customer Memory Agent sub-graph (agnet-service/app/agents/customer_memory/)
  resolve_customer → check_consent → parse → retrieve → persist → compose_output
      │                (ToolRegistry → HTTP → ASP.NET Core internal endpoints)
      ▼
/orgs/{orgId}/...  clients / Salon (staff review the draft)
```

## Two runtimes, one contract

| Concern | Where |
|---|---|
| Entities, EF config, migrations, pgvector column | `Aveline.Api/Modules/CustomerConcierge/Models` + `Infrastructure/Data` |
| Repositories (data access, incl. cosine search) | `Aveline.Api/Modules/CustomerConcierge/Repositories` |
| Services (identify, memory, consent, interactions, events, brief, loyalty, reminders) | `Aveline.Api/Modules/CustomerConcierge/Services` |
| Embedding generation (`IEmbeddingService`) | `Aveline.Api/Modules/CustomerConcierge/Services` |
| Internal endpoints (`/internal/customers/*`) | `Aveline.Api/Endpoints/CustomerConciergeEndpoints.cs` |
| Typed I/O schemas | `agnet-service/app/schemas/customer_memory.py` |
| Backend client (`ToolRegistry`) | `agnet-service/app/tools/registry.py` |
| LangGraph sub-graph (nodes, state, parsing) | `agnet-service/app/agents/customer_memory/` |

The Python agent never writes to the database and never calls third parties directly — it calls
the API's internal endpoints guarded by the `X-Internal-Token` header (ADR-009).

## Data model

All entities are tenant-scoped (`OrganizationId`) and created by the
`AddCustomerConciergeEntities` migration:

| Table | Purpose | Notes |
|---|---|---|
| `Customers` | Customer identity | unique `(OrganizationId, PhoneNumber)`, status `new/returning/vip/dormant/deleted`, soft-delete |
| `Customer_Preferences` | Stated / inferred preferences | `preference_key/value`, `is_explicit`, `confidence` |
| `Customer_Events` | Weddings, birthdays, parties… | `event_type`, `event_date`, `is_active` |
| `Customer_Memory` | Semantic memory | `embedding vector(1536)` + HNSW cosine index (raw SQL, ADR-017) |
| `Customer_Interactions` | Inbound/outbound log | `channel`, `direction`, `parsed_intent` (jsonb) |
| `Customer_Consent` | Data-processing consent | one row per org + customer |
| `Customer_Tags` | Free-form labels | unique per customer |

## Internal endpoints

Routed under `/internal/customers`, all require the `InternalServicePolicy`
(`X-Internal-Token`, ADR-009). Org is always carried explicitly for tenant scoping.

| Method | Path | Purpose |
|---|---|---|
| POST | `/identify` | Look up a customer by phone, creating a `new` profile when absent |
| POST | `/lookup` | Read-only lookup by name and/or phone/email (never creates) - used to resolve a customer from free text |
| GET | `/{id}/profile` | Full profile (preferences, tags, consent) |
| POST | `/{id}/memories` | Persist a semantic memory (embeds content) |
| POST | `/memories/search` | pgvector cosine search over a customer's memories |
| GET | `/{id}/brief` | Staff-facing interaction brief (real events/tags/preferences) |
| POST | `/{id}/interactions` | Record an interaction (with parsed-intent JSON) |
| GET/POST | `/{id}/consent` | Read / update consent |
| GET/POST | `/{id}/events` | List / add customer events |
| POST | `/{id}/status` | Recompute/override the customer's loyalty tier |

The `/lookup` result is cached for 60s via `IDistributedCache` so repeat lookups skip the
database. Phones are matched in exact and E.164-normalised form; names use a case-insensitive
fragment match and emails an exact case-insensitive match.

## Agent sub-graph flow

1. **resolve_customer** — from an org id plus customer id **or** phone number, load/create the
   profile. Without any customer context the agent short-circuits (`skipped`).
2. **check_consent** — revoked consent short-circuits; nothing is retrieved or stored.
3. **parse** — deterministic rule parsing extracts intent (occasion/colour/size/budget), explicit
   preferences ("I like/prefer/love/hate …"), and event signals.
4. **retrieve** — semantic search over prior memories for context.
5. **persist** — saves explicit preferences and detected events as `Customer_Memory` rows, records
   the inbound interaction with its parsed intent (`Customer_Interactions`), and creates a
   **structured `Customer_Event` row** for each dated event (undated signals stay text-only).
6. **compose_output** — enriches the `interaction_brief` from the backend
   `CustomerMemoryService.GenerateBriefAsync` (real events/tags/status) plus semantic context,
   generates a draft reply via the LLM (falling back to a deterministic template when no LLM/key
   is configured or the provider fails), and validates the result against `MemoryAgentOutput`
   before returning it.

### LLM, usage and runtime validation

- **LLM (Issue #163).** Draft generation uses `create_chat_model` when `AGENT_LLM_ENABLED` and an
  `LLM_API_KEY`/`LLM_MODEL` are set (`app/llm/runtime.py`). Without them, or on provider failure,
  the agent is fully deterministic — CI and keyless dev never require a live model.
- **Usage (Issue #165).** Every completed `/agents/query` run reports usage to
  `/internal/usage/record` (ADR-010) — real provider/model + token split when the LLM ran, else a
  `rule-based` sentinel with zero tokens. Reporting is best-effort and never fails a query.
- **Schema validation (Issue #167).** The composed output is validated against
  `MemoryAgentOutput` (extra fields forbidden); drift yields a safe `error` status.

### Loyalty & reminders (Issue #169/#170)

- **Loyalty.** `CustomerLoyaltyService` derives `Customer.Status` from `TotalSpent`/`VisitCount`/
  `LastVisitAt` (new → returning → vip → dormant), exposed via `POST /internal/customers/{id}/status`
  for recompute or owner override. Spend/visit data is populated by purchase activity (Slice 3).
- **Reminders.** `EventReminderService` + `EventReminderWorker` (daily) dispatch a
  `NotificationType.EventReminder` to the boutique's staff for each active, un-reminded event
  within a rolling 30-day horizon, then set `ReminderSentAt` so each event is reminded once.

### Message-level customer resolution (shared, Issue #161)

When staff type natural language into the **General Salon** (e.g. "Any events for Samantha
Arias?") there is no `customer_id`/phone in context. The concierge orchestrator resolves the
customer **once** before dispatching specialists via the shared module
`agnet-service/app/customer_resolution/`:

- Deterministic extraction (`extract_phone`, `extract_customer_name`) finds a phone or a
  capitalized proper-name phrase in the message.
- `resolve_customer` calls `ToolRegistry.lookup_customers` (the `/lookup` endpoint) and returns
  a `CustomerResolution`: `resolved` | `ambiguous` | `not_found` | `no_signal`.
- `resolved`/`no_signal` proceed to the specialists, which read the resolved `customer_id` from
  shared state; `ambiguous`/`not_found` short-circuit to Aveline, who posts a `choice` block
  (tap a candidate) or an ask-for-phone `text` block. Tapping a candidate calls
  `POST /orgs/{org}/conversations/{id}/select-customer`, binding the Salon's `CustomerId` and
  re-triggering the agent with that customer in context.

Because resolution lives in the orchestrator and the shared state carries the result, the
capability is agent-agnostic - Ava uses it today and Elle/Lina can consume it later without
their own lookup logic.

The graph is a dependency-injected `ToolRegistry` consumer, so it is fully testable with a stub
(no LLM, no live backend).

## Testing strategy

- **.NET in-memory** — entities/EF config, repository CRUD, service logic, endpoint auth + happy
  paths (stubbed `IEmbeddingService`).
- **.NET Testcontainers Postgres** — real `vector(1536)` column, HNSW index, cosine ordering, and
  the end-to-end search path (`pgvector/pgvector:pg16`).
- **Python pytest** — schema validation, `ToolRegistry` routing against a mocked client, the
  sub-graph golden cases (wedding inquiry, revoked consent, missing context, preference
  extraction), and rule-based parsing — all plain assertions.

## Related

- [ADR-017](ADR-017-memory-pgvector-embeddings.md) — embeddings & pgvector column decision.
- [The Salon / conversations](inbox.md) — staff see the agent's draft replies as persona messages.
