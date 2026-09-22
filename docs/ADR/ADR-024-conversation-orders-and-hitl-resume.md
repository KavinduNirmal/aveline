# ADR-024: Conversation-Initiated Orders and the HITL Approval Loop

## Status
Proposed

## Context

### The gap, as observed

A customer messaged "I want to buy the emerald green saree, please send the order". Nothing paused
for approval, and no order was created. The piece is **LKR 75,000 with a LKR 65,000 cost** — a
13.3% margin. That breaches two business rules at once (high-value > LKR 40,000, and margin < 25%),
so the workflow should have stopped for sign-off twice over.

Reproduced against the running agent with the exact org context the conversation path sends:

```
intent         : order_placement
commerce ran   : True
commerce status: skipped
reason         : no items in order context to evaluate
needs_approval : None
```

The commerce agent is wired and executes. It simply has nothing to evaluate, and even if it did
there is nowhere for the pause to land. Three independent gaps, in the order they are hit:

1. **No order context.** `evaluate_deal` computes subtotal, discount and margin from
   `org_context.items`. The inbound conversation payload carries `organization_id`,
   `conversation_id`, `customer_id`, `phone_number`, `channel`, `direction`, `attachments` and
   `image_url` — and no items. The commerce agent therefore short-circuits before it looks at a
   single business rule. Nothing converts "I want to buy X" — or the piece Elle matched — into
   line items.

2. **No Order to approve.** `ApprovalQueueEntry.OrderId` is `[Required]` with a foreign key to
   `Order`, and the only thing that creates an `Order` is the staff orders API. The agent never
   creates one. So an agent-driven pause has no row to hang an approval on, and the Approval Queue
   stays empty however loudly the agent asks for sign-off.

3. **The existing resume does not work.** `ApprovalService.ProcessDecisionAsync` fires a
   `POST /agents/query` with `approval_decision` and `order_id`. That was measured, and it fails in
   three ways at once:
   - it sends **no items**, so `evaluate_deal` returns `skipped` before reading the decision;
   - the decision vocabulary does not match: the API sends `approve`/`reject`/`revise`, the graph
     matches `approved`/`rejected`;
   - the re-query text is `"[Human Approval Decision: approve]"`, which carries no commerce signal,
     so the supervisor routes to memory only and **commerce never runs**.

   Even with items supplied and the graph's own vocabulary, the resume produced no commerce output,
   because routing never selected it.

### What is already correct

- **The pause itself works.** A rule breach returns `pending_approval` with `needs_approval`,
  `approval_type`, `approval_reason`, `triggered_rules` and no payment link. Verified for all three
  rule types (high value, low margin, discount cap) via `scripts/hitl-scenario.py`.
- **The run is recorded as paused.** `pending_approval` maps to `AgentRunStatus.PausedForApproval`,
  and `FocusFeedService` surfaces paused runs to the owner.
- **The order-created path is complete.** Creating an order that breaches rules already writes an
  `ApprovalQueueEntry` (with `ThreadId` and `ConversationId`) and the Approvals API already
  authorises decisions per verb. This ADR extends that path to conversation-originated orders
  rather than inventing a second one.
- **The checkpoint infrastructure exists.** ADR-002 chose LangGraph with a Postgres checkpointer
  precisely so a workflow can pause and resume, and `create_checkpointer` is implemented.

## Options Considered

### 1. The API drafts and creates the order; the agent supplies context (chosen)
The conversation path derives candidate line items and sends them as `org_context.items`. The
commerce agent evaluates them and, when it pauses, the **API** creates the `Order` and the
`ApprovalQueueEntry`. Approval resumes the graph through a real checkpoint resume.

- **Pros:** keeps business rules and persistence in the API, which is the stated architecture
  (`SYSTEM_PROMPT.md`: the agent is a reasoning engine and never owns business logic); reuses the
  approval queue, permissions and order lifecycle that already work; makes the pause actionable.
- **Cons:** requires an intent-to-line-items step whose accuracy is the hard part; two new
  responsibilities (order drafting, pause-time creation) to place carefully.

### 2. The agent creates the order itself
Give the commerce agent an order-creation tool and let it write the order before pausing.
- **Pros:** fewest moving parts; one round trip.
- **Cons:** rejected. It moves pricing, validation and persistence into the agent, contradicting the
  "tools only, never the database" rule the whole service is built on. It also makes the agent the
  authority on what a valid order is, which is exactly the kind of business decision an LLM should
  not own.

### 3. Keep the re-query as the resume mechanism
Repair the existing `POST /agents/query` resume: send items, align the vocabulary, force commerce.
- **Pros:** smallest change; the endpoint and the decision flow already exist.
- **Cons:** a re-query is a **re-run, not a resume**. Every side effect before the pause executes
  again — the second evaluation, the sourcing request, the memory writes — unless each is separately
  guarded for idempotency. The checkpoint is never used, so the guarantee ADR-002 was chosen for is
  not actually provided. Acceptable only as a stopgap, and a stopgap that has to be un-taught.

