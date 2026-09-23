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
| `LLM_THINKING_ENABLED` | no (default `false`) | When `false`, reasoner-capable DeepSeek models are asked to skip the thinking pass. |
| `AGENT_STATE_DELAY_MS` | no (default `0`) | Artificial delay (ms) between emitted lifecycle states so clients can visibly animate Aveline's blossom during integration testing. `0` disables it. |
| `DATABASE_URL` | yes | Async SQLAlchemy connection string (PostgreSQL 16 + pgvector). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | no | OTLP exporter endpoint shared by traces and metrics. Metrics append `/v1/metrics`. |
| `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` | no | Metrics-specific OTLP/HTTP endpoint, used **verbatim** when set (wins over the generic endpoint above). |
| `OTEL_SEMCONV_STABILITY_OPT_IN` | no (default `http`) | Recorded semconv convention. The service sets `http` before instrumenting so its HTTP metrics use the stable seconds-based names the API uses. An explicit operator value wins. |
| `OTEL_SERVICE_NAME` | no (default `aveline-agent-service`) | Resource attribute identifying this service in traces and metrics. |
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
attributes. Every concierge graph node now opens one (`agent.node.<node>`), so
the helper has a real caller on the query path. Set `OTEL_TRACE_CONTENT=false` in
production to strip prompt/completion content from exported spans. In
docker-compose, traces flow to the `otel-collector` and are visualized in Jaeger
at `http://localhost:16686`.

### Metrics (OTLP push → collector → Prometheus)

`app/observability/metrics.py` builds a `MeterProvider` with an
`OTLPMetricExporter` next to the tracer provider; `init_metrics()` is called from
the FastAPI lifespan. There is deliberately **no `/metrics` route and no inbound
metrics port** — the agent pushes OTLP to the collector, whose config carries a
`metrics:` pipeline and a `prometheus` exporter that Prometheus scrapes. A
GET of `http://agent:8000/metrics` still returns `404`.

- **Endpoint precedence** (OTel spec): explicit argument > `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT`
  (verbatim) > `OTEL_EXPORTER_OTLP_ENDPOINT` + `/v1/metrics`. When none is set the
  provider is not built and every instrument is a no-op, so tests and local runs
  without a collector keep working.
- **Cumulative temporality** is pinned explicitly via
  `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=CUMULATIVE` **and** the
  exporter's `preferred_temporality`, so it cannot be flipped downstream.
  Prometheus requires cumulative.
- **Semconv**: `OTEL_SEMCONV_STABILITY_OPT_IN=http` is recorded before the HTTP
  instrumentors run, so the agent's HTTP metrics use the same new (seconds) names
  as `Aveline.Api` rather than the legacy millisecond incubating names.
- `opentelemetry-exporter-otlp-proto-http` is an explicit pin in
  `requirements.txt` (N-6): the metric exporter is the first Python code whose
  correctness depends on it.

The **additive** instruments (plan §6.6) are declared in `INSTRUMENTS` with their
exact label keys:

| Instrument | Type | Labels |
|---|---|---|
| `aveline.agent.run.duration` | histogram (s) | `workflow`, `status` |
| `aveline.agent.run.count` | counter | `workflow`, `status` |
| `aveline.agent.node.duration` | histogram (s) | `node` |
| `aveline.agent.node.failures` | counter | `node`, `error_class` |
| `aveline.agent.tool.calls` | counter | `tool`, `status` |
| `aveline.agent.retries` | counter | `node` |
| `aveline.agent.run.unattributed` | counter | — |
| `aveline.agent.stream.runs` | counter | — |
| `aveline.agent.llm.tokens.cached` | counter | `provider`, `model` |

There is intentionally **no input/output token counter**: the LangChain
instrumentor already emits `gen_ai.client.token.usage`, and a second measure of
the same quantity would be a defect (R-15). `workflow` is the workflow **name**
(`concierge`), never a run id; `run_id`/`step_index` never become metric labels
(plan §7.3).

### Step records

`POST /agents/query` creates a `TelemetryCollector` and passes it into
`run_concierge`. Each graph node writes an `AgentStepTelemetry` row (start /
complete, with duration and any LLM usage), and registry tool calls append a
`ToolCall` row. The collector is finalized into an `AgentRunTelemetry` payload and
sent through the existing `report_agent_run` → `POST /internal/agent-runs`
`steps[]` path, replacing the previous single synthetic step.


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
ruff check app/ tests/
```

Metrics and step-record coverage lives in `tests/test_agent_metrics.py` (every
instrument exists with exactly its declared label keys, no forbidden label key,
endpoint precedence, cumulative temporality, and `test_metrics_are_recorded` —
a real `/agents/query` that must move a non-zero counter) and
`tests/test_agent_step_records.py` (a real graph run writes more than one bounded
step, the serialized step keys match the .NET `AgentStepReportRequest` contract,
and `chain_of_thought_span` has a real caller).

Coverage gate: ≥ 90% (`--cov=app --cov-fail-under=90`). DB integration tests are
skipped unless `TEST_DATABASE_URL` is set (e.g.
`postgresql+asyncpg://aveline:change-me@localhost:5433/aveline`).

## Conversation context

`load_context` loads the conversation transcript named by `org_context.conversation_id`, fits it
to `context_window_tokens`, and carries it in three layers (`history`, `thread_summary`,
`pinned_slots`). The supervisor and the specialist sub-graphs that own a prompt all render the same
block through `app.context.render_context_block`, which is what lets a follow-up such as "the pink
one" resolve its referent. Without a conversation id the context is empty and the assembled prompt
is unchanged, so offline and CI runs stay deterministic.

Propagation requires **both** a declaration on the sub-graph's state schema and a pass-through in
the orchestrator node; LangGraph drops undeclared state keys silently. See
`docs/architecture/agent-context.md` for the path, scope, and limits.

## Handbook (platform questions)

`load_handbook` runs between `load_context` and the supervisor and retrieves handbook excerpts for
the two intents the supervisor is consulted for (`general_inquiry`, `aveline_help`). The supervisor
answers a platform question herself from those excerpts; the specialist sub-graphs do not run, and the
`sources` block is built from the chunks that were actually retrieved rather than from the model's
text, so both frontends can link each citation back to its page. With `HANDBOOK_ENABLED=false`, or
with no LLM configured, the node makes no call at all and the run is unchanged.

The index is seeded from a repository checkout, not at runtime:

```bash
python scripts/seed_handbook.py --dry-run                      # no HTTP at all
python scripts/seed_handbook.py --token "$INTERNAL_API_TOKEN"  # idempotent upsert
```

See `docs/architecture/handbook.md` for the corpus, chunking rules and the hybrid retrieval, and
`handbook/README.md` for authoring a company page.

## Further reading

- Internal service auth: `docs/ADR/ADR-009-internal-service-authentication.md`
- Conversation context propagation: `docs/architecture/agent-context.md`
- Layered context and the supervisor: `docs/ADR/ADR-023-conversation-context-and-supervisor.md`
- Handbook knowledge base: `docs/architecture/handbook.md`
- Handbook decision record: `docs/ADR/ADR-025-handbook-knowledge-base.md`
