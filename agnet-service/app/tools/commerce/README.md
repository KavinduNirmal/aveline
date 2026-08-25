# Tools: Commerce Agent

This folder contains tool implementations for the **Commerce Agent** (Slice 3).

## What belongs here

| Tool | File | Description |
|---|---|---|
| `calculate_margin` | `pricing_tools.py` | Compute margin based on cost + proposed price |
| `get_customer_loyalty_tier` | `loyalty_tools.py` | Determine discount eligibility |
| `generate_payment_request` | `payment_tools.py` | Create payment link via gateway API |
| `validate_payment` | `payment_tools.py` | Confirm payment status with gateway |
| `apply_discount` | `pricing_tools.py` | Update order with approved discount |
| `check_approval_threshold` | `approval_tools.py` | Evaluate against `Business_Rules` table |
| `book_courier` | `delivery_tools.py` | Call courier API, record delivery plan |
| `validate_business_rules` | `rules_tools.py` | Run all active rules against an order |
| `pause_for_approval` | `approval_tools.py` | Issue LangGraph `interrupt()` — HITL gate |

## The Approval Interrupt

`pause_for_approval` is the **human-in-the-loop** gate. It uses LangGraph's `interrupt()`
primitive to pause the graph. The checkpoint thread ID is saved in `Approval_Queue` by the
ASP.NET Core backend. When the owner approves via React, the backend calls the resume
endpoint, which restarts the graph from the checkpoint.

## Rules

- Each tool must have a clear docstring — LangGraph uses this as the tool description
- Tools must validate their inputs using Pydantic `@tool` argument schemas
- All DB access goes through `app/db/` — never import raw psycopg2 here
- External API calls go through `app/services/` — no `httpx` calls directly in tool files
