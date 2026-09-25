# Integration Gateway — Architecture

> **Status:** Approved (see [ADR-015](../ADR/ADR-015-whatsapp-integration-gateway.md)).

This document describes how Aveline connects to each boutique's own WhatsApp credentials and
acts as the "integration gateway". It covers the credential lifecycle, the outbound provider,
the inbound webhook path, and the background health service.

---

## 1. Core principle

> Aveline never stores or handles third-party credentials in plaintext. Credentials are
> encrypted at rest (AES-256-GCM, ADR-011) and scoped per-organization. All integration
> traffic flows through the ASP.NET Core backend, which acts as the gateway. Clients never
> call Meta directly.

---

## 2. Components

| Component | Responsibility |
|---|---|
| `IntegrationCredentials` (table) | Per-org, per-type encrypted credential blob + lifecycle `Status`/`LastConnectedAt`/`LastError` |
| `CredentialEncryptionService` | AES-256-GCM, AAD-bound to `org:type` |
| `IntegrationService` | Save/list/test/delete + status transitions |
| `IWhatsAppService` / `WhatsAppService` | Meta outbound provider (test/send text/send template/validate) behind a typed `HttpClient` |
| `IOutboundChannel` / `WhatsAppOutboundChannel` | One delivery channel: per-org credentials, stable idempotency key, bounded retry, outbound `InboundMessageLog` write |
| `IOutboundMessagingService` / `OutboundMessagingService` | Channel-agnostic dispatcher over the registered channels; the single entry point for business-initiated sends |
| `WebhookEndpoints` | Public GET challenge + POST HMAC verification, audit log, `message.received` publish |
| `InboundMessageLog` | Minimal tenant-scoped message audit log |
| `IntegrationHealthService` | Background token-expiry detection + `IntegrationExpired` notification |
| `IntegrationEndpoints` | `settings:manage`-gated save/test/delete API |

---

## 3. Connect flow (owner provides credentials)

```
Owner → Settings → Integrations (React)
        │  PUT /orgs/{org}/integrations/whatsapp  { accessToken, phoneNumberId, appSecret, webhookVerifyToken }
        ▼
ASP.NET Core
  1. Validate required keys
  2. Encrypt (AES-256-GCM, AAD org:type) and store → Status = Pending
  3. Auto-connect: IWhatsAppService.TestConnectionAsync
        ├─ success → Status = Connected, LastConnectedAt = now
        └─ failure → Status = Error, LastError = <message>
        ▼
Owner sees Connected / Error badge
```

> **Note:** Meta webhook *subscription* is configured manually in the Meta App dashboard. The
> owner pastes the callback URL (`https://<api>/api/v1/webhooks/whatsapp/{orgId}`) and the
> verify token into Meta once. Aveline builds the receiving side.

---

## 4. Inbound message flow

```
[Meta WhatsApp Cloud API]
        │  POST /api/v1/webhooks/whatsapp/{orgId}   (X-Hub-Signature-256)
        ▼
WebhookEndpoints
  1. (optional) IP allow-list check
  2. Rate limit per org + IP
  3. Verify X-Hub-Signature-256 (HMAC-SHA256 of raw body vs stored appSecret, constant-time)
  4. Parse entry[].changes[].value.messages[]
  5. Persist InboundMessageLog (audit)
  6. Publish message.received on the Redis event bus (ADR-014)
  7. Return 200 immediately (non-blocking)
        │
        ▼
[Python Agent Service]  (subscribes to aveline:*:message.received)
  - parses message, identifies customer, triggers agents, drafts response
```

---

## 4a. Conversation-initiated orders and the approval loop (ADR-024)

A customer message is persisted, broadcast into the Salon, and handed to the agent service by
`ConversationService.TriggerInboundDraftAsync`. When the message asks to buy something, the API
resolves it to inventory **before** the call, so the commerce agent has real line items to evaluate.

