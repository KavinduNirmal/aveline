# Workflows — LangGraph Orchestration

This folder contains the **top-level orchestration graph** that coordinates all three agents.

## What belongs here

- **`concierge_workflow.py`** — The root LangGraph `StateGraph` that:
  1. Receives an inbound customer message (with thread ID for checkpointing)
  2. Delegates to the Customer Memory Agent sub-graph
  3. Delegates to the Visual Insight Agent sub-graph
  4. Delegates to the Commerce Agent sub-graph (which may interrupt for approval)
  5. Returns the final result or pauses at the approval interrupt

- **`checkpointer.py`** — PostgreSQL-backed LangGraph checkpointer setup
  (`langgraph.checkpoint.postgres`). Thread IDs correspond to conversation sessions
  and are stored in `Approval_Queue.thread_id` for resume.

## What does NOT belong here

- Agent graph definitions (those live in `app/agents/<agent_name>/graph.py`)
- Tool implementations (those live in `app/tools/`)
- FastAPI routes (those live in `app/api/`)

## Key concept: Sub-graphs

Each agent (`customer_memory`, `visual_insight`, `commerce`) is compiled into its own
`CompiledGraph` and referenced as a sub-graph node in the parent workflow. This keeps
concerns separated and allows each student to develop their agent independently.
