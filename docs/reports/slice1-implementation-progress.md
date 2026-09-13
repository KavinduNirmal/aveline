# Slice 1 — Customer Concierge & Memory: Implementation Progress Report

> Owner: Student 1 (Kavindu) · Domain: Customer Concierge & Memory
> Status: **Implemented & tested, awaiting merge to `development`** · Last updated: 2026-09-10

## 1. Scope

Slice 1 is the boutique's "relationship brain": it turns raw WhatsApp/Instagram messages into a
living customer profile and prepares staff for every interaction. The agreed scope (from
`README.md` and `docs/architecture/customer-memory.md`) is:

- WhatsApp webhook handling and the Meta integration gateway.
- Customer profile management (identity, preferences, events, tags, consent).
- Semantic memory search over customer memories via `pgvector`.
- The Customer Memory Agent (Python/LangGraph sub-graph).
- Interaction brief generation for staff.
- Loyalty-tier progression and event reminders.
- The shared "General Salon" customer resolution from free text (`@name` / `#phone`).
- Surfacing agent output into the Salon as persona-attributed messages (the messaging loop).

## 2. Deliverables — implemented

### 2.1 WhatsApp integration gateway (Issues #113–#119)
`Aveline.Api/Modules/Integrations`, `WebhookEndpoints`, `Modules/CustomerConcierge`, `frontend/web`.

- Integration status lifecycle (`Pending/Connected/Error/Expired/Disconnected`) + migration.
- Meta (WhatsApp) provider: `TestConnectionAsync`, `SendMessageAsync`, HMAC webhook signature
  verification (`WebhookSignatureVerifier`), inbound audit log, `message.received` event publish.
- Connect/test endpoints, integration health worker, React `IntegrationsPanel`.
- Guardrails: optional Meta IP allow-list + per-org/per-IP rate limiting.

### 2.2 Customer profile & memory data layer (Issues #143–#145)
`Aveline.Api/Modules/CustomerConcierge/{Models,Repositories,Services}`.

- 7 tenant-scoped entities: `Customer`, `CustomerPreference`, `CustomerEvent`, `CustomerMemory`,
  `CustomerInteraction`, `CustomerConsent`, `CustomerTag`.
- `pgvector` `vector(1536)` column + HNSW cosine index via raw SQL migration (ADR-017).
- `EmbeddingService` (`IEmbeddingService`, OpenAI-compatible) for memory embeds.
- Full internal API under `/internal/customers/*` (guarded by `InternalServicePolicy`, ADR-009):
  `identify`, `lookup`, `profile`, `memories`, `memories/search`, `brief`, `interactions`,
  `consent`, `events`, `status`.

### 2.3 Customer Memory Agent (Issues #146–#147)
`agnet-service/app/agents/customer_memory/`, `app/schemas/customer_memory.py`, `app/tools/registry.py`.

- Typed Pydantic schemas (`extra="forbid"`) and `ToolRegistry` memory methods bound to the real
  internal endpoints.
- LangGraph sub-graph: `resolve_customer → check_consent → parse → retrieve → persist →
  compose_output`, dependency-injected (fully testable without an LLM or live backend).

### 2.4 Realtime Salon messaging loop (Issues #150–#159)
`Aveline.Api/Modules/Conversations`, `agnet-service/app/events/`.

- Real agent output mapped to rich Salon content blocks (`block_builders.py`).
- Specialist stubs (Elle/Lina) emit structured output (real sub-graphs are Slice 2/3).
- Inbound WhatsApp loop: `ClientMessage` → agent draft → `message.created`.
- `message.updated` application; batched content delivery + `agent.status` lifecycle (ADR-018).

### 2.5 Shared customer resolution (Issues #161–#162, ADR-019)
`agnet-service/app/customer_resolution/`, `Aveline.Api/Modules/Conversations`.

- Deterministic `@name` / `#phone` extraction + `/lookup` resolution (`resolved | ambiguous |
  not_found | no_signal`).
- `choice` block UI (tap a candidate) + `select-customer` round-trip (web + mobile).
- Brand-new customer onboarding via `#phone` mention.

### 2.6 LLM, usage & runtime validation (Issues #163–#168)
- LLM draft generation via `memory_llm_or_none` (`AGENT_LLM_ENABLED` + key/model), deterministic
  template fallback.
