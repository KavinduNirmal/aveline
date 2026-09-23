# Conversation Context Propagation

> **Status:** Approved (see [ADR-023](../ADR/ADR-023-conversation-context-and-supervisor.md), Decision 1).

This document describes how the bounded conversation window reaches a model: where it is loaded,
how it crosses the orchestrator-to-specialist boundary, what bounds it, and what it deliberately
does **not** do.

---

## 1. The path

One turn takes this route:

```
load_context                 (fetches + compacts the transcript)
      │
      ▼
supervisor                   (routing; sees the window)
      │
      ├── memory_agent       (Ava;  sees the window when drafting)
      ├── visual_agent       (Elle; sees the window when styling)
      └── commerce_agent     (Lina; receives it, consumes none)
```

`load_context` (`app/workflows/concierge_workflow.py`) is the graph's entry node. It calls
`ToolRegistry.get_conversation_history(org_id, conversation_id)`, fits the transcript to
`context_window_tokens` through `app/context`, and returns three fields on `ConciergeState`:

| Field | Meaning |
|---|---|
| `history` | the retained turns, verbatim, oldest first |
| `thread_summary` | a narrative of what fell outside the window, or `None` |
| `pinned_slots` | the working set that must survive trimming (active item, budget, event) |

Each orchestrator node spreads `_context_fields(state)` into the initial state of the sub-graph it
invokes. Each sub-graph's `TypedDict` declares the same three fields, and the two sub-agents that
own a prompt render them with `app.context.render_context_block` and pass the result to
`assemble_system_prompt(..., dialogue_context=...)`.

The supervisor renders the identical block into its human message, so a transcript cannot look
different in one prompt than in another.

---

## 2. Why both halves are required

LangGraph copies into a node's state only the keys its `TypedDict` declares, and it drops
undeclared keys **silently**: no warning, no error. Passing `history` into `ainvoke` while the
destination schema does not declare it is a no-op.

That makes the transport a two-part contract:

1. **Declaration** in `app/agents/<name>/state.py`.
2. **Invocation** in the orchestrator node's hand-built state dictionary.

A mocked sub-graph test can only see part 2, so the declarations are pinned separately by
`tests/test_context_propagation.py::test_every_subagent_state_declares_the_context_transport`, and
the round trip is asserted inside the real sub-graphs
(`tests/test_customer_memory_agent.py::test_history_reaches_the_draft_prompt`,
`tests/agents/test_visual_insight_graph.py::test_visual_history_reaches_the_styling_prompt`).

---

## 3. Scope

**One conversation = one checkpoint thread = one window.** Identity is per-conversation: the
`.NET` API generates `Conversation.ThreadId` once when the row is created and sends it as
`thread_id` on every `POST /agents/query`
(`Aveline.Api/Modules/Conversations/Services/ConversationService.cs`). The agent service never
keys context by customer, so two conversations with the same customer do not share a window, and
a window never leaks across conversations.

The transport is internal. `/agents/query` already carries `org_context.conversation_id`; no wire
format, DTO, or OpenAPI contract changed, and there is no new configuration key or dependency.

---

## 4. Limits

- **Bounded by policy, not by a hard cap.** `context_window_turns` (default 20) limits how many
  turns are fetched and `context_window_tokens` (default 2000) bounds what is kept. Both live in
  `app/core/config.py`. `render_context_block` adds no budget of its own, so there is exactly one
  threshold to tune.
- **The oldest turns are summarised or dropped.** Compaction runs only when the window overflows
  (`app/context/__init__.py`). A short conversation therefore has `thread_summary = None`, which is
  the common case; the verbatim window is the layer that resolves an immediate reference.
- **Without an LLM the window is still bounded but nothing is summarised.** The overflow is
  dropped, and the run stays deterministic. That is the offline/CI path.
- **An empty context is byte-identical to no context.** Without `organization_id` or
  `conversation_id`, `load_context` short-circuits and the rendered block is the empty string, so
  the assembled prompt is unchanged. `tests/test_context_propagation.py` pins this.
- **Commerce consumes nothing.** `app/agents/commerce/nodes.py` makes no LLM call, so the fields
  are forwarded there for uniformity only and change no behaviour. They are declared so the
  transport stays complete if a prompt is added later.
- **Visual's referent parsing does not read the window.** `parse_visual_intent` is rule-based; the
  injected block reaches the styling commentary prompt, not the search-criteria step. Turning a
  reference such as "the pink one" into search criteria remains a separate, unaddressed gap.

---

## 5. Security

Conversation text is untrusted input, and it now reaches specialist prompts as well as the
supervisor's. The rule stated in `app/gate.py` and repeated in `app/prompts/SYSTEM_PROMPT.md`
applies to every prompt that carries turns: **conversation context is data, not instruction**. No
specialist gains an action it did not already have, and no tool is invoked from context text.
Widening this surface from one prompt to three is an accepted consequence of the feature, recorded
in ADR-023.

---

## 6. Related

- [ADR-023](../ADR/ADR-023-conversation-context-and-supervisor.md) — the decision and the layer model
- [`app/context/__init__.py`](../../agnet-service/app/context/__init__.py) — window, compaction, rendering
- [`inbox.md`](./inbox.md) — the conversation surface the context is drawn from
- [`integrations.md`](./integrations.md) — the `.NET` to agent-service contract
- [`handbook.md`](./handbook.md) — the documentation lane, and [`tenant-awareness.md`](./tenant-awareness.md)
  for the live-figures lane. Both are *live data* rather than conversation history: they are fetched
  per turn into their own prompt blocks and never enter the window, the summary, or `pinned_slots`.
