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
Intent Gate (agent-service/app/gate.py)
      │  intent_type + routing
      ▼
Customer Memory Agent sub-graph (agent-service/app/agents/customer_memory/)
  resolve_customer → check_consent → parse → retrieve → extract → persist → compose_output
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
| Typed I/O schemas | `agent-service/app/schemas/customer_memory.py` |
| Backend client (`ToolRegistry`) | `agent-service/app/tools/registry.py` |
| LangGraph sub-graph (nodes, state, parsing) | `agent-service/app/agents/customer_memory/` |

The Python agent never writes to the database and never calls third parties directly — it calls
the API's internal endpoints guarded by the `X-Internal-Token` header (ADR-009).

## Data model

All entities are tenant-scoped (`OrganizationId`) and created by the
`AddCustomerConciergeEntities` migration:

| Table | Purpose | Notes |
|---|---|---|
| `Customers` | Customer identity | unique `(OrganizationId, PhoneNumber)`, status `new/returning/vip/dormant/deleted`, optional staff-written `Description`, soft-delete |
| `Customer_Preferences` | Stated / inferred preferences | `preference_key/value`, `is_explicit`, `confidence` |
| `Customer_Events` | Weddings, birthdays, parties… | `event_type`, `event_date`, `is_active` |
| `Customer_Memory` | Semantic memory | `embedding vector(1536)` + HNSW cosine index and a generated `SearchVector tsvector` + partial GIN index over live rows (both raw SQL, ADR-017; `AddCustomerMemorySearchVector`); normalised `ContentKey` with a partial unique index over `(OrganizationId, CustomerId, ContentKey)` where `DeletedAt IS NULL`; optional `ExpiresAt` |
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
| POST | `/{id}/memories` | Persist a semantic memory with its provenance (embeds content) |
| GET | `/{id}/memories` | List a customer's live memories, newest first |
| PATCH | `/{id}/memories/{memoryId}` | Correct one memory's statement and re-embed it (409 on a collapse) |
| DELETE | `/{id}/memories/{memoryId}` | Withdraw one memory (soft delete) |
| POST | `/memories/search` | hybrid (dense + lexical, RRF-fused) search; `mode` = `hybrid`\|`lexical`\|`vector`, `minSimilarity` bounds the dense leg only |
| POST | `/{id}/preferences` | Record a stated preference in the preferences table |
| GET | `/{id}/brief` | Staff-facing interaction brief (description/events/tags/preferences) |
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
| Memory correction / withdrawal | `CustomerMemoryService.CorrectMemoryAsync` / `RemoveMemoryAsync` | Returns `null` / `false`; the row is untouched. |
| Preference write | `CustomerMemoryService.SavePreferenceAsync` | Returns `null`; nothing stored. |
| Memory list | `CustomerMemoryService.ListMemoriesAsync` | **Deliberately ungated.** The customer's own memory panel is what this backs and the tenant route behind it was never gated; gating it would empty a panel whose notes are already on screen. |
| Outbound customer delivery | `CustomerDeliveryService` via `IConsentGateService` | `DeliveryOutcome.Refused(ConsentRevoked, …)`; nothing leaves and no message row is written. |
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
both the organization and the sender. It resolves the sender against the customer book by phone
and, when the number is new, creates the profile there and then (`IdentifyOrCreateAsync`), so a
first-time customer has a consent row to stamp. It then asks `IConsentGateService` whether the
customer may be processed - passing the number as well, so an erasure tombstone is honoured - and
if so enqueues a `DisclosureIntent`. A revoked or erased customer is never enqueued. Creating the
profile in the webhook, before `message.received` is published, also means the agent's own identify
finds the row rather than racing it. An unknown number used to be skipped here, which meant a
brand-new customer received no disclosure on the first message they ever sent.

