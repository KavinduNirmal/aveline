# Schemas — Pydantic I/O Models

This folder contains all **Pydantic** request and response models used by the agent service.

## Purpose

Schemas define the **public API contract** between the ASP.NET Core backend and the agent
service. They also define the input/output types for each agent workflow.

## What belongs here

One module per agent/workflow:

- **`customer_memory.py`** — `RunCustomerMemoryRequest`, `RunCustomerMemoryResponse`
- **`visual_insight.py`** — `RunVisualInsightRequest`, `RunVisualInsightResponse`
- **`commerce.py`** — `RunCommerceRequest`, `RunCommerceResponse`
- **`common.py`** — Shared types (e.g., `WorkflowStatus`, `AgentError`)

## Rules

- All fields must have type annotations
- Use `model_config = ConfigDict(extra="forbid")` to reject unknown fields
- Response models must not expose internal implementation details
- Use `Optional[T]` only when a field is genuinely optional

## What does NOT belong here

- Agent state schemas (those go in `app/agents/<name>/state.py` — internal to the graph)
- Database models (those go in `app/db/`)
- Any logic — schemas are pure data definitions
