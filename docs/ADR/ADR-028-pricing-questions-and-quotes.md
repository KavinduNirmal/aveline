# ADR-028: Pricing Questions, Quotes, and the Discount Ceiling

## Status
Accepted — implemented in this change.

## Context

### The gap, as observed

A staff member asked the Salon, in a thread about a client:

> How much of a discount can we give this customer for +Champagne Rose satin midi dress?

One reply came back, and it was the memory agent's:

> Kasha Vivian Perera is a new customer. 2 notes are on file. *(table of both notes)*

Nothing answered the question. Nothing errored either — the run closed `Succeeded`.

The run was inspected rather than guessed at. `AgentWorkflowRuns` for that turn records
`AgentsInvolved = {commerce, customer_memory, orchestrator}`, and its twelve `AgentStepRuns` rows
contain no `visual_agent` step; `commerce_agent` is present and took **56 ms**. Measured directly
against the same message and organisation, Elle returns three pieces (including the named dress at
LKR 10,000) and Lina returns `skipped`, `"no items in order context to evaluate"`.

Three independent causes, in the order they are hit:

1. **The plan never included the piece.** `discount` is a pricing word, and the gate's keyword table
   is first-match-wins with pricing above item search, so the message is `pricing_query`. That intent
   routes `["memory", "commerce"]` — no visual agent. Elle is not broken; she was never dispatched.
2. **Commerce had nothing to price.** `OrderContextBuilder` resolves line items only from a purchase
   signal, deliberately, so that a question never becomes an order (ADR-024, invariant A1). A
   question has no purchase signal, so `items` was empty and `evaluate_deal` short-circuited before
   reading a single business rule.
3. **A skip was silence.** With no content from any specialist, the publisher's "one answer per
   message" rule left the memory agent's brief as the reply. A customer brief is content, but it is
   not an answer to *this* question.

The same hole is not specific to discounts. `pricing_query` has no producer for a question at all:
"How much is this dress?" runs the same three steps and ends in the same silence.

### What is already correct

- **The ceiling exists.** `get_customer_loyalty_tier` returns the tier and its `max_allowed_discount`;
  `validate_business_rules` returns `max_allowed_discount`, `min_required_margin` and
  `high_value_threshold`, and the API owns them. Nothing had to be invented to answer the question —
  only reached without a basket.
- **The rules are already authoritative.** `evaluate_deal` is the one place a discount is judged, and
  the quote below reuses its verdict rather than restating the rules.
- **The audience flag exists.** `staff_query` is already sent `true` on staff paths and `false` on the
  inbound customer path, and is already required to be explicitly `true` before the tenant-account
  lane opens (ADR-026). The same flag gates the quote, for the same reason: it quotes the house's own
  economics.

## Options Considered

### 1. Route the piece to Elle and stop there
Widen `pricing_query` to include visual whenever the message names a garment.
- **Pros:** smallest possible change; it is exactly the missing reply in the report.
- **Cons:** answers the *product* half only. Elle shows the dress and its price; nobody says what
  discount is possible on it, which is what was asked. Kept, but not as the whole answer.

### 2. Teach commerce to answer the policy question without a basket
When there is nothing to evaluate and the question is about a discount, answer from the tier cap and
the margin floor.
- **Pros:** no new wire contract; no line items needed, because a ceiling is a property of the
  customer and the house rules.
- **Cons:** the answer is generic. "Up to 5% comes off" is true of every piece the client might buy,
  so it does not answer the question *about the dress*.

### 3. A purpose on the order context, so a question may be priced and not bought (chosen)
The API resolves the pieces a pricing question names exactly as it resolves an order's, and marks the
context `quote`. Commerce prices it and is forbidden to pause or settle; the API refuses to create an
order from it.
- **Pros:** answers the actual question, per piece, from the catalog's real price and cost — and the
  invariant that a question never becomes a sale is enforced structurally rather than by the agent
  behaving.
- **Cons:** a new field on a contract that already carries money, and a second read-only terminal in
  the commerce graph to keep honest.

### 4. Let the agent resolve the pieces itself
Give commerce an inventory lookup and let it find the price and cost.
- **Pros:** no API change at all.
- **Cons:** rejected. The wholesale cost is not on the visual agent's piece model and is not meant to
  be: the agent would need a second, parallel path to the catalog's cost data, and the price the
  ceiling is computed from would then have two sources. The API already resolves items for orders
  with the catalog's real figures (ADR-024, invariant A1) — the quote uses that path.

## Decision

### Decision 1 — A pricing question that names a piece also dispatches the visual agent

`agents_for(intent_type, message)` applies one refinement over `_AGENT_ROUTING`: for
`pricing_query`, a message that names a garment inserts `visual` into the plan
(`["memory", "visual", "commerce"]`).

