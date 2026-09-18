# ADR-018: Realtime Conversation Delivery Model

## Status
Accepted

## Context

The Salon (ADR-016) delivers agent output to staff in real time over SignalR. As the
end-to-end workflow was finalised (Issues #150-#153), two delivery questions remained open
that affect what the team should build next and what behaviour staff can rely on today:

1. **How does content reach the thread?** Two options exist:
   - **Batched**: the agent service publishes a complete `message.created` content event
     when a step finishes; the API persists the whole `Message` and broadcasts it. The
     client already animates progress via the `agent.status` lifecycle (`thinking`,
     `searching`, `processing`, ...) so the reply still feels alive, then the full message
     types in at once.
   - **Token streaming**: the agent streams individual tokens (`/agents/query/stream`) →
     Redis → API → SignalR, so text appears word by word as it is generated.
   Today the agent publishes batched `message.created` events and also emits
   `agent.status`; the SSE endpoint (`/agents/query/stream`) only relays raw event kinds and
   is not consumed by the API.

2. **How does a human-in-the-loop SignOff resume?** The Commerce agent should pause for
   approval and resume the paused LangGraph workflow via the conversation `thread_id`
   (ADR-002 checkpointer). `ConversationService.DecideSignOffAsync` records the decision and
   updates the message/conversation status, but the actual LangGraph resume is not wired.

## Options Considered

### Content delivery
1. **Batched cards + lifecycle states (chosen).** One `message.created` per persona step;
   `agent.status` drives the thinking indicator. Cheapest end-to-end, already works after
   Issues #150/#153, and needs no streaming bridge or new bus event types.
2. **Token-level streaming bridge.** Builds an SSE→Redis→SignalR relay. Closer to a true
   typewriter but adds a persistent stream, ordering/dedup complexity, and more bus traffic
   for marginal UX gain at this stage.

### SignOff resume
1. **Defer resume (chosen).** Record the decision and update status now (done); leave the
   actual LangGraph resume to the commerce approval slice, which must implement a real
   `pause_for_approval` interrupt before it can be resumed. Do not fake an interrupt.
2. **Implement resume now.** Requires the Commerce agent graph to pause at an interrupt
   point, which does not exist yet (Slice 3). Out of order for this workflow finalisation.

## Decision

1. **Batched content delivery.** The realtime conversation workflow uses batched
   `message.created` content events plus the `agent.status` lifecycle for perceived
   responsiveness. The token-level streaming bridge is future work and is not required for a
   working end-to-end flow. `/agents/query/stream` remains an unimplemented placeholder.
2. **SignOff resume is deferred.** `DecideSignOffAsync` records the decision (bound to a
   content hash) and updates message/conversation status. It does **not** resume LangGraph
   and does not write an `ApprovalQueueEntry`. The resume path is explicitly logged as
   deferred and will be implemented with the Commerce human-in-the-loop flow (which must add
   a real `pause_for_approval` interrupt).
3. **Real specialist sub-graphs are deferred.** Issues #150/#151 wired the block builders
   and structured stub output for Elle (visual) and Lina (commerce); their real `graph.py`
   sub-graphs remain the responsibility of Slice 2 and Slice 3 owners.

## Consequences

- The Salon delivers complete, rich messages reliably today without a streaming bridge.
- The `sign_off` card is never emitted by the generic persona publisher; a SignOff remains a
  first-class `kind == SignOff` message created by the commerce approval flow.
- A reviewer reading `DecideSignOffAsync` sees an explicit "resume deferred" note and cannot
  assume a decision resumes the workflow.
- When Slice 2/3 land real sub-graphs and a real interrupt, this ADR can be revisited to add
  token streaming and/or live SignOff resume as follow-ups.

## Related

- [ADR-016](ADR-016-conversation-inbox.md) - the Salon conversation inbox this delivery
  model serves.
- [ADR-014](ADR-014-redis-pubsub-event-bus.md) - the event bus carrying `message.created` and
  `agent.status`.
- [ADR-002](ADR-002-agent-framework.md) - LangGraph checkpointing behind the deferred resume.
