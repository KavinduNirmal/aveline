# ADR — Architecture Decision Records

This folder contains Architecture Decision Records (ADRs) for the Aveline project.

## What is an ADR?

An ADR is a short document that captures an important architectural decision, the context
that led to it, the options considered, and the outcome. They are permanently kept in version
control as an audit trail.

## Required ADRs (per assignment specification)

| File | Decision |
|---|---|
| `ADR-001-monolith-vs-microservices.md` | Why a Modular Monolith was chosen over microservices |
| `ADR-002-agent-framework.md` | Why LangGraph was chosen for the agentic AI layer |
| `ADR-003-database-strategy.md` | PostgreSQL with pgvector — single instance, one schema |
| `ADR-004-state-management-react.md` | State management approach for the React dashboard |
| `ADR-005-state-management-flutter.md` | State management approach for the Flutter app |
| `ADR-006-deployment-platform.md` | Cloud platform selection (Azure/Railway/Render + Vercel) |
| `ADR-007-clerk-authentication.md` | Clerk as identity provider + JwtBearer/JWKS validation strategy |
| `ADR-008-jwt-token-strategy.md` | JWT strategy — Clerk custom template `jwt-aveline-v1` and its role claims |
| `ADR-009-internal-service-authentication.md` | Internal service-to-service auth via `X-Internal-Token` |

## ADR Template

Use this template for each ADR:

```markdown
# ADR-XXX: [Title]

## Status
Proposed | Accepted | Deprecated | Superseded by ADR-XXX

## Context
What is the problem or decision that needs to be made?

## Options Considered
1. Option A — pros/cons
2. Option B — pros/cons

## Decision
Which option was chosen and why.

## Consequences
What are the trade-offs and follow-on implications?
```