The intent stays `pricing_query`. A price question *is* a price question, and reordering the keyword
table so item search won would mislabel every one of them; what was missing is that a price is only
computable from the piece, and the piece is Elle's to look up. The refinement is defined once and
used by both the rule path and the supervisor's fallback, so a plan cannot differ by which one
produced it.

### Decision 2 — Commerce answers a discount question with no basket from the tier and the rules

When `evaluate_deal` skips and the message asks what a discount may be, a new read-only terminal
(`explain_discount_ceiling`) answers from `get_customer_loyalty_tier` and the house rules.

The rules call is made with `order_total=0.0, margin=1.0, requested_discount=0.0`, which cannot
trigger any rule, so *reading* the policy cannot trip it. The reply is built by code, not by a model:
a ceiling is a policy number, and an invented one would be a promise about money.

### Decision 3 — A quote is priced but never committed

`org_context.purpose` is `"order"` or `"quote"`, always sent. A quote:

- resolves the same line items as an order, with the catalog's price and cost;
- is evaluated by the same rules — the verdict is what the sentence reports;
- **never pauses**, so `needs_approval` is false and no approval row can exist;
- **never settles**, so no payment link and no courier booking are produced;
- is **refused by the API** if one somehow arrives on a pause: `CreateForPausedRunAsync` rejects a
  quote context before it reads anything else (invariant A7).

The distinction is carried on the wire rather than inferred, so a missing `purpose` reads as the
conservative `"order"` and reproduces the pre-ADR-028 behaviour exactly.

### Decision 4 — The quote is staff-only

The quote arm requires `staff_query is True`, exactly and explicitly, as the tenant-account lane does
(ADR-026). It states the house's margin position on a piece, which is not a customer's to see: a
customer asking what a dress costs is answered by Elle's piece block, which carries the price.

For the same reason the quote states the *discount* it can give and not the margin it computed —
"the 25% floor leaves no whole-percent room on it" rather than the piece's actual margin. The Salon
is used by roles that do not hold `pricing:view`, and the floor's consequence is the answer to the
question while the piece's cost is not.

### Decision 5 — A ceiling is quoted in whole percent, rounded down

Discount ceilings are rounded down to whole percent before they are stated. Two reasons, the second
found by the property test beside the function:

- a ceiling stated to sixteen decimal places is not a number anyone can act on; and
- the exact algebraic bound sits one ulp *inside* the margin floor. Quoted verbatim it claimed
  73.33333333333334% off a LKR 500 piece whose margin then computed to 24.999999999999983% — a ceiling
  that disagrees with the very check it exists to describe.

Rounding the *claim* down is what makes "comes off without sign-off" true. The property test asserts
the ceiling holds and that one whole percent more does not, for several price/cost pairs, so the two
arithmetics cannot drift apart again.

## Consequences

### Invariants

- **A1 (ADR-024) is unchanged in substance and stronger in form.** A question still never becomes an
  order. It is now expressed as a purpose rather than as an absence of line items, which is what let
  the question be answered at all.
- **A7 (new).** A run carrying a `quote` context never pauses, never settles, and never creates an
  order. Enforced in the commerce graph's routing and again in `ConversationOrderBridge`.
- **A5 (ADR-024) still holds.** The agent writes nothing; it returns a sentence, and the API owns
  every row.

### What this costs

- One catalog read on a pricing question that the old code refused to make. It is the same query the
  visual agent runs on the same message, and it is bounded (`CatalogScanLimit`, `MaxItems`).
- The pricing lane now runs three specialists instead of two when a piece is named, so a price
  question costs one more sub-graph.
- A price question where nothing resolves to a specific piece still produces no items, and the quote
  degrades to the ceiling answer — which is the honest outcome, not a failure.

### What is deliberately not done

- **No price negotiation.** The ceiling is what the rules allow, not a suggested counter-offer. The
  probe in `_quote_sentence` reports; it does not propose.
- **No LLM in the quote.** `_compose_narrative` handles three order scenarios and the quote does not
  join them: its numbers are arithmetic about money and must be reproducible offline.
- **No customer-facing quote.** A customer is shown the price and never the policy.
- **No multi-intent vocabulary.** `IntentType` is still single-valued, and this ADR adds a per-message
  refinement rather than a second intent. A message that is genuinely two requests at once remains a
  known limitation of the table.

## Related

- ADR-024 — conversation-initiated orders and the HITL approval loop (invariants A1–A6).
- ADR-026 — tenant account awareness (the audience flag this reuses).
- ADR-023 — the supervisor, which routes the plan this refines.
- `app/gate.py`, `app/agents/commerce/{graph,nodes,state}.py`,
  `Aveline.Api/Modules/Commerce/Services/OrderContextBuilder.cs`,
  `Aveline.Api/Modules/Commerce/Services/ConversationOrderBridge.cs`.