- Always-on usage/Blossom reporting to `/internal/usage/record` (ADR-010).
- Runtime output validation against `MemoryAgentOutput` (schema drift fails loudly).
- Email lookup, intent-type consistency, stale agent-README cleanup.

### 2.7 Loyalty & event reminders (Issues #169–#170)
- `CustomerLoyaltyService`: `new → returning → vip → dormant` from spend/visit/recency,
  recompute/override via `POST /internal/customers/{id}/status`.
- `EventReminderService` + daily `EventReminderWorker` dispatch `NotificationType.EventReminder`
  for active events in a 30-day horizon, each reminded once.

## 3. Verification / testing

| Layer | Evidence |
|---|---|
| Python agent | `pytest tests/` green (268 passed, 2 skipped as of finalization); `ruff` clean; coverage 94%+ (gate ≥ 90). Tests: `test_customer_memory_agent.py`, `test_customer_memory_schemas.py`, `test_customer_resolution.py`, `test_mentions.py`, `test_concierge_workflow.py`, `test_block_builders.py`, `test_intent_gate.py`, `test_event_bus.py`. |
| .NET API | `dotnet build` clean; in-memory + Testcontainers pgvector suites green. Tests: `CustomerConcierge{EntityConfiguration,Repository,LookupRepository,LookupService,Service,EndpointsIntegration,SearchPostgres}Tests`, `CustomerMemoryRepositoryPostgresTests`, `CustomerLoyaltyServiceTests`, `EventReminderServiceTests`, `PhoneNormalizerTests`, `Webhook{EndpointsIntegration,SignatureVerifier}Tests`. |
| Web | `bun run lint/test/build` green (~85 tests); mobile `flutter analyze` clean. |
| Husky | Pre-commit gates (secrets, lockfiles, `dotnet build`, Python syntax, `flutter analyze`) pass on every commit. |

## 4. Delivery status

- Branches: `feature/slice1-whatsapp-integration-gateway` → `feature/slice1-customer-memory-agent`
  → `feature/slice1-customer-resolution` → `feature/slice1-ava-finalize` (tip).
- `feature/slice1-ava-finalize` is **31 commits ahead of `development` and not yet merged**.
- Pushed to `origin/feature/slice1-customer-resolution`; a PR into `development` is still outstanding.

## 5. Remaining / deferred work (honest list)

These are the items **not** completed in Slice 1. Two are deliberately deferred to later slices,
and the rest are optional follow-ups:

1. **Outbound WhatsApp send loop (deferred).** The provider `SendMessageAsync` exists, but the
   "approve draft → actually send to the customer" outbox is not wired. Today the loop is
   inbound-only: the agent drafts a reply and staff review it in the Salon, but sending the
   approved reply out is future work.
2. **Human-in-the-loop SignOff resume (deferred to Slice 3).** `DecideSignOffAsync` records the
   decision + status, but the actual LangGraph pause/resume is deferred until the Commerce agent
   adds a real `pause_for_approval` interrupt (ADR-018).
3. **Loyalty data population (depends on Slice 3).** Tiering logic is implemented and tested, but
   `TotalSpent`/`VisitCount`/`LastVisitAt` are populated by Slice 3 purchase activity, so the rule
   is inert on real data until then.
4. **Optional optimizations (non-blocking).** Postgres `ILIKE` name matching, write-side phone
   normalisation in `identify`, and an optional LLM name-extraction layer (from the resolution
   session notes).

## 6. Completion assessment

**Core Slice 1 deliverables are implemented, tested, and integrated end-to-end.** Customer
identity, semantic memory (pgvector), consent, the memory agent sub-graph, staff-facing briefs and
drafts, the WhatsApp webhook, and the Salon messaging UI all work together and are covered by tests.

**However, you should not claim "zero remaining development in Slice 1."** Two items are genuinely
unfinished by design and live inside Slice 1's messaging responsibility: the **outbound send loop**
and the **SignOff resume** (both explicitly deferred). Loyalty also awaits Slice 3 data. Finally,
the branch has **not been merged to `development`**, so it is not yet "delivered".

The accurate statement is: *"Slice 1 is feature-complete for its agreed in-scope deliverables;
the only remaining items are the two explicitly deferred loops (outbound send, SignOff resume),
the Slice 3-dependent loyalty data, and the pending merge/PR into `development`."*
