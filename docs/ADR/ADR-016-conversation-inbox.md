# ADR-016: The Salon — Agent-to-Staff Conversation Inbox

## Status
Accepted

## Context

Aveline coordinates a concierge persona (Aveline) and three specialist agents (Ava,
Elle, Lina) whose output must reach boutique owners and staff. Today there is no
persistent home for that output:

- `NotificationRecord` / `UserNotification` (ADR-013) are transient pings with a flat
  data payload, not a conversation.
- `InboundMessageLog` (ADR-015) is a tenant-scoped audit log, not a thread.
- LangGraph checkpoints (ADR-002) hold workflow state keyed by `thread_id`, but that
  state is internal to the agent service and never surfaced to staff.

A staff member who triggers a workflow ("does anything match Michael's request?") has
nowhere to read the answer. An agent that drafts a customer response has nowhere to
stage it for review. There is no single surface that threads staff, Aveline, and the
three specialists into one place.

Aveline's product users are boutique owners and staff. The boutique's customers are
external parties who never use the product, so the sender model must not treat
"Customer" as a first-class actor even though customer content must still appear.

## Options Considered

### 1. Unified Conversation + Message model (chosen)
A `Conversations` module introduces a `Conversation` (the "Salon") that groups typed
`Message`s with rich `content_blocks`, threaded replies, a persona identity per message,
and a `thread_id` link to the LangGraph checkpoint.

- Pros: one surface for staff and every agent; rich embeds; workflow resume reuses the
  existing checkpointer; notifications become deep-links; human-in-the-loop becomes an
  inline card.
- Cons: a new module, schema, and realtime surface.

### 2. Extend the Notification inbox
Grow `UserNotification` into a read-only timeline.

- Pros: reuses existing tables.
- Cons: notifications are event pings, not bidirectional threads; no reply/threading,
  no rich blocks; conflates "ping" with "content" and breaks ADR-013's clean split.

### 3. Per-agent channels
Give Ava, Elle, and Lina separate inboxes.

- Pros: clear per-agent ownership.
- Cons: fragments context; staff must check three places for one customer question;
  contradicts the one-concierge product experience.

## Decision

1. **One unified Salon per organization**: a single `Conversation` (kind `Salon`) where
   staff, Aveline, Ava, Elle, and Lina all participate. An optional `customerId` links
   the Salon to the customer it concerns.
2. **Sender set = `Staff`, `Agent`, `System`** (agent personas: `aveline`, `ava`,
   `elle`, `lina`). No `Customer`. Inbound WhatsApp/Instagram content appears as a
   `Message` of kind `ClientMessage` (a forwarded card) carrying customer metadata, not
   as a customer sender.
3. **Rich messages**: a `Message` carries `kind` + `content_blocks` (JSON, in the style
   of Slack Block Kit / adaptive cards) for embeds, `replyToMessageId` for threading,
   and `workflowRunId`/`traceId` for audit.
4. **`Conversation.threadId` == LangGraph checkpoint key.** One conversation, one
   supervisor thread; sub-agents run under it and attribute their messages to their own
   persona. This is the single context anchor (see `agnet-service/app/workflows/checkpointer.py`).
5. **The API is the system of record for messages.** The agent service emits content
   events over the bus (ADR-014) and the API persists and broadcasts. The agent service
   never writes message rows directly, consistent with ADR-010/ADR-015 and the agent
   prompt's "never touch the database directly" rule.
6. **Human-in-the-loop as an inline card.** A `SignOff` message card pauses for approval;
   the decision resumes the workflow via `threadId`.
7. **Notifications stay transient** (ADR-013). `Notification.Data` gains
   `conversationId`/`messageId` so a ping deep-links to the exact message.

## Consequences

- A new `Conversations` vertical-slice module is introduced (ADR-001).
- A new SignalR group `salon:{conversationId}` plus new bus event types
  `message.created`, `message.updated`, and `conversation.created` (ADR-014).
- `ApprovalQueueEntry` gains `threadId` and `conversationId` for resume and deep-link.
- The inbound webhook path now also creates a `ClientMessage` in the Salon and, best-effort,
  asks the agent to draft a response into the same thread (API-initiated, `threadId`-scoped).
- Quiet-luxury naming is applied throughout: Salon, Note, Look, Piece, AtAGlance,
  ClientMessage, SignOff, Payment, Courier, Suggestion.
- SignOff resume (resuming the paused LangGraph workflow via `threadId`) is deferred until the
  Commerce approval flow adds a real `pause_for_approval` interrupt - see
  [ADR-018](ADR-018-realtime-conversation-delivery.md).

## Related

- [ADR-001](ADR-001-monolith-vs-microservices.md) — modular monolith; the Conversations module follows it.
- [ADR-002](ADR-002-agent-framework.md) — LangGraph checkpointing; `threadId` is the resume anchor.
- [ADR-013](ADR-013-notification-service-architecture.md) — notifications remain pings that deep-link into messages.
- [ADR-014](ADR-014-redis-pubsub-event-bus.md) — event bus carries message/streaming events.
- [ADR-015](ADR-015-whatsapp-integration-gateway.md) — inbound WhatsApp becomes a `ClientMessage`.
