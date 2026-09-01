# ADR-002: LangGraph for the Agentic AI Layer

## Status
Accepted

## Context
The Python agent subsystem must plan and execute workflows (customer concierge,
visual intelligence, commerce), use tools, pause for human approval, and persist
conversation/execution state. It must run behind the ASP.NET Core API.

## Options Considered
1. **LangGraph** — graph-based state machine over LLM calls. Pros: explicit workflows, `interrupt()` for human-in-the-loop approvals, PostgreSQL checkpointers, first-class tool calling, strong ecosystem. Cons: adds a Python framework dependency and API coupling.
2. **LangChain alone** — chain-based. Pros: familiar. Cons: less structured for branching workflows and resumable checkpoints.
3. **Custom state machine** — no framework. Pros: zero deps. Cons: re-implements checkpointing, tooling, and HITL.

## Decision
**LangGraph** (FastAPI + LangGraph service, `agnet-service/app/`), with a
PostgreSQL checkpointer so long-running workflows survive restarts and can
resume after an owner approves or revises a step.

## Consequences
- The agent service depends on the LangGraph API; schema/workflow definitions live in `agnet-service`.
- Checkpoint state is persisted in PostgreSQL (same instance as business data).
- Human-in-the-loop approvals surface as resumable interrupts, consumed by the React dashboard.
