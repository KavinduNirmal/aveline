# Tools: Customer Memory Agent

> This directory no longer holds per-tool Python files.

The Customer Memory Agent (Slice 1) calls the ASP.NET Core backend through a single shared
**`ToolRegistry`** (`agent-service/app/tools/registry.py`) which is a thin, authenticated wrapper
over the backend internal endpoints. There is intentionally **no** `@tool`/per-tool file split —
the registry methods map 1:1 onto the `/internal/customers/*` endpoints.

## Registry methods the memory agent uses

| ToolRegistry method | Internal endpoint | Purpose |
|---|---|---|
| `identify_customer` | `POST /internal/customers/identify` | Look up by phone, create `new` when absent |
| `lookup_customers` | `POST /internal/customers/lookup` | Read-only lookup by name/phone/email |
| `search_customer_profile` | `GET /internal/customers/{id}/profile` | Full profile (preferences, tags, consent) |
| `get_customer_memories` | `POST /internal/customers/memories/search` | pgvector semantic search (carries `minSimilarity`) |
| `save_customer_memory` | `POST /internal/customers/{id}/memories` | Persist a semantic memory with its provenance |
| `save_customer_preference` | `POST /internal/customers/{id}/preferences` | Record a stated preference the brief's summary reads |
| `record_customer_interaction` | `POST /internal/customers/{id}/interactions` | Log an inbound/outbound interaction |
| `add_customer_event` | `POST /internal/customers/{id}/events` | Persist a structured event |
| `get_customer_events` | `GET /internal/customers/{id}/events` | List a customer's structured events |
| `generate_interaction_brief` | `GET /internal/customers/{id}/brief` | Staff-facing interaction brief |
| `get_customer_consent` | `GET /internal/customers/{id}/consent` | Consent status |

The registry shares `InternalApiClient` for transport (attaches the `X-Internal-Token` header,
ADR-009). It never talks to the database or third parties directly.

## There is deliberately no send tool

`PROJECT_CONTEXT` lists `send_whatsapp_message(customer_id, message)` among this agent's tools, and
this registry does not expose it. That is a decision, not an omission:

- **AVA advises; it does not act on the customer's channel.** The memory agent's terminal act is to
  emit a draft plus `action_required`, and the draft is sent by a human. A tool that could message a
  customer from inside the agent's own turn would put a model's output in front of a customer with
  nobody in between, which is the one thing the platform's approval posture exists to prevent.
- **The send path already exists, and it is approval-gated.** `ICustomerDeliveryService` is the
  single path by which words leave the boutique for a customer's own channel, it records what went
  out in the Salon thread, and it refuses a revoked customer outright. The agent's job is to produce
  the content that path delivers.
- **The one `action_required` value the agent does emit is a request, not a send.**
  `action = "send_whatsapp"` tells the Salon that this draft is ready to go to a customer; the client
  renders it as a control an associate operates.

The other tool `PROJECT_CONTEXT` lists, `extract_entities_from_message(message_text)`, is not a
registry method either: parsing is a pure function (`app/agents/customer_memory/parsing.py`) that
the `parse` node calls directly. A tool that only wraps an in-process function would add a network
hop to nothing, and the determinism the parse rules provide is worth more than the registry's
instrumentation.
