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

## Consent enforcement (privacy plan, Phase 1)

Consent is enforced at three layers, from the ingress inward. A revoked customer's data is never
read or written, and the customer is not silently ignored: the inbound message itself is still
recorded (it is the evidence the customer contacted the boutique and that the objection was
honoured), but no agent runs.

| Action | Where it is enforced | Behaviour on `revoked` |
|---|---|---|
| Agent dispatch (whole workflow) | `ConversationService.TriggerInboundDraftAsync` via `IConsentGateService` | No `POST /agents/query` is issued. The webhook still returns 200 and still writes `InboundMessageLogs` + `Messages`. |
| Orchestrator routing (visual / commerce) | `_route_after_memory` on `ConciergeState.consent_status` | Short-circuits to `formulate_response`; `run_visual_agent` and `run_commerce_agent` never run. |
| Agent sub-graph (memory) | `CustomerMemoryAgent.check_consent` | `skipped`; nothing retrieved, parsed, persisted or drafted. |
| Memory write | `CustomerMemoryService.SaveMemoryAsync` | Returns `null`; nothing stored. |
| Memory read (semantic search) | `CustomerMemoryService.SearchAsync` | Returns an empty list; the embedding call and the store are never reached. |
| Brief generation | `CustomerMemoryService.GenerateBriefAsync` | Returns `null`; profile, tags and events are not read. |
| Interaction log write | `CustomerInteractionService.RecordAsync` | Returns `null`; endpoint answers 400, nothing written. |
| Event write | `CustomerEventService.AddAsync` | Returns `null`; endpoint answers 400, nothing written. |
| Streaming query | `POST /agents/query/stream` (its own pre-check, since it bypasses `run_concierge`) | A single `consent_skipped` SSE frame; the graph is never built. |
| The inbound message row itself | `RecordInboundClientMessageAsync` | **Deliberately not blocked** (§8.4). |

Rules, stated once in `ConsentGateService`:

- **No bound customer ⇒ process.** An unknown number cannot be consent-gated, and the message must
  still be recorded. The outcome carries `reason = no_customer_context` so it stays observable.
- **`revoked` ⇒ skip.** No other status does.
- **A read failure ⇒ fail closed.** The gate reports `unavailable` and nothing is processed; it
  never throws, so a consent-store outage cannot 500 message handling or the agent query.

`ConsentGateReasons` (`consent_revoked`, `no_customer_context`, `consent_check_unavailable`) are
the label values of the `aveline_message_skip_total{reason}` counter
(`Aveline.Api/Modules/CustomerConcierge/Metrics/ConsentMetrics.cs`). A consent skip is reported
through the agent envelope as its own `skipped` status and as `Skipped` on the run record — never
as `success`/`Succeeded`.

## First-contact disclosure and the permanent opt-out link (privacy plan, Phase 3)

The first inbound message from an identified customer triggers a one-time, transactional WhatsApp
disclosure. It names the boutique, discloses that Aveline is an AI assistant, states that a real
member of the team reads every conversation and can step in, links the data policy, and links a
permanent opt-out URL. It closes with `Reply STOP to opt out, or HELP for a human.` This is **not**
the staff-facing "Welcome to your Salon" string in `ConversationService`; that is a different
message for a different audience.

**Where the trigger lives.** `WebhookEndpoints` (the inbound POST) is the first place that knows
both the organization and the identified customer. After the message is recorded it asks
`IConsentGateService` whether the customer may be processed, and if so enqueues a
`DisclosureIntent`. An unknown number is not enqueued (there is no consent row to stamp); it is
disclosed once the agent identifies it and it messages again. A revoked customer is never enqueued.

**Synchronous or asynchronous? (plan §15 Q-1).** Asynchronous. The webhook's contract is "return
200 fast and record what we can", and holding it open for a Meta round-trip risks a Meta retry that
re-enters the whole path. The webhook therefore only enqueues; `DisclosureDispatchWorker`
(`Modules/Privacy/Jobs/`) drains the queue, creates a DI scope per intent (the disclosure service
holds the scoped `AppDbContext` — the trap Pr2 documented in `IntegrationsModule`), and takes a
per-customer `IDistributedJobLock` as defence in depth. The queue is a bounded in-process
`Channel<T>`; the durable intent is `CustomerConsent.DisclosureShownAt IS NULL`, and the retry
trigger is the customer's own next inbound message, so a restart delays a disclosure rather than
losing it.

**Exactly once.** `DisclosureDispatchService.DispatchAsync` performs the §4.4 sequence:

