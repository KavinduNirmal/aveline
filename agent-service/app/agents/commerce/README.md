# Agent: Commerce Agent (Slice 3)

> **Owner:** Student 3  
> **Domain:** Commerce Validation & Optimization

## Responsibility

This agent makes **the deal work**. It is a LangGraph agent (graph) with the following
core responsibilities:

1. Calculate profit margins for orders
2. Apply loyalty-based discounts using business rules
3. Generate and validate payment requests
4. Check approval thresholds — trigger a **human-in-the-loop interrupt** if exceeded
5. Plan delivery routes and book couriers
6. Answer what a discount may be, with or without a basket: the tier/rule ceiling, and a quote for the
   pieces a pricing question named (ADR-028). Both are read-only: they never pause, settle or write.

## Tools available to this agent

Defined in `app/tools/commerce/`:

- `calculate_margin(order_id)` — compute margin based on cost + proposed price
- `get_customer_loyalty_tier(customer_id)` — look up discount eligibility
- `generate_payment_request(order_id, amount)` — create payment link via gateway
- `validate_payment(payment_id)` — confirm payment status with gateway
- `apply_discount(order_id, discount_percent)` — update order with approved discount
- `check_approval_threshold(order_id)` — evaluate against `Business_Rules`
- `book_courier(delivery_details)` — call courier API and record delivery plan
- `validate_business_rules(order_id)` — run all active rules against an order
- `pause_for_approval(order_id)` — issue a LangGraph interrupt; resume on owner decision

## Human-in-the-Loop

This agent contains the **mandatory approval gate**. When the business rules breach,
`evaluate_deal` routes to `pause_for_approval`, which reports `pending_approval` with the reason and
the triggered rules — and writes nothing. The pause is owned one layer up: the concierge node
`commerce_approval` calls `interrupt()`, the API turns the pause into an `Order` plus an
`ApprovalQueueEntry`, and the owner's decision resumes the checkpoint (ADR-024).

## Input / Output contract

Defined in `app/schemas/commerce.py`.

## Current state

The real sub-graph is implemented. `evaluate_deal` is the entry, and it has four terminals:

| Terminal | Reached when | Writes |
|---|---|---|
| `prepare_settlement` | the deal passes the rules, or the owner approved/revised it | payment link, courier booking |
| `pause_for_approval` | a rule was breached — the run stops for a decision | nothing; the *concierge* node `commerce_approval` owns the `interrupt()`, and the API writes the Order |
| `explain_discount_ceiling` | no basket, and the message asks what a discount may be (ADR-028) | nothing |
| `present_quote` | the message named pieces to price rather than to buy (ADR-028) | nothing |

The last two are the read-only arms. They exist because every other verb here is a deal verb, so a
question about a discount had nothing to answer with: `evaluate_deal` skipped for want of line items
and the run went silent, leaving the memory agent's customer brief as the only reply to a question
about money. A quote is priced by the same rules and still never pauses or settles — the API resolves
the pieces it names and marks the context `purpose: "quote"`, and
`ConversationOrderBridge` refuses to create an order from one.

The block mapping for this agent is in `app/events/block_builders.py::build_lina_blocks`: a `summary`
(with `status != "stub"`) becomes `text`, `payment` and `courier` blocks attributed to the `lina`
persona. A SignOff is deliberately not emitted by the generic builder - it is a first-class
human-in-the-loop message (`kind == SignOff`) created by the commerce approval flow (see
"Human-in-the-Loop" above).

## What belongs in this folder

- `graph.py` — The LangGraph `StateGraph` definition for this agent
- `state.py` — The typed state schema for the graph
- `nodes.py` — Individual node functions

## What does NOT belong here

- Tool implementations (those go in `app/tools/commerce/`)
- Pydantic I/O schemas (those go in `app/schemas/`)
- FastAPI routes (those go in `app/api/`)
