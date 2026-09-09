# Tools: Customer Memory Agent

> This directory no longer holds per-tool Python files.

The Customer Memory Agent (Slice 1) calls the ASP.NET Core backend through a single shared
**`ToolRegistry`** (`agnet-service/app/tools/registry.py`) which is a thin, authenticated wrapper
over the backend internal endpoints. There is intentionally **no** `@tool`/per-tool file split —
the registry methods map 1:1 onto the `/internal/customers/*` endpoints.

## Registry methods the memory agent uses

| ToolRegistry method | Internal endpoint | Purpose |
|---|---|---|
| `identify_customer` | `POST /internal/customers/identify` | Look up by phone, create `new` when absent |
| `lookup_customers` | `POST /internal/customers/lookup` | Read-only lookup by name/phone/email |
| `search_customer_profile` | `GET /internal/customers/{id}/profile` | Full profile (preferences, tags, consent) |
| `get_customer_memories` | `POST /internal/customers/memories/search` | pgvector semantic search |
| `save_customer_memory` | `POST /internal/customers/{id}/memories` | Persist a semantic memory |
| `record_customer_interaction` | `POST /internal/customers/{id}/interactions` | Log an inbound/outbound interaction |
| `add_customer_event` | `POST /internal/customers/{id}/events` | Persist a structured event |
| `get_customer_events` | `GET /internal/customers/{id}/events` | List a customer's structured events |
| `generate_interaction_brief` | `GET /internal/customers/{id}/brief` | Staff-facing interaction brief |
| `get_customer_consent` | `GET /internal/customers/{id}/consent` | Consent status |

The registry shares `InternalApiClient` for transport (attaches the `X-Internal-Token` header,
ADR-009). It never talks to the database or third parties directly.