1. `UPDATE CustomerConsent SET DisclosureShownAt = now(), DisclosureVersion = 'v1' WHERE
   OrganizationId = @o AND CustomerId = @c AND DisclosureShownAt IS NULL`. The affected-row count is
   the concurrency control: zero rows means "already disclosed", and nothing is sent. The
   `ExecuteUpdate` form is the atomic production path; the in-memory provider used by tests takes a
   documented read-check-write fallback because it does not support `ExecuteUpdate`.
2. The message is built from the boutique's `Name` and `Slug` and the signed links.
3. `IOutboundMessagingService.SendWhatsAppTextAsync` sends it under the stable key
   `disclosure:{organizationId}:{customerId}:v1`, so a replay returns the prior `wamid` without a
   second Meta call.
4. On success a `ConsentAuditEntry` with `Action = consent.disclosure.shown`, `Source =
   welcome_message` and `EvidenceJson = {"disclosureVersion":"v1","channel":"whatsapp"}` is written
   — identifiers only, never the number or the body.
5. On failure (provider refusal, unconfigured integration, missing organization) the claim is
   released (`DisclosureShownAt = NULL`) so the next inbound message retries.

If `Privacy:LinkSigningKey` is absent, the service records `SigningKeyMissing`, sends nothing and
leaves the stamp NULL. `PrivacyOptionsValidator` refuses to boot when the key is present but blank,
malformed or shorter than 32 bytes; an entirely absent key only warns, matching
`Credentials:EncryptionKey` (dozens of hosts do not use the privacy surface).

**The permanent opt-out link (plan §5.1, DR-2).** `PrivacyLinkSigner` produces
`{App:BaseUrl}/privacy/opt-out?o={organizationId}&v=1&s={hmac}`, where the signature is
base64url(HMAC-SHA256(`Privacy:LinkSigningKey`, `v1|{organizationId}`)). The version is part of the
signed payload, so a tampered `v` or `o` is rejected. There is no timestamp and no stored row, so
the link never expires. **No phone number appears in the URL**: the link proves which boutique, and
the OTP step of the opt-out flow (Phase 4) proves which number. The data-policy URL is
`{App:BaseUrl}/privacy?org={slug}`.

**Historical rows.** The data-only migration `BackfillDisclosureShownAt` sets
`DisclosureShownAt = CreatedAt` for every pre-existing consent row, so a deploy does not
mass-message the customer base. Backfilled rows keep a NULL `DisclosureVersion` because nothing was
actually shown.

The disclosure signals are `aveline_disclosure_shown_total` and `aveline_disclosure_unshown_total`
(`Modules/Privacy/Metrics/DisclosureMetrics.cs`).

## The OTP-verified opt-out flow (privacy plan, Phase 4)

The signed link proves *which boutique*; the one-time code proves *which number* (DR-2). The flow is
two anonymous routes on `/api/v1/privacy`, mapped by `Endpoints/PrivacyEndpoints.cs` and implemented
by `Modules/Privacy/Services/`. Neither carries a JWT and neither is reachable through a
boutique-scoped route: the OTP **is** the authentication.

**`POST /privacy/opt-out/start`** verifies the link signature, normalizes the number with
`PhoneNormalizer.ToE164`, charges the two start budgets, resolves the customer, and — only when the
customer exists and the boutique has a WhatsApp channel — mints and sends a code. The response is the
same `202` field set on every path — constant `status` and `expiresInSeconds`, plus a fresh opaque
`handle` the client returns on `verify` — so a known number, an unknown number, a boutique with no
WhatsApp integration, a failed send and an unsigned link are indistinguishable on the wire. The
handle is minted before the customer lookup, so it is present even when no code is stored behind it
(the unknown-number and unconfigured-boutique paths). That is the anti-enumeration contract; the
observable differences live in `AuditLogEntry` (`privacy.otp.issued`) and the logs only.

**`POST /privacy/opt-out/verify`** takes `{organizationId, handle, otp, scope, version, signature}` —
**no phone number**. The code's digest covers the number, so the endpoint revokes the number the code
proved, never one a caller supplied. `scope: "org"` revokes exactly the issuing organisation's row;
`scope: "all"` revokes every organisation's row whose stored `Customers.PhoneNumber` equals the
proven number (plan §5.4 Option 1, "identity by phone"), reading across tenants through
`IPhoneSubjectLocator` — the only cross-tenant read in the flow. Every failed verification (unknown
handle, expired, replayed, wrong code, spent attempts, bad signature) answers the same
`400 {"code":"otp-invalid"}`.

