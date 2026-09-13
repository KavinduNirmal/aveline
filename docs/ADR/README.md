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
| `ADR-010-usage-tracking-architecture.md` | Blossom usage tracking — two-table design, .NET API as recording owner, token-based normalization |
| `ADR-011-credential-encryption.md` | AES-256-GCM encryption of tenant integration credentials |
| `ADR-012-invitation-code-lifecycle.md` | One-time staff invitation code lifecycle |
| `ADR-013-notification-service-architecture.md` | Notification gateway + channel adapters (SignalR realtime, FCM push, email) |
| `ADR-014-redis-pubsub-event-bus.md` | Redis Pub/Sub event bus for API–agent decoupling (reusable `IEventBus` abstraction) |
| `ADR-015-whatsapp-integration-gateway.md` | WhatsApp integration gateway — additive status lifecycle, `IWhatsAppService` provider, HMAC-verified webhook, health service |
| `ADR-016-conversation-inbox.md` | The Salon — unified agent-to-staff conversation inbox with typed rich messages, persona authors, threaded replies, and LangGraph `threadId` linkage |
| `ADR-017-memory-pgvector-embeddings.md` | Customer memory semantics — OpenAI `text-embedding-3-small` 1536-d embeddings, pgvector column kept outside the EF model, cosine search in the .NET API via raw SQL + Testcontainers |
| `ADR-018-realtime-conversation-delivery.md` | The Salon realtime delivery model — batched `message.created` cards + `agent.status` lifecycle (no token streaming yet); SignOff LangGraph resume and real specialist sub-graphs deferred |
| `ADR-019-entity-mentions.md` | Explicit entity mentions (`@name`, `#phone`, …) for deterministic customer resolution; free-text extraction kept as fallback |
| `ADR-020-multimodal-vision-provider.md` | Multimodal vision provider — centralized OpenAI-compatible `VisionService`, config resolution, ADR-010 Blossom usage tracking, deterministic offline fallback |

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
