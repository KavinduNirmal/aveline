# Agent Service: API Layer

This folder contains **FastAPI route handlers** — the HTTP interface of the agent service.

## What belongs here

One router module per agent/workflow, for example:

- `customer_memory.py` — routes that trigger the Customer Memory Agent
- `visual_insight.py` — routes that trigger the Visual Insight Agent
- `commerce.py` — routes that trigger the Commerce Agent

Each module should define an `APIRouter` and be mounted in `app/main.py`.

## What does NOT belong here

- Agent graph definitions (those go in `app/agents/`)
- Tool implementations (those go in `app/tools/`)
- Business logic (route handlers should be thin — validate input, call workflow, return output)
- Database access (never query the DB directly from a route handler)

## Pattern

```python
from fastapi import APIRouter
from app.schemas.customer_memory import RunAgentRequest, RunAgentResponse
from app.workflows.concierge_workflow import run_concierge_workflow

router = APIRouter()

@router.post("/run", response_model=RunAgentResponse)
async def run_customer_memory_agent(request: RunAgentRequest) -> RunAgentResponse:
    result = await run_concierge_workflow(request)
    return result
```