### 4. Ask the customer to confirm in chat instead of an approval queue
Treat the pause as a conversational question rather than an owner decision.
- **Pros:** no queue, no order lifecycle work.
- **Cons:** answers the wrong question. These rules exist to protect the boutique's **margin**, which
  is not the customer's decision to make. Rejected.

## Decision

### Decision 1 — Order context is derived on the conversation path, and the agent never invents it

`org_context.items` is populated by the API before the agent is called, from explicit customer
intent resolved against inventory: the piece(s) the customer named, with unit price, quantity and
wholesale cost. An item that cannot be resolved to inventory reference data is **omitted**, not
guessed — a fabricated price or cost would silently corrupt the very margin check the rules exist
to enforce.

A message that names no resolvable item continues to produce no line items, and the commerce agent
continues to skip. That is the correct outcome and must stay distinguishable from "evaluated and
approved": `skipped` is not `is_auto_approved`.

### Decision 2 — The API creates the Order when a run pauses, not before

On a `pending_approval` result the API creates the `Order` (status `pending_approval`) and then the
`ApprovalQueueEntry` referencing it, carrying `ThreadId` and `ConversationId`.

Creating the order only at the pause keeps the order's existence tied to a decision that actually
needs one, and avoids writing speculative orders for every enquiry. The API already derives the
initial status from the same business-rules service the agent calls, so the two cannot disagree
about whether approval was required.

### Decision 3 — Resume is a real checkpoint resume, not a re-query

Approval resumes the paused graph via the LangGraph checkpointer (`Command(resume=...)`), keyed by
the conversation's `thread_id`, rather than replaying the request.

Two consequences that must be honoured:

- **The resume must be idempotent.** The order status transition, the approval row and any message
  emission stay guarded, so a duplicated decision cannot produce a second order.
- **The resume must not re-route.** A resume enters at the paused node with its decision; it does
  not go back through the supervisor, whose plan was made for the *original* message. Routing a
  decision through the supervisor is what made the re-query silently skip commerce.

The decision vocabulary is fixed as one published set, shared by the API and the graph, with the
graph's expectations as the source of truth. The current `approve` vs `approved` mismatch is the
concrete instance of a class of bug that only appears at the seam; a shared constant removes it.

### Decision 4 — A pause is actionable only if it is linked

An approval row is reachable only when it names a conversation and a thread. `ThreadId` and
`ConversationId` are therefore required on the entry (`ThreadId` is nullable today and the resume
silently skips when it is absent), and the agent run's `ConversationId`/`CustomerId` linkage — added
in ADR-023's follow-up — is what lets the owner see *which* conversation paused.

### Decision 5 — The agent's authority is unchanged

The agent may compute a proposal and report that approval is required. It may not create, price,
discount or confirm an order. Every write goes through the API, consistent with ADR-023's Decision 3
and the routing-only boundary.

## Consequences

- **Order drafting accuracy becomes the critical path.** Everything here depends on turning a
  message into correct line items. A missed item understates the total and can let a deal past the
  threshold; a wrong price does the same. This is the part to test hardest, and the reason Decision 1
  forbids guessing.
- **A new write path exists on the agent turn.** Pause-time order creation is a side effect of a run
  that may be retried. It must be idempotent on `(OrganizationId, ThreadId)`.
- **The re-query shortcut must be removed, not left in place.** Two resume mechanisms would be worse
  than one; the re-query path should be deleted once the checkpoint resume lands.
- **Pauses become visible and completable**, which is the user-facing point: today the owner sees a
  focus-feed item and has nothing to act on.
- **`ApprovalQueueEntry.ThreadId` becoming required is a schema change** and needs a migration plus
  a backfill decision for existing rows.
- **Conversation-originated orders inherit order semantics** — status transitions, cancellation and
  the existing permission split between who may approve and who may reject.

## Out of Scope

- **True token streaming of the pause** (ADR-018 defers it).
- **Multi-item bargaining** — negotiating a basket down to a margin threshold across turns.
- **Automatic margin optimisation** — suggesting a discount that clears the threshold.
- **The product-help handbook lane**, still its own ADR.
- **Re-pricing historical orders** when a rule changes.

## Related
- `ADR-002` — LangGraph and the checkpointer this relies on for a real resume
- `ADR-023` — the supervisor, the conversation context window, and the routing-only boundary
- `ADR-016` / `ADR-018` — the Salon thread the pause is surfaced in
- `ADR-010` — usage and Blossom accounting for the run that pauses
- Scenario: `scripts/hitl-scenario.py` — triggers the pause for each rule today