```
Customer: "I want to buy the emerald green saree, please send the order"
        │
        ▼
ConversationService
  1. OrderContextBuilder: purchase signal + inventory name match
       -> items = [{ item_id, quantity, unit_price, wholesale_cost }]
       (price and cost come from the inventory row; an unresolvable piece is omitted, never guessed)
  2. POST /agents/query  { query, thread_id, org_context{ items, conversation_id, ... } }
        │
        ▼
Agent Service (LangGraph, checkpointed by thread_id)
  load_context -> supervisor -> resolve_customer -> memory -> visual -> commerce
                                                                        │
                         evaluate_deal: high-value / low-margin / discount rules
                                                                        │
                        breach? ── no ──> prepare_settlement -> payment link
                                └── yes ─> commerce_approval: interrupt(payload)   [graph PAUSES]
        │
        ▼
  Reply: status = pending_approval  +  output.approval { type, reason, triggered_rules }
        │
        ▼
ConversationService (on that reply only)
  3. IConversationOrderBridge -> IOrderService.CreateOrderAsync
       -> Order(status=pending_approval) + ApprovalQueueEntry(ThreadId, ConversationId)
       idempotent on (OrganizationId, ThreadId): a retried turn reuses the order it already drafted
        │
        ▼
Owner decides in the dashboard  ->  POST /approvals/{id}/decision { approve | reject | revise }
        │
        ▼
ApprovalService
  4. transitions the order, then POST /agents/resume { thread_id, decision: "approved" | ... }
        │
        ▼
Agent Service: Command(resume=...) re-enters AT commerce_approval
  (the supervisor is NOT consulted and nothing before the pause re-runs)
  approved/revised -> prepare_settlement -> payment link published into the Salon
  rejected         -> handle_rejection
```

Two things this diagram exists to make obvious:

- **The agent never writes an order.** It reports that one is required; the API prices, persists and
  transitions it (ADR-023 Decision 3, `SYSTEM_PROMPT.md` rule 10).
- **The resume is a resume, not a re-query.** Replaying the request would re-run every specialist and
  every tool call before the pause, and a decision carries no commerce signal, so the routing that
  was correct for the original message would be wrong for the decision.

---

## 5. Outbound message flow

There are **two** outbound paths, and they are deliberately different contracts.

### 5.1 Staff reply path (the console)

```
[Flutter / React] (staff sends)
        │  POST /orgs/{org}/conversations/{id}/messages
        ▼
CustomerDeliveryService
  1. Resolve the thread's customer and a connected channel
  2. IWhatsAppService.SendMessageAsync → Meta Cloud API
  3. Record a `Sent` Message in the thread + broadcast via SignalR
        ▼
[WhatsApp Cloud API] → Customer's phone
```

This path records a *conversation message* and reports a refusal to the associate. It does **not**
retry: the associate is looking at the result and owns the decision.

### 5.2 Business-initiated path (the privacy/compliance senders)

```
DisclosureDispatcher / OtpService / opt-out acknowledgement   (Phase 3+)
        │  (own DI scope)
        ▼
IOutboundMessagingService.SendWhatsAppTextAsync(org, toE164, text, idempotencyKey)
        ▼
WhatsAppOutboundChannel
  1. IntegrationService.GetCredentialsAsync(org, WhatsApp)
        IntegrationNotConfiguredException → { IsSuccess=false, Skipped=true }   (never throws)
  2. Missing accessToken or phoneNumberId → configuration failure, not a send attempt
  3. Idempotency pre-check on InboundMessageLogs (OrganizationId, ExternalId = idempotencyKey)
        hit → return the prior success WITHOUT calling Meta
  4. IWhatsAppService.SendMessageAsync, with bounded retry at THIS layer:
        3 attempts, backoff 250ms → 1s → 4s, jittered
        retry only on transport failure (no HTTP status) and 429/5xx
        never retry any other 4xx
        total delay capped (8s) so a webhook path is never held open
  5. On success: write InboundMessageLog { Channel="whatsapp", Direction="outbound",
        ExternalId=idempotencyKey, From=<Meta wamid>, To=toE164, Content=text }
        A DbUpdateException means the row was already written → treat as recorded
  6. On failure: return { IsSuccess=false, Error } and log with the phone masked
  7. The message body is NEVER logged
        ▼
[WhatsApp Cloud API] → Customer's phone
```

**Why the retry is here and not in the provider.** The idempotency key must mean the same thing
across every attempt of one logical message; a provider-level retry would make each attempt a fresh
logical send as far as the caller can see. `WhatsAppService` therefore performs exactly one attempt
and surfaces Meta's HTTP status on `WhatsAppSendResult.HttpStatus`, and this layer decides whether to
try again. There is no Polly or resilience package: the policy is a few lines in
`WhatsAppOutboundChannel` and does not justify a dependency.

**Why `ExternalId` holds the idempotency key and `From` holds Meta's `wamid`.** The pre-check and
the unique filtered index on `(OrganizationId, ExternalId)` must agree on one column for "the same
key twice ⇒ one send" to be a database-enforced guarantee. The caller cannot know the `wamid` before
sending, so the key is what belongs in `ExternalId`; Meta's own id goes in `From` (for an outbound
row the sender is us) and stays visible in `GET /orgs/{id}/integrations/messages`. `Content` holds
the message text — what the customer received, and therefore what a data-subject request must erase.