### OTP storage and the entropy argument

`OtpService` stores, under the key `otp:code:{handle}` with a **300-second TTL**, the value
`base64(SHA-256(otp + ":" + handle + ":" + phone))` — **never the code**. The `handle` is 32 random
bytes of base64url, returned to the client instead of the number so a verify call cannot be used to
probe which numbers have a pending code, and the phone is part of the hashed input so a code minted
for one number is not a proof for another. The digest is compared with
`CryptographicOperations.FixedTimeEquals`, and a success deletes the key before the caller is told
anything, which is what makes the code single-use.

**Six digits is about 19.9 bits**, which on its own is far too little for a bearer proof. It is
defensible **only** in combination with the three caps, and removing any one of them breaks the
argument:

| Cap | Value | What it buys |
| --- | --- | --- |
| Attempts per code | hard **5** (`otp:attempts:{handle}`, 300 s) | bounds one code's success probability at `5/10^6`; the sixth call is refused even with the correct code |
| Code TTL | **300 s** | bounds how long an attacker has to spend those five guesses |
| Sends per number | **3 / 15 min** (`otp:send:{phoneFingerprint}`) | bounds how fast a fresh code can be requested once one is burned |
| Starts per address | **10 / hour** (`otp:ip:{ipFingerprint}`) | bounds a single source's request rate |
| Verifies per address | **30 / hour** | bounds the attempt rate across codes |

Every counter is read straight from `IDistributedCache` and **fails closed** (decision DR-6): a Redis
outage is a `503`, never "allowed". This is deliberately the opposite of `DistributedRateLimiter`,
which documents and implements fail-open for sign-ups. No key, log line or audit row carries the raw
phone number — the counters use `PhoneFingerprint` (base64url SHA-256), the audit rows use the same
fingerprint as `ActorRef`.

### Revocation, audit and the acknowledgement

`ConsentRevoker` is the single writer behind both customer surfaces. It sets
`CustomerConsent.ConsentStatus = revoked`, stamps `ConsentRevokedAt` and `ConsentSource`, and writes
**two** audit rows per affected organisation: the append-only `ConsentAuditEntry`
(`consent.revoked`, `ActorKind = Customer`, `Source = otp_link`, `EvidenceJson = {scope, source}`) and
a generic `AuditLogEntry`. A missing consent row is **created** rather than skipped, so a customer who
never answered becomes a customer who objected and the gate stops processing them. A global
revocation additionally annotates `GlobalSubjectId` from the phone fingerprint; the column is an
annotation, not a second identity model.

The staff equivalent is `POST /api/v1/orgs/{organizationId}/customers/{customerId}/consent` under
`BoutiqueCustomerAccessPolicy` (`customers:manage` — `customers:view` is held by every role, so there
was nothing correct to gate a write on). It carries no `scope` and writes `ActorKind = User`,
`Source = staff`. **The two paths must remain distinguishable by `ActorKind`**; that is the only
reason two routes exist, and it is pinned by `StaffConsentRevocationTests`.

After a successful revocation, one **non-personalised** acknowledgement ("You have opted out of
messages from {boutique}…") is queued. `DistributedOptOutAcknowledgementGate` is a Redis key that
allows at most **one per (boutique, number) per 24 hours**; a refused send releases the key so the
confirmation is delayed rather than lost. The body is fixed copy built in `Aveline.Api` and the send
uses `IOutboundMessagingService.SendWhatsAppTextAsync` — **never `SendTemplateAsync`**, which is
gated on the unresolved policy question Q-2 — so the message never re-enters the agent path.
Plan §15 Q-9 records why a revoked customer still receives this one message: a single confirmation is
necessary to honour the request they just made, and nothing follows it.

The metrics are `aveline_otp_{issued,verified,failed,start_refused}_total`
(`Modules/Privacy/Metrics/OtpMetrics.cs`) and `aveline_privacy_delivery_{delivered,failed}_total`
(`PrivacyDeliveryMetrics.cs`).

## Agent sub-graph flow

1. **resolve_customer** — from an org id plus customer id **or** phone number, load/create the
   profile. Without any customer context the agent short-circuits (`skipped`).
2. **check_consent** — revoked consent short-circuits; nothing is retrieved or stored. A backend
   failure fails closed (reports `unavailable`) instead of raising — see the enforcement table
   above. The resolved status is promoted to the orchestrator, which stops the visual and commerce
   agents too.
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
