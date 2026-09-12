# The Salon — Conversation Inbox Architecture

> **Status:** Approved (see [ADR-016](../ADR/ADR-016-conversation-inbox.md)).

This document describes how Aveline delivers agent output to staff and owners through a
unified, Instagram-like conversation surface called the **Salon**. It covers the persona
model, the `Conversation` and `Message` entities, rich content blocks, threading, the
realtime contract, and the relationship to notifications, the event bus, and WhatsApp.

---

## 1. Core principle

> One conversation is the single home for every interaction about a customer or topic.
> Staff, Aveline, and the three specialists (Ava, Elle, Lina) all participate in the same
> thread. Notifications are transient pings that deep-link into a specific message; they
> are not themselves the content.

The product's users are boutique owners and staff. Boutique customers are external
parties, so they never appear as a sender. Their inbound WhatsApp/Instagram messages
surface as a `ClientMessage` card inside the Salon.

---

## 2. Personas (the voices in the Salon)

A message is attributed to an **author**. The author is either a staff user, an agent
persona, or the system.

| Author | Kind | Identity | Design accent |
|---|---|---|---|
| **Staff** | `user` | `userId` (Clerk-backed `User.Id`) | neutral charcoal |
| **Aveline** | `agent` | `aveline` | primary wine-rose (the house) |
| **Ava** | `agent` | `ava` | memory magenta-rose `#b0566b` |
| **Elle** | `agent` | `elle` | visual gold/brass |
| **Lina** | `agent` | `lina` | commerce wine-rose `#8b2e42` |
| **System** | `system` | `(none)` | muted neutral |

Persona mapping to the agent service:

| Persona | Agent graph (`agnet-service/app/agents/`) | Role |
|---|---|---|
| `aveline` | supervisor (orchestrator) | routes intent, summarizes, introduces |
| `ava` | `customer_memory` | understands the customer |
| `elle` | `visual_insight` | understands the product, composes looks |
| `lina` | `commerce` | pricing, payments, sign-off, delivery |

Aveline is a **first-class orchestrator sender**, distinct from the specialists. It emits
its own messages (introductions, summaries, "I have asked Ava and Elle") and, where
useful, replies are attributed to whichever specialist actually produced the content.

---

## 3. Entities

### 3.1 `Conversation` (the Salon)

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `OrganizationId` | Guid | tenant scope (filtered on every query) |
| `Kind` | enum `ConversationKind` | `Salon` (unified) now; `Announcement`, `Digest` reserved |
| `CustomerId` | Guid? | optional; the external customer this Salon concerns |
| `ThreadId` | string | LangGraph checkpoint key (the context anchor) |
| `ExternalRef` | string? | channel reference, e.g. WhatsApp conversation id |
| `Status` | enum | `Active`, `AwaitingSignOff`, `Resolved`, `Archived` |
| `CreatedAt` / `UpdatedAt` / `LastMessageAt` | DateTime | |

### 3.2 `Message`

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `ConversationId` | Guid | FK |
| `Author` | value object | `kind` (user/agent/system) + `userId?` + `agentKey?` |
| `Kind` | enum `MessageKind` | render type (see §4) |
| `ContentBlocks` | JSON | ordered array of typed blocks (see §5) |
| `ReplyToMessageId` | Guid? | threading |
| `WorkflowRunId` / `TraceId` | Guid? | audit: link to a LangGraph run |
| `Status` | enum `MessageStatus` | lifecycle (see below) |
| `CreatedAt` | DateTime | |

### 3.3 `MessageStatus`

`Draft` → `AwaitingSignOff` → `Published`/`Sent` → `Delivered` → `Read`, plus terminal
`Failed` and `Cancelled`. Internal notes are `Published`; outbound customer messages move
through `Sent`/`Delivered`/`Read`. `AwaitingSignOff` means the message is staged and
waiting for staff approval before it goes to the customer.

---

## 4. Message kinds (quiet-luxury vocabulary)

