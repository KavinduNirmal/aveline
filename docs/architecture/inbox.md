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

### 5.2 The inbox row (list-tile contract)

The list endpoint (`GET /orgs/{orgId}/conversations`) returns a tile per thread that is rich
enough to draw a row without a second request: the client's name, the newest message's
preview, **the block the preview came from**, who spoke last, the persona key, and the
actionable `markers` set. The derivation lives in one server-side place
(`ConversationTileMapper`), so a tile that arrives over the realtime hub is the same tile a
re-read returns.

**The row's category is a content block, not the message kind.** Agent output is published
as `kind: Note` for every persona (`agnet-service/app/events/message_publisher.py`), so
`lastMessageBlock` is what the row switches on. The preview is derived per block type -
notably `client_message -> its text`, because an inbound message is a block whose *type* is
`client_message`, not `text`. A rule that looked for a block of type `text` would render the
customer's own words as `No messages yet`.

**Markers** are a derived set over the closed vocabulary `approval | choice | draft`, sorted
by fixed priority `approval` -> `choice` -> `draft`:

| Marker | Derived from | Producer |
|---|---|---|
| `approval` | the conversation holds a message with `Kind == SignOff` and `Status == AwaitingSignOff` | the commerce approval flow (ADR-018); the write path is complete, the producer is deferred |
| `choice` | the newest message carries a `choice` block (Aveline's clarification) | reachable today |
| `draft` | the newest message carries a `suggestion` block (Ava's `draft_response`, Elle's) | reachable today |

**Customer context is a separate axis from `kind`.** `ConversationKind` keeps its declared
meaning (the thread's nature and audience); the customer a thread carries rides
`CustomerId`. Clients classify by that context first (`customerId`, then `ExternalRef`), so
exactly one thread per caller is the general concierge and a channel thread whose customer
is not yet identified is rendered rather than pinned. The inbound path binds the context at
creation through the existing phone lookup (`ICustomerRepository.GetByPhoneAsync`); see §6.2.

**The list is paged and the ordering is total.** `page`/`pageSize` are clamped, page one
holds the newest threads, and the order is `OrderByDescending(LastMessageAt ?? CreatedAt)`
with `.ThenByDescending(Id)`, so a page boundary cannot duplicate or skip a row. The client
loads further pages on demand and states how much of the inbox is on screen; search narrows
only what is loaded, and the copy says so.

---

## 6. Flows

### 6.1 Staff triggers a query
Staff opens the Salon → sends a `Note`. API calls the agent service (`/agents/query` or
`/agents/query/stream`, `agnet-service/app/api/agents.py`) with the conversation's
`threadId`. Agents reply with attributed messages; streaming tokens are bridged to the
client so the reply types in live. `WorkflowRunId`/`TraceId` are recorded for audit.

### 6.2 WhatsApp inbound
Webhook (`/api/v1/webhooks/whatsapp/{orgId}`, ADR-015) → persist `InboundMessageLog` → the
API resolves the sender's phone against the customer book
(`ICustomerRepository.GetByPhoneAsync`) and creates a `Message` of kind `ClientMessage` in
that customer's Salon, binding `CustomerId` at thread creation (so staff see it immediately
and the thread carries the context it exists for) → the API then asks the agent service to
draft a response into the same thread (`/agents/query` with the conversation's `threadId`
and the client's phone in `org_context`), so the memory agent can refine the identification,
and publishes `message.received` (carrying any `attachmentId`) → a phone that is not on file
yields a thread with `ExternalRef` set and `CustomerId` null - the rendered "not yet
identified" state, resolvable through `select-customer` (§5.1.1).

**Media is no longer dropped (D8).** `ExtractMessage` used to select only
`type == "text"`, so a customer's photo produced `{status: "ignored"}` and nothing in the
thread. It now reads the message whatever its type and returns a media descriptor
(`{id, mime_type, sha256, caption?}`); the caption is the client's own words. Before the
message is recorded, the API fetches the bytes with the tenant's credentials through
`IWhatsAppService.GetMediaAsync` (Meta's two-step resolve-then-download, the bearer on both
hops) and stores them through `IAttachmentStore`, which the message then binds; the
`client_message` block keeps the caption and an `attachment` block is appended. A missing
integration, an expired media URL, a failed download or a type outside the allow-list
(images and PDF; audio and video are refused) is logged and skipped - the caption is still
recorded and the webhook still answers `200`, because Meta's retry would fix none of them.
Every step is best-effort and never delays the webhook `200`.

### 6.3 Human-in-the-loop sign-off
Commerce agent issues an interrupt (`pause_for_approval`, `agnet-service/app/agents/commerce/README.md`)
→ agent emits a `SignOff` content event → API persists a `Message` of kind `SignOff` and
stages it: the message is written `AwaitingSignOff` and the conversation is set
`AwaitingSignOff`, which is what lights the row's `approval` marker and makes the thread's
decision affordance appear → staff approve or reject inline; the API records the decision
(bound to a content hash) and updates the message/conversation status (`Published`/`Cancelled`
and `Active`/`Resolved`).

> **Deferred (ADR-018):** resuming the paused LangGraph workflow via `threadId`, writing
> `ApprovalQueueEntry` (`threadId` + `conversationId`), and dispatching an `ApprovalNeeded`
> notification deep-linking to the message all land with the Commerce approval flow, which
> must add a real `pause_for_approval` interrupt first. No fake resume is used today.

### 6.4 Notification deep-link
`Notification.Data` = `{ "conversationId": "…", "messageId": "…" }`. Tapping a
notification opens `/conversations/thread/{conversationId}` (with `?messageId=` when one
was named), which reads the thread's row through `ConversationRepository.fetchConversation`
and then opens it **on the page that holds that message** rather than on the newest words:
the request carries `around`, the server serves the page containing the anchor and echoes
the served page, so earlier history stays reachable through the ordinary "load earlier"
control. The ids ride the payload rather than the route, because the router re-parses its
location whenever the auth or profile listenable fires and `extra` does not survive that. A
notification that addresses only a client (`customerId`) still opens the client book.

> **Shipped.** The deep link is implemented client-side for every kind that carries ids:
> the pure rule `notificationRouteForIds` (`lib/core/notifications/notification_route.dart`)
> is shared by the inbox tile and a push tap, so both open the same place. `NewMessage`
> carries `conversationId` (+ `messageId`) and opens the thread; `NewMatch` and
> `EventReminder` carry `customerId` and open the client book; `IntegrationExpired` and
> `SystemAlert` carry no deep-link target and open nothing (the tile simply unfolds). A
> push tap additionally marks **that notification** read (`PATCH /api/v1/notifications/{id}/read`)
> and never the conversation or the message — the thread owns its own read state.

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
- **Inbox tiles (`ReceiveConversationChanged`)**: after an agent message is applied, and from
  the API's own creation/change sites (the WhatsApp webhook, `POST /conversations`,
  `select-customer`, a staff message), the API broadcasts the conversation's tile so an open
  inbox updates without a re-list. The payload is exactly the list's `ConversationDto`,
  derived by the same `ConversationTileMapper`. A brand-new thread needs this: `message.created`
  carries a message for a conversation the client may never have seen, and
  `conversation.created` has no publisher.
- **Routing is a correctness rule.** The tile goes to `org:{organizationId}` for an
  organization-shared thread (`OwnerUserId == null`) and to `user:{ownerUserId}` for a
  per-user general Salon. The org group is joined by every active member on connect, so
  sending a private Salon's tile there would leak its existence to colleagues.
- **One connection per screen.** The Salon and the inbox each construct their own
  `ConversationRealtimeService` and disconnect on dispose, so neither steals the other's
  connection; the inbox's connect joins no Salon.
- **The notification hub re-joins on reconnect.** SignalR does not preserve group
  membership across a rebuilt socket, so `NotificationHub.SubscribeAsync`
  (`Modules/Notifications/Hubs/NotificationHub.cs`) is an idempotent, client-callable
  re-join of `user:{id}` and the active `org:{id}` groups. Both clients invoke it from
  their `onreconnected` handler; a client that never re-joins looks connected and
  receives nothing.

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
