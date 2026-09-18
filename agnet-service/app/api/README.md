# Agent Service: API Layer

This folder contains the **FastAPI route handlers** — the HTTP interface of the agent service.
They are guarded by the internal service token (`X-Internal-Token`, ADR-009).

## Routes (in `agents.py`)

| Method | Path | Purpose |
|---|---|---|
| POST | `/agents/ping` | Service-to-service auth echo |
| POST | `/agents/warmup` | Warm up agents with boutique context |
| POST | `/agents/query` | Run the concierge workflow and return a structured `AgentResponse` |
| POST | `/agents/query/stream` | Stream workflow lifecycle events over SSE |

`POST /agents/query` runs the full concierge workflow (intent gate → resolve customer →
specialist agents → formulate response), publishes lifecycle `agent.status` events, publishes
persona `message.created` events to the Salon, and reports always-on usage/blossom consumption to
the backend (`/internal/usage/record`, ADR-010).

`GET /health` (in `health.py`) stays public for liveness checks.

## Pattern

Handlers are thin: they validate input (`AgentQueryRequest`), call the workflow
(`run_concierge`), and return the result. Business logic lives in `app/workflows/`,
`app/agents/`, and the backend internal endpoints.

```python
@router.post("/query", response_model=AgentQueryResponse)
async def agents_query(payload: AgentQueryRequest, request: Request) -> AgentQueryResponse:
    ...
```

## What does NOT belong here

- Agent graph definitions (those go in `app/agents/`).
- Backend tool wrappers (those go in `app/tools/registry.py`).
- Business logic / database access (handlers stay thin).
