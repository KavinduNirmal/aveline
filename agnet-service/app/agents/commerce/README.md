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

## What belongs in this folder

- `graph.py` — The LangGraph `StateGraph` definition for this agent
- `state.py` — The typed state schema for the graph
- `nodes.py` — Individual node functions

## What does NOT belong here

- Tool implementations (those go in `app/tools/commerce/`)
- Pydantic I/O schemas (those go in `app/schemas/`)
- FastAPI routes (those go in `app/api/`)