### 5.3 Templates and the policy gate (Q-2)

`IWhatsAppService.SendTemplateAsync(accessToken, phoneNumberId, to, templateName, languageCode,
components)` and `IOutboundChannel.SendTemplateAsync(...)` exist, are implemented, and are tested for
payload shape. They exist because a business-initiated message outside an open 24-hour customer
session is generally only permitted with an approved template.

> **Hard gate.** No proactive (non-reply) customer messaging may ship until Meta's template rules are
> confirmed by policy (privacy plan open question Q-2). The capability must exist so it is not
> blocked behind a rewrite, but **no proactive sender may be wired to it in this phase.** The reply
> path (§5.2) uses free-form text, which the customer's own inbound message makes permissible inside
> the session window. Instagram DM rules are a separate, unperformed research task (Q-5) and must not
> be assumed to match WhatsApp's.

### 5.4 Instagram reports honestly

`IntegrationType.Instagram` is a credential-only enum value: there is no provider client, no
webhook, no OAuth flow and no send method. `IntegrationService.TestConnectionAsync` therefore reports
Instagram as **not valid** and leaves it `Pending` with a "not implemented yet" error instead of
marking it `Connected` without a provider call. `IOutboundChannel.IsConfiguredAsync` returns `false`
for a channel whose provider does not exist, so a parity badge can stop showing green for something
Aveline cannot use. Instagram inbound/outbound remains out of scope (`.agents/plans/conversation_messaging_implementation.ignore.md:517`, ADR-015 "encrypt-only stub").

> **Deferred, deliberately:** `CustomerDeliveryService` (§5.1) still calls `IWhatsAppService`
> directly, so a staff reply is not yet recorded as an outbound `InboundMessageLog` and does not
> retry. Consolidating it onto `IOutboundMessagingService` is a follow-up slice; doing it inside the
> privacy phase would change a staff-facing contract that has its own tests and refusal vocabulary.

---

## 6. Expiry / health flow

```
IntegrationHealthService (BackgroundService, default every 6h)
        │
        ▼
for each Connected WhatsApp credential:
  1. Decrypt credentials
  2. IWhatsAppService.TestConnectionAsync
        ├─ valid   → leave Connected
        └─ invalid → Status = Expired, LastError = <message>
                     dispatch IntegrationExpired notification (SignalR + push) to owner
```

---

## 9. Observability

The outbound path publishes one counter, `aveline.outbound_message.result`, exported as
`aveline_outbound_message_result_total{channel,result}` with a bounded vocabulary:

| `result` | Meaning |
|---|---|
| `sent` | A provider accepted the message (after however many retries). |
| `skipped` | The channel is not configured, or its credentials are incomplete. **No HTTP call was made.** |
| `failed` | An attempt was made and every attempt failed, or the failure was terminal (a non-429 4xx). |

`skipped` versus `failed` is the distinction that matters operationally: a boutique with no
credentials must not look like a Meta outage. A `timeout` in the transport layer surfaces as
`failed`, with no HTTP status. The label keys are listed in `MetricsCatalog`; a tenant, customer or
message id is forbidden there because it would create one series per entity.

---

## 10. Configuration

| Setting | Purpose |
|---|---|
| `Credentials:EncryptionKey` | Base64 32-byte AES key (ADR-011) |
| `WhatsApp:BaseUrl` | Meta Graph base URL (default `https://graph.facebook.com`) |
| `WhatsApp:ApiVersion` | Meta Graph API version (default `v21.0`) |
| `IntegrationHealth:IntervalHours` | Health-check interval (default 6) |
| `Webhook:AllowedIps` | Optional Meta webhook IP allow-list |

No new configuration key is required for outbound messaging: the retry budget and attempt count are
constants on `WhatsAppOutboundChannel` (`DefaultMaxAttempts = 3`, `DefaultMaxTotalDelay = 8s`).

---

## 11. Security

See [integration-security-review.md](../security/integration-security-review.md). Highlights:
encryption at rest + AAD binding, constant-time HMAC webhook verification, optional IP
allow-list, per-org rate limiting, tenant-isolated audit log, and secret masking in logs.

Outbound-specific properties:

- **The message body is never logged.** Both `WhatsAppService` and `WhatsAppOutboundChannel` log
  only the masked recipient, the provider message id, the template name and Meta's error text.
  `OtpService` and the disclosure body builder must follow the same rule (Phase 4).
- **Phone numbers are masked in every log line** (`+94****4567`), including failure paths.
- **Provider errors are returned, never surfaced to a customer**: Meta's `error.message` can echo
  the request, so it is for logs and staff-facing text only.
