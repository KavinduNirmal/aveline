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

This agent contains the **mandatory approval interrupt**. When `check_approval_threshold`
returns `True`, the graph must call `pause_for_approval`, which issues a LangGraph
`interrupt()`. The ASP.NET Core backend stores the checkpoint thread ID. The React
dashboard calls the resume endpoint when the owner decides.

## Input / Output contract

Defined in `app/schemas/commerce.py`.

## Current stub state (Issue #151)

No `graph.py` exists yet; this folder is owned by **Student 3**. The top-level concierge
workflow (`app/workflows/concierge_workflow.py`) runs a **stub** `run_commerce_agent` node
that declares the structured output shape with `status: "stub"`, `needs_approval: False`,
and empty `summary`/`payment`/`courier`, so the Salon never shows fabricated payment or
approval data.

The block mapping for this agent is already implemented and tested in
`app/events/block_builders.py::build_lina_blocks`: when the real graph emits a `summary`
(with `status != "stub"`), the Salon renders `text`, `payment`, and `courier` blocks
attributed to the `lina` persona. A SignOff is deliberately not emitted by the generic
builder - it is a first-class human-in-the-loop message (`kind == SignOff`) created by the
commerce approval flow (see "Human-in-the-Loop" above).

## What belongs in this folder

- `graph.py` — The LangGraph `StateGraph` definition for this agent
- `state.py` — The typed state schema for the graph
- `nodes.py` — Individual node functions

## What does NOT belong here

- Tool implementations (those go in `app/tools/commerce/`)
- Pydantic I/O schemas (those go in `app/schemas/`)
- FastAPI routes (those go in `app/api/`)
