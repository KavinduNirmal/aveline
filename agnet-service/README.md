# Aveline Agent Service — FastAPI + LangGraph

Internal agentic AI service for Boutique Concierge workflows. It is **only ever
called by `Aveline.Api`** — clients (Flutter, React) never reach it directly.

## Prerequisites

- Python 3.12
- (optional) virtualenv

## Setup

```bash
cp .env.example .env.local   # fill in INTERNAL_API_TOKEN (+ OpenAI key)
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8000
```

Health check: `GET http://localhost:8000/health` (public).

## Environment variables

| Key | Required | Purpose |
|---|---|---|
| `INTERNAL_API_TOKEN` | yes | Shared secret that must match the API's `AgentService:InternalToken`. Sent by the API in the `X-Internal-Token` header. The service **refuses to start** if this is empty or a known placeholder. |
| `LLM_PROVIDER` | no (default `openai`) | `openai` or `deepseek` — selects the chat model at runtime. |
| `LLM_API_KEY` | no | API key for the selected LLM provider. |
| `LLM_BASE_URL` | no | Optional base URL override for the LLM provider. |
| `LLM_MODEL` | no | Default model name (e.g. `gpt-4o`, `deepseek-v4-flash`). |
| `DATABASE_URL` | yes | Async SQLAlchemy connection string (PostgreSQL 16 + pgvector). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | no | OTLP exporter endpoint for traces. |
| `OTEL_SERVICE_NAME` | no (default `aveline-agent-service`) | Resource attribute identifying this service in traces. |
| `OTEL_TRACE_CONTENT` | no (default `true`) | Set `false` in production to strip prompt/completion content from spans. |
| `AVELINE_LOG_FORMAT` | no (default `json`) | `json` for structured logs, `text` for local dev. |
| `REDIS_URL` | no | Redis connection URL for the Pub/Sub event bus (ADR-014). When unset the bus is disabled. |
| `SUBSCRIBE_EVENT_TYPES` | no | Space-separated event types this service consumes from the API (e.g. `message.received`). Empty until a consumer lands. |

## Redis Pub/Sub event bus

The agent publishes and consumes events over Redis Pub/Sub (ADR-014) via
`app/events/`. Channels are org-scoped (`aveline:<org_id>:<event_type>`); the agent
subscribes to the `aveline:*:<event_type>` pattern for the event types in
`SUBSCRIBE_EVENT_TYPES`. The subscriber is started/stopped on the FastAPI lifespan in
`app/main.py`. Publishing is fire-and-forget — critical operations should use the internal
HTTP path instead. See `docs/architecture/eventing.md`.

## Internal service authentication

Every route under `/agents` depends on `require_internal_token`
(`app/core/security.py`), which rejects requests that do not carry a valid
`X-Internal-Token` header:

- token not configured → `500` (fail-closed)
- token mismatch → `401` (constant-time comparison via `hmac.compare_digest`)
- success → request proceeds; validation is logged as a structured auth event

The API forwards the authenticated `userId` and roles in the request payload so
the agent can attribute the work.

## Endpoints

| Route | Auth | Description |
|---|---|---|
| `GET /health` | none | Liveness check (used by docker-compose / CI). |
| `GET /health/ready` | none | Readiness check — verifies DB connectivity (200 ready / 503 not ready). |
| `POST /agents/ping` | internal token | Echo endpoint that verifies service-to-service auth and logs the forwarded user context. |
| `POST /agents/query` | internal token | Run an agent workflow for a query and return the final result (stub graph until real agents land). |
| `POST /agents/query/stream` | internal token | Stream agent workflow events over SSE (`X-Accel-Buffering: no`). |

## Observability (OpenTelemetry)

`app/observability/tracing.py` configures the OpenTelemetry SDK with FastAPI,
HTTPX and LangChain auto-instrumentation plus an OTLP exporter. `init_tracing()`
is called from the FastAPI lifespan. Manual "chain-of-thought" spans are opened
with `chain_of_thought_span(...)`, which sets `gen_ai.*`, `llm.*` and `agent.*`
attributes. Set `OTEL_TRACE_CONTENT=false` in production to strip prompt/completion
content from exported spans. In docker-compose, traces flow to the `otel-collector`
and are visualized in Jaeger at `http://localhost:16686`.

## Rate limiting

`app/middleware/rate_limit.py` applies a Redis ZSET sliding-window rate limit to
expensive agent endpoints (`/agents/query*`). Allowed responses carry
`X-RateLimit-Limit` / `X-RateLimit-Remaining`; blocked requests return `429` with
`X-RateLimit-Reset` and `Retry-After`. The limiter fails open if Redis is
unreachable. Enabled automatically when `REDIS_URL` is configured.

## Logging

Structured JSON logs (`app/core/logging.py`) are emitted to stdout with
`timestamp`, `level`, `logger`, `message`, and any structured fields (e.g.
`user_id`, `reason`, `action`) — ready to ship to ELK or Application Insights.

## Tests

```bash
pytest tests/ -q
```

Coverage gate: ≥ 90% (`--cov=app --cov-fail-under=90`). DB integration tests are
skipped unless `TEST_DATABASE_URL` is set (e.g.
`postgresql+asyncpg://aveline:change-me@localhost:5433/aveline`).

## Further reading

- Internal service auth: `docs/ADR/ADR-009-internal-service-authentication.md`