**The message itself is an image with the notice as its caption.** `DisclosureImageComposer`
(`Modules/Privacy/Services/`) draws a co-branding lockup - the Aveline mark and wordmark, a cross,
and the boutique's own name - knocked out to white on the app's aurora, at 1200x675. The fonts
(Playfair Display SemiBold, DM Sans SemiBold and Regular) are embedded resources, because the
runtime image is chiseled with no font packages. `DisclosureImageProvider` renders it, publishes it
to Cloudinary under a public id that carries a hash of the boutique's name and the layout version,
and returns the unsigned, version-less delivery URL for Meta to fetch. **The text remains the
fallback**: if no image can be produced or Meta refuses the image, the identical notice goes out as
text under the same idempotency key. The image is decoration on a consent notice, and a notice must
not depend on an image host.

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
2. **apply_staff_update** — a staff instruction to change the bound customer's details. This is the
   only path that writes identity fields or staff notes from chat, and it runs **before**
   `check_consent`, so it consults consent itself and refuses a revoked or unreadable customer:
   revocation means nothing is stored, and who is asking does not change that. A **note**
   instruction ("add a note for this customer: he prefers green tea") is stored verbatim as a
   `category="note"`, `source="staff"`, `confidence=1.0` memory and confirmed back to staff; a
   note-only instruction sends no identity PATCH. A failure is reported, never swallowed.
3. **check_consent** — revoked consent short-circuits; nothing is retrieved or stored. A backend
   failure fails closed (reports `unavailable`) instead of raising — see the enforcement table
   above. The resolved status is promoted to the orchestrator, which stops the visual and commerce
   agents too.
4. **parse** — deterministic rule parsing extracts intent (occasion/colour/size/budget), explicit
   preferences ("I like/prefer/love/hate …"), event signals, and a complaint or sentiment signal.
   A preference's polarity comes from the pattern's own negation group, and a value that names
   nothing on its own (a bare pronoun, a whole trailing clause) is dropped rather than stored.
5. **retrieve** — hybrid search over prior memories for context: a pgvector cosine leg and a
   PostgreSQL full-text leg fused with Reciprocal Rank Fusion (ADR-025). The similarity floor
   (`MEMORY_SIMILARITY_FLOOR = 0.25`) bounds **the dense leg only**, so an unrelated note is not
   handed back as context while an exact-token match still is, and each hit's `source`, `isExplicit`,
   `confidence`, `similarity`, `vectorRank` and `lexicalRank` are preserved.
