# ADR-023: Layered Conversation Context and the Supervisor LLM

## Status
Accepted

> Implementation note: the supervisor, the layered context window, and the transport of those
> layers into the specialist prompts (see [Consequences](#consequences)) are shipped. The
> `Kind=Local` telemetry defect recorded in the Context section remains an open prerequisite for
> routing evaluation.

## Context

### What the current design does

The concierge workflow (`agnet-service/app/workflows/concierge_workflow.py`) routes every
inbound message through a fixed pipeline:

```
intent_gate -> resolve_customer -> memory_agent -> visual_agent -> commerce_agent -> formulate_response
```

Two properties of that pipeline are load-bearing and, in combination, are what broke the
WhatsApp integration on 2026-09-22:

1. **`run_intent_gate` calls `classify_by_rules` directly** (`concierge_workflow.py:80`). The
   hybrid gate described in `app/gate.py` — deterministic rules first, an LLM classifier to
   refine ambiguous input — is never used. `infer_intent()` and its `llm_classifier` seam exist
   but have **no call site**.
2. **Routing is a hard gate.** `_route_after_resolve` sends *any* `ambiguous`/`not_found`
   customer resolution straight to `formulate_response`, and `formulate_response` discards all
   specialist output when that happens (`concierge_workflow.py:324-326`). The only thing
   rendered is a clarification block.

### The observed failure

A customer asked *"Hello there. Are there any pinkish gowns in your collection?"*. Aveline
replied *"I couldn't find a customer with that name. Could you share their phone number so I
can look them up?"*

Diagnosis (evidence retained in the session record):

| Observation | Evidence |
|---|---|
| Sender `94763475058` is not a customer | `POST /internal/customers/lookup` → `{"matches":[],"isExact":false,"total":0}` |
| Phone normalization is **not** at fault | `+947612345678` resolves correctly to an existing customer |
| No specialist ran | Agent log: one lookup, then `usage/record`; no `visual`/`commerce` node |
| Every unknown sender is affected | The clarification was also produced for `"this is a text message"` |
| It is the gate, not the agents | Same question with a *known* phone produced a full, correct draft response |
| Fashion vocabulary is unrouted | `classify_by_rules` returns `general_inquiry` for `gowns`/`pinkish`; routing is `["memory"]`, so `visual` is `null` |

### The deeper problem

Resolution treats an unknown sender as a **veto** rather than a **fact**. For an inbound
WhatsApp message the sender's own number arrives in `org_context.phone_number`
(`ConversationService.cs:844`), so "I don't know this customer" is the expected state for every
first-time contact. Asking them to supply the number they are already messaging from is
incoherent, and blocking the entire workflow on it means **no product question from a new
customer can ever be answered**.

The rule table cannot be patched indefinitely: `item_search` lists `dress, saree, blouse,
outfit, party, bluish, size, stock, inventory, item, photo, image, picture, matching` and will
always miss the next synonym (`gown`, `frock`, `jewellery`, `collection`, and most colour
words — note `bluish` is present while `pinkish` is not).

### The missing precondition: there is no conversation context

`/agents/query` receives only `query`, `thread_id`, and `org_context`. `ConciergeState` holds
`message: str` — not a message list. The tool registry exposes customer memories, events,
profile, and inventory, but **no conversation-history retrieval**. `ThreadId` is a stable
LangGraph checkpoint key per conversation (`Conversation.cs:31`), yet no node reads prior turns.

Consequently the agent answers each turn from the current message plus customer data alone. Any
supervisor built on this foundation would be routing **without the information needed to
disambiguate**: "yes, that one" is unresolvable without the preceding turn. This is why the
context layer is a prerequisite for the supervisor, not a parallel workstream.

### A second, independent defect

Every agent run fails to record telemetry: `POST /internal/agent-runs` returns 500 with
`Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone', only UTC
is supported`. Best-effort by design, so replies still succeed, but run telemetry is silently
lost for all conversations. This is recorded here because it affects the observability the
supervisor rollout depends on.

## Options Considered

### 1. Extend the keyword list (incremental patch)
Add `gown`, `frock`, `jewellery`, `collection`, and a colour vocabulary; special-case the
"unknown sender" path.
- **Pros:** hours of work; no new dependencies; fully deterministic.
- **Cons:** does not address the class of bug, only the instance. The next synonym, tense, or
  phrasing fails identically. Ambiguity ("which dress?") remains unhandled. Rejected as a
  stopgap at best.

### 2. Wire the dormant LLM classifier and fix the short-circuit
Call the already-written `infer_intent(..., llm_classifier=...)`, and stop letting `not_found`
veto the run.
- **Pros:** small, surgical; reuses existing code; preserves the rule-based fast path and the
  no-LLM fallback; fixes both observed bugs.
- **Cons:** the classifier sees only the current message, so it cannot resolve **referential**
  ambiguity ("yes, that one", "the second one", "still available?"). It is a classifier, not an
  orchestrator: it cannot decide *which* agents to run, in what order, or whether a customer
  lookup is even required. Good as a component; insufficient as the answer.

### 3. Supervisor LLM over a layered context model (chosen)
Replace rule-based routing with a supervisor node that runs once per turn, sees bounded
conversation history plus customer state, and emits a structured plan (intent, agents to run
and order, whether customer resolution is needed, whether to ask a clarifying question). A
deterministic driver executes that plan. Context is managed in explicit layers rather than one
window. Rules are retained as a cheap pre-filter and as the offline fallback.
- **Pros:** addresses the whole class; handles referential ambiguity because it can see history;
  makes "ask a clarifying question" a first-class outcome instead of a side effect of a veto;
  the dead `llm_classifier` seam becomes a component rather than an unused branch.
- **Cons:** adds an LLM call per turn to the hot path (cost + latency); introduces
  nondeterminism into routing; requires a context layer to exist first; needs an LLM-free
  fallback preserved for CI and offline development.

### 4. Semantic reply cache before the LLM
Key responses by message content to skip the model on repeats.
- **Pros:** reduces cost/latency for genuinely repeated questions.
- **Cons:** unsafe in conversation. The same string means different things at different points
  ("yes", "that one", "do you have it in blue?"), so a content-keyed cache will confidently
  return a wrong answer in context. Rejected. Note that **prompt-prefix caching is a different
  and useful mechanism** (it attacks cost, not context size) and is compatible with this ADR.

## Decision

Adopt **option 3**, in this order:

### Decision 1 — The context model is layered, and layers are not interchangeable

Conversation context is managed as four distinct layers with distinct retention policies:

| Layer | Contents | Retention | Mechanism |
|---|---|---|---|
| **Recent turns** | last *N* turns (user + agent), verbatim | bounded by token budget | window; what is proposed to be trimmed |
| **Thread summary** | rolling narrative of what left the window | bounded, replaced on compaction | LLM summarization on turn boundaries |
| **Durable facts** | preferences, events, constraints ("budget is 50k") | permanent | existing `CustomerMemory` + pgvector (ADR-017) |
| **Live data** | profile, inventory, stock, pricing | never retained in prompt | tool calls on demand |

Two consequences follow, and they are the substance of this decision:

- **Deletion is a UX decision on the model's *view*, not on data.** The full transcript remains
  in the API's `Messages` table and LangGraph checkpoints. Trimming the prompt is therefore
  recoverable, and a summary can always be regenerated from source.
- **Deleting the oldest messages is not sufficient, and can be harmful.** Naive FIFO eviction
  separates a question from its answer: drop *"which dress did you mean?"* while keeping *"yes,
  that one"* and the referent is destroyed. Therefore: turn-pair integrity (never split a
  question from its answer), compaction on turn boundaries, and a small set of **pinned slots**
  for the active working set (item under discussion, stated budget, event date) which do not age
  out while the thread depends on them. "Slots" are the right mechanism for the pinned working
  set, not for the whole window.

### Decision 2 — Budget is measured in tokens, never in message count

A turn carrying an image is not a turn of text. The window is governed by a token budget with a
hard ceiling. With a supervisor plus Elle/Lina/Ava, the dominant consumers are **tool results**
(catalog searches, image analyses, margin computations), not chat turns; tool output MUST be
distilled to conclusions before re-entering context. A tidy message window will not compensate
for a verbatim 4k-token inventory dump per turn.

### Decision 3 — The supervisor is the routing authority; rules are a fast path and a fallback

A single supervisor call per turn runs before any specialist and emits a strict structured
schema: `intent_type`, `agents` (ordered subset of `memory`/`visual`/`commerce`),
`needs_customer_resolution`, `clarification` (nullable), `reply` (nullable), `safety_flags`.
Three constraints:

- **The supervisor decides; a deterministic driver executes.** LangGraph edges stay
  deterministic and the driver iterates the returned agent list. Routing becomes data, not a
  graph rewrite, which keeps the workflow auditable and resumable under the existing checkpointer.
- **Rules remain.** A cheap pre-filter handles unambiguous cases without a model call, and the
  result degrades to the existing rule-based path when `agent_llm_enabled` is false or no key is
  configured. The current guarantee that "CI and local development stay green without an LLM"
  (`app/llm/runtime.py`) MUST be preserved.
- **The supervisor may answer a conversational message herself, but only when nobody else
  will.** `reply` carries a short customer-facing message (a greeting, small talk, a general
  question); it is rendered only if no specialist produced content, and a plan that routes
  `visual` or `commerce` never carries one. This replaces the earlier routing *summary* ("Treated
  this as a general inquiry."), which described the orchestrator rather than answering the person
  and was the only thing an unresolvable message ever showed. The bound is enforced in code
  (`app/gate._with_a_bounded_reply`) and in the publisher, not merely requested in the prompt. The
  offline path keeps a deterministic reply so a greeting is never silently dropped.

### Decision 4 — Clarification becomes a first-class outcome, not a veto

Customer resolution stops being a gate that discards work. `resolve_customer` records a fact
(`resolved` / `ambiguous` / `not_found` / `no_signal`); whether that fact *requires asking the
customer something* is the supervisor's decision, and when it does, the run produces a
clarification output **without** silently discarding any specialist output already produced.
`not_found` derived from the sender's own context must never ask for the sender's number.

### Decision 5 — The supervisor follows the existing prompt architecture

The supervisor is a prompt layer, not a new prompt system. It composes through the established
three-layer assembly in `app/prompts/assembly.py` (universal `SYSTEM_PROMPT.md` + agent-specific
layer + dynamic context), adding a `supervisor` entry to `AGENT_PROMPTS`. Handbook content is
**out of scope for this ADR** (see below).

## Consequences

- **Cost and latency move onto the hot path.** Every inbound message gains an LLM call. Mitigated
  by the rules pre-filter, by keeping the supervisor's own prompt small (it routes; it does not
  answer), and by prompt-prefix caching on a stable prefix. Cost must be measured, not assumed.
- **Routing becomes probabilistic.** Two identical messages may route differently across runs.
  Mitigated by strict structured output, a temperature-0 policy for the supervisor, and the
  deterministic driver. Deterministic edge behaviour is retained deliberately.
- **Observability becomes a dependency, not a nicety.** You cannot tune a supervisor you cannot
  see. The `Kind=Local` telemetry defect (above) must be fixed for routing evaluation to mean
  anything.
- **Referential ambiguity becomes addressable**, which is the main functional gain. It is also
  entirely dependent on Decision 1: without history, the supervisor cannot resolve "that one".
- **The rule-based path is retained indefinitely** as fallback and offline mode. This is
  deliberate duplication, and the two paths must be kept consistent by tests.
- **`ADR-002`'s routing description is amended**, not superseded: LangGraph, checkpointing, and
  HITL remain as decided; what changes is that routing authority moves from a static keyword
  table to a supervisor decision executed deterministically.
- **Two known defects are in scope for the implementation plan but not decided here**: the
  keyword gap is resolved by Decision 3 (the table stops being the authority), and the telemetry
  bug is a prerequisite fix.
- **Prompt injection becomes a real surface.** Once the supervisor consumes conversation text —
  and especially once it consumes handbook or retrieved content — untrusted input is in a
  privileged prompt. The supervisor's authority MUST be bounded to *routing and a reply*, never to
  executing actions or bypassing approval. No tool may be invoked directly from supervisor output,
  and the `reply` it writes is text only: it cannot approve, order, or call anything. Decision 3
  now makes the supervisor a producer of customer-facing text as well as a router, which raises the
  stakes on that bound without changing it.
- **The context layers now reach the specialist prompts, not only the supervisor's.** Decision 1's
  intent — a bounded window that makes referential ambiguity resolvable — is fulfilled for the
  specialists as well as for routing. This **widens the injection surface from one prompt to
  three**: the guard above now applies to `memory` and `visual` too, and
  `SYSTEM_PROMPT.md` states the data-not-instruction rule for every prompt that carries turns. The
  transport is a two-part contract (each sub-graph state must *declare* the fields; each
  orchestrator node must *pass* them), because LangGraph drops undeclared state keys silently. See
  [`docs/architecture/agent-context.md`](../architecture/agent-context.md) for the path, scope
  (one conversation = one thread = one window), and limits.
- **Two specialists consume context, and one does so only partially.** `commerce` makes no LLM
  call, so the fields it now receives are inert. `visual`'s referent parsing is rule-based, so the
  window reaches its styling commentary but not its search-criteria step. Neither is a behavioural
  gain, and neither is claimed as one.

## Out of Scope

- **Aveline customer-support handbook / product-help lane.** This was deferred here and is now
  decided: [ADR-025](ADR-025-handbook-knowledge-base.md) adopts grounded retrieval (a hybrid
  pgvector + full-text index) over a static prompt block, using the documentation corpus that now
  exists. The lane is delivered by the supervisor herself under the `aveline_help` intent, with no
  new subagent.
- Replacing LangGraph, the checkpointer, or the response envelope.
- Token-level streaming (`ADR-018` explicitly defers this).

## Related
- `ADR-002` — LangGraph agent framework (routing authority amended in part)
- `ADR-017` — Customer memory & pgvector embeddings (the permanent-memory layer reused here)
- `ADR-018` — Realtime conversation delivery (`agent.status` lifecycle the supervisor emits into)
- `ADR-019` — Entity mentions (`@name`/`#phone`; the staff-lookup path that legitimately clarifies)
- `ADR-016` — Conversation inbox and `threadId` linkage
- [`docs/architecture/agent-context.md`](../architecture/agent-context.md) — how the context layers propagate
- Implementation plan: `.agents/plans/supervisor-llm-and-conversation-context-implementation-plan.md`