| Kind | Meaning | Typical author |
|---|---|---|
| `Note` | plain text | any |
| `Look` | image / moodboard / visual reference | Elle, staff |
| `Piece` | a curated inventory item card | Elle, Aveline |
| `AtAGlance` | tabular comparison (options, sizes, prices) | any agent |
| `ClientMessage` | inbound WhatsApp/Instagram forwarded from the customer | System |
| `SignOff` | human-in-the-loop approval request | Lina |
| `Payment` | payment link / invoice | Lina |
| `Courier` | delivery update | Lina |
| `Suggestion` | a soft, proactive recommendation | Aveline, Ava |

---

## 5. Content blocks

`ContentBlocks` is an ordered JSON array. Each block has a `type` and type-specific
fields. A message may carry a single block or a composite (e.g. a `Note` plus several
`Piece` blocks for "here are three dresses matching Michael's request").

```json
[
  { "type": "text", "text": "Three pieces match Michael's brief." },
  { "type": "piece", "itemId": "…", "name": "Silk Slip Dress", "price": 24000,
    "size": "M", "stock": 2, "imageUrl": "…" },
  { "type": "at_a_glance", "columns": ["Size", "Price", "Stock"],
    "rows": [["M", "24000", "2"], ["L", "26000", "1"]] },
  { "type": "sign_off", "approvalId": "…", "orderId": "…", "amount": 48000,
    "reason": "above discretionary limit" }
]
```

Block types: `text`, `piece`, `look`, `at_a_glance`, `sign_off`, `payment`, `courier`,
`suggestion`, `client_message`, `choice`.

### 5.1 Emitted blocks from real agent output

The agent service maps the concierge `AgentResponse.output` into blocks in
`agnet-service/app/events/block_builders.py`. A persona posts a message only when it
produces real content (no placeholder text).