6. **extract** — with a model configured, reads the small, durable facts ("nitbits") out of an
   inbound message: the ones no deterministic shape covers, such as an observation ("browsing a
   brown dress seen on Instagram") or a fact whose object is a pronoun the regex had to drop
   ("nothing nylon, or spandex, I dont like them"). The bounded transcript is supplied so the model
   can resolve what a reference points at, and the prompt labels it as being for that **reference
   resolution only** — it is never itself a source of facts. Each stored fact is one self-contained
   sentence that starts with the customer's name, so retrieval does not depend on which conversation
   is asking. The category is a closed set (`preference`, `event`, `observation`, `complaint`,
   `constraint`); an unknown category, a malformed entry, an empty or over-long sentence is dropped,
   facts are capped per turn, confidence is clamped to 0..1, and `stated` records whether the
   customer asserted the fact about themselves or the boutique observed it. Best-effort throughout:
   no model, a staff query, an empty message, a provider failure or an unparseable reply all yield
   no facts and never fail the run.
7. **persist** — saves explicit preferences, detected events and the quoted complaint/sentiment as
   `Customer_Memory` rows **with their provenance** (`source="conversation"`, `IsExplicit=true`,
   `confidence=0.90`), mirrors a stated preference into `Customer_Preferences` (the table the
   brief's summary is assembled from), records the inbound interaction with its parsed intent
   (`Customer_Interactions`), and creates a **structured `Customer_Event` row** for each dated
   event (undated signals stay text-only). The model-extracted facts are written **after** the
   deterministic classes, under the model's own provenance (`is_explicit=stated`, its clamped
   confidence), skipping any fact whose normalised content a class already wrote this turn; an
   extracted preference carrying a `key`/`value` is mirrored into `Customer_Preferences` too.
8. **compose_output** — enriches the `interaction_brief` from the backend
   `CustomerMemoryService.GenerateBriefAsync` (real events/tags/status), **leads it with the
   customer's own `description`** when one is on file, and adds the semantic context. An inbound
   customer message also gets a draft reply via the LLM (falling back to a deterministic template
   when no LLM/key is configured or the provider fails); a **staff question is answered from the
   same grounded facts and retrieved memories** rather than by counting them, again falling back to
   the deterministic template. The result is validated against `MemoryAgentOutput` before return.

### Memory retrieval (hybrid, per-leg modes)

`POST /internal/customers/memories/search` runs the same retrieval pattern as the handbook
(ADR-025): one statement, two legs over the same filter, fused by Reciprocal Rank Fusion.

- **`mode`** is `hybrid` (default; both legs fused), `lexical` (PostgreSQL full-text only) or
  `vector` (pgvector cosine only). The single-leg modes exist so the retrieval evaluation can report
  vector, lexical and hybrid recall separately; the agent always asks for the default. An absent
  value means `hybrid`, and a value the store cannot run is refused (`400`) rather than silently
  defaulted.
- **`minSimilarity`** bounds the **dense leg only**. A lexical hit has no cosine score to floor, and
  a post-fusion floor would delete exactly the lexical-only hits the hybrid exists to surface. The
  agent sends `MEMORY_SIMILARITY_FLOOR = 0.25`.
- **Per-leg ranks are returned.** `vectorRank`, `lexicalRank` and the fused `score` travel with each
  hit alongside `similarity` (which is NULL for a lexical-only hit: a note with no cosine is not
  reported as `0`), so the eval scores each leg from the same response.
- **Degradation.** A failed query embedding runs the lexical leg alone rather than failing the
  request; every hit then carries a NULL `vectorRank`, which is how the degradation is visible on
  the wire.
- **Scope and liveness hold in every mode.** The org + customer scope, `DeletedAt IS NULL` and the
  `ExpiresAt` predicate are applied to both legs and to the outer select, so a withdrawn note, an
  expired note, or another customer's note is never returned in any mode.

### LLM, usage and runtime validation

- **LLM (Issue #163).** Draft generation uses `create_chat_model` when `AGENT_LLM_ENABLED` and an
  `LLM_API_KEY`/`LLM_MODEL` are set (`app/llm/runtime.py`). A **staff question** is answered by the
  same model from the grounded facts and retrieved memories, so the answer is about the customer
  rather than a count of notes; its prompt confines it to the supplied facts, and "that is not on
  file" is preferred over an invented detail. Without a model, or on provider failure, both paths
  fall back to their deterministic templates — CI and keyless dev never require a live model.
- **Fact extraction (the "nitbits").** An inbound turn also asks the model to record the small facts
  the deterministic classes cannot express — an observation, a constraint, a fact whose object was a
  pronoun. The transcript is given for reference resolution only and each stored sentence is
  self-contained, so a fact is findable later "regardless of her context". The node is best-effort:
  no model, a staff query, an empty message, a provider failure or an unparseable reply yields no
  facts, so the offline/CI writes are byte-identical to the pre-extraction agent. Extraction is a
  task contract beside the node, not a new agent persona: it reuses the memory system prompt through
  `assemble_system_prompt`, the same layer the draft and staff-answer calls use.
- **Usage (Issue #165).** Every completed `/agents/query` run reports usage to
  `/internal/usage/record` (ADR-010) — real provider/model + token split when the LLM ran, else a
  `rule-based` sentinel with zero tokens. An inbound LLM turn now makes two calls (extraction, then
  the draft) and the reported figure is their **sum**, because the contract is per run, not per
  call. Reporting is best-effort and never fails a query.
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
`agent-service/app/customer_resolution/`:

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
- **.NET Testcontainers Postgres** — real `vector(1536)` column, generated `SearchVector tsvector`
  + partial GIN index, cosine ordering, the similarity floor as a dense-leg predicate, the fused
  hybrid path (a lexical-only hit surfacing beside the dense winner), the tenant / soft-delete /
  expiry filters in every mode, and the end-to-end search path plus the live `/brief` payload shape
  (`pgvector/pgvector:pg16`).
- **Python pytest** — schema validation, `ToolRegistry` routing against a mocked client, the
  sub-graph golden cases (wedding inquiry, revoked consent, missing context, preference
  extraction), and rule-based parsing — all plain assertions.

## Related

- [ADR-017](ADR-017-memory-pgvector-embeddings.md) — embeddings & pgvector column decision.
- [The Salon / conversations](inbox.md) — staff see the agent's draft replies as persona messages.
