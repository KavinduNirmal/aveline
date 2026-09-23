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
| `ADR-021-per-user-salon-ownership.md` | Per-user ownership of the general Salon — supersedes ADR-016 decision 1 in part; customer-bound Salons stay organization-shared |
| `ADR-022-media-storage-and-access.md` | Media storage and access — the two-tier Cloudinary model (`upload` catalog vs `authenticated` protected), the streaming token proxy, the tag/context schema, "no third-party CDN" (AUP §4.2), R2 as the recorded escape hatch, and the deferred `ImageData` drop |
| `ADR-023-conversation-context-and-supervisor.md` | Layered conversation context (bounded turn window + rolling thread summary + reusable pgvector memory + on-demand tools) and the supervisor LLM that replaces the keyword table as routing authority; clarification becomes a first-class outcome instead of a resolution veto; handbook/product-help lane deferred to its own ADR |
| `ADR-024-conversation-orders-and-hitl-resume.md` | Conversation-initiated orders and the HITL approval loop — order context is derived on the API side from explicit customer intent, the API (never the agent) creates the Order and approval entry when a run pauses, and approval resumes through the LangGraph checkpointer rather than replaying the request |
| `ADR-025-handbook-knowledge-base.md` | The handbook knowledge base — a global, audience-scoped corpus in `.NET`; hybrid retrieval (pgvector cosine + PostgreSQL full-text, fused with Reciprocal Rank Fusion) with both search columns outside the EF model per ADR-017; a deterministic `load_handbook` node feeding the supervisor's own reply, with a citation built from the retrieved chunks rather than from model text |

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