- **Aveline summary** (`build_aveline_blocks`): a single `text` block that is intent-aware
  and names the resolved customer when the memory agent found one. It never duplicates
  Ava's rich detail. When the orchestrator cannot resolve a customer it instead renders a
  **clarification** (`build_clarification_blocks`): an ambiguous lookup becomes a `choice`
  block listing candidate customers to tap; a not-found lookup becomes a `text` block asking
  for a phone number (Issue #161).
- **Ava / memory** (`build_ava_blocks`), in order:
  1. `text` - the `interaction_brief`.
  2. `at_a_glance` - one `Category`/`Content` row per `extracted_memories` entry.
  3. `suggestion` - the customer-facing `draft_response`.
  When the memory agent skipped (no customer context), no Ava message is emitted.
- **Elle / visual** (`build_elle_blocks`): emits `suggestion`, `piece` (per `items`), and
  `look` (per `looks`) blocks when the visual output carries content.
- **Lina / commerce** (`build_lina_blocks`): emits `text` (summary), `payment`, and
  `courier` blocks when the commerce output carries content.
- **Stub discipline (Issues #150/#151)**: the pre-Slice 2/3 stub nodes set
  `status: "stub"` with no content, so Elle/Lina stay silent and the Salon never shows
  fabricated product/payment data. A `sign_off` card is never emitted from these generic
  builders - a SignOff is a first-class HITL message (`kind == SignOff`).

#### 5.1.1 `choice` block (customer resolution)

A `choice` block lets staff pick a customer when Aveline's lookup was ambiguous:

```json
{
  "type": "choice",
  "prompt": "I found a few customers that could match. Which one did you mean?",
  "options": [
    { "customerId": "…", "fullName": "Samantha Arias", "status": "vip", "lastVisitAt": "2026-08-20" },
    { "customerId": "…", "fullName": "Samantha Ranaweera", "status": "returning", "lastVisitAt": null }
  ]
}
```

Tapping an option calls `POST /orgs/{orgId}/conversations/{id}/select-customer`
(`{ customerId, query }`), which binds the Salon's `CustomerId` and re-triggers the agent
with that customer in context so Ava pulls up their profile/events.

---

## 6. Flows

### 6.1 Staff triggers a query
Staff opens the Salon → sends a `Note`. API calls the agent service (`/agents/query` or
`/agents/query/stream`, `agnet-service/app/api/agents.py`) with the conversation's
`threadId`. Agents reply with attributed messages; streaming tokens are bridged to the
client so the reply types in live. `WorkflowRunId`/`TraceId` are recorded for audit.

### 6.2 WhatsApp inbound
Webhook (`/api/v1/webhooks/whatsapp/{orgId}`, ADR-015) → persist `InboundMessageLog` →
publish `message.received` → API creates a `Message` of kind `ClientMessage` in the
customer's Salon (so staff see it immediately) → the API then asks the agent service to
draft a response into the same thread (`/agents/query` with the conversation's `threadId`
and the client's phone in `org_context`), so the memory agent can try to identify the
customer. Both steps are best-effort and never delay the webhook `200`.

### 6.3 Human-in-the-loop sign-off
Commerce agent issues an interrupt (`pause_for_approval`, `agnet-service/app/agents/commerce/README.md`)
→ agent emits a `SignOff` content event → API persists a `Message` of kind `SignOff`
(`AwaitingSignOff`) → staff approve or reject inline; the API records the decision (bound to a
content hash) and updates the message/conversation status.

> **Deferred (ADR-018):** resuming the paused LangGraph workflow via `threadId`, writing
> `ApprovalQueueEntry` (`threadId` + `conversationId`), and dispatching an `ApprovalNeeded`
> notification deep-linking to the message all land with the Commerce approval flow, which
> must add a real `pause_for_approval` interrupt first. No fake resume is used today.

### 6.4 Notification deep-link
`Notification.Data` = `{ "conversationId": "…", "messageId": "…" }`. Tapping a
notification opens the Salon scrolled to the message.

### 6.5 Threaded agent replies
Ava, Elle, and Lina reply to specific messages via `ReplyToMessageId`, matching the
conversation example:

```
Staff:  Message 1
Aveline: Message 2
Ava:    Message 3
Elle:   Message 4  (reply to Message 2)
Lina:   Message 5  (reply to Message 3)
```

---

## 7. Realtime

- **SignalR**: a new group `salon:{conversationId}` joins the existing `user:{id}` and
  `org:{id}` groups (`Modules/Notifications/Hubs/NotificationHub.cs`). Clients subscribe
  to `ReceiveMessage`.
- **Streaming bridge**: agent SSE (`/agents/query/stream`) → Redis bus → API subscriber →
  persist `Message` → SignalR broadcast. Clients never open SSE directly.
- **New event types** (ADR-014): `conversation.created`, `message.created`,
  `message.updated`. Existing `agent.status` drives the "thinking" indicator.
- **`message.updated` handling**: when an agent revises a message, the API subscriber
  (`ConversationEventSubscriber.OnMessageUpdatedAsync`) applies the new `status` and/or
  `blocks` to the persisted message and rebroadcasts it. Editing a SignOff's payload
  re-binds its `contentHash` to the revised content. Unknown messages are skipped without
  failing the listener.

---

## 8. Ownership and boundaries

- The **API is the system of record** for `Conversation` and `Message`. It persists rows,
  enforces tenant isolation, and broadcasts.
- The **agent service** emits content events with `author.agentKey`, `kind`, `blocks`,
  `threadId`, and `workflowRunId`; it never writes message rows directly.
- **Notifications** remain separate (ADR-013); they reference messages, they are not
  messages.

---

## 9. Design tokens (quiet luxury)

Reference `.agents/brain/DESIGN.md`. Key values for the Salon UI:

- Palette: warm oatmeal surfaces, wine-rose primary `#8b2e42`, agent accents (memory
  magenta-rose `#b0566b`, visual gold, commerce wine-rose).
- Type: Playfair Display for greetings/headers, DM Sans for body and dense lists.
- Shapes: 16px radius cards, pill inputs, tonal layering, soft warm shadows.
- Motif: the 8-petal Blossom (the unit of AI work).

See [conversation_messaging_implementation.ignore.md](../../conversation_messaging_implementation.ignore.md)
for the full build plan.
