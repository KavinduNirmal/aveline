# ADR-001: Modular Monolith over Microservices

## Status
Accepted

## Context
The platform must demonstrate a complete cross-platform workflow (Flutter → ASP.NET
Core API → Python agent → PostgreSQL → React dashboard) for a 3-student SE3090
assignment. Each student owns one vertical slice. The backend must be an ASP.NET
Core API (mandatory), with a small team and a strict budget.

## Options Considered
1. **Microservices** — independent deployables per domain. Pros: team autonomy, scaling. Cons: orchestration, service discovery, observability overhead, higher ops burden for a demo.
2. **Modular Monolith** — single ASP.NET Core project with clean domain boundaries. Pros: one deployable, simple local dev, shared infrastructure, low ops cost, slices isolated by modules. Cons: single deployment unit; boundaries rely on discipline.
3. **Monolithic app (no module boundaries)** — simplest but poor separation for three students.

## Decision
A **modular monolith**: one `Aveline.Api` project with vertical-slice modules
(`Modules/CustomerConcierge`, `Modules/VisualIntelligence`, `Modules/Commerce`),
shared `Common/` and `Infrastructure/` layers, and explicit configuration
extensions. Each student owns one module.

## Consequences
- Single deployable (Container App) keeps the < $100 demo budget viable.
- Module boundaries must be enforced by convention (documented in each module README).
- Slices can be extracted into services later without a rewrite if the monolith grows.
