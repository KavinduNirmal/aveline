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
| `INTERNAL_API_TOKEN` | yes | Shared secret that must match the API's `AgentService:InternalToken`. Sent by the API in the `X-Internal-Token` header. |
| `DATABASE_URL` | yes | Async SQLAlchemy connection string (PostgreSQL 16 + pgvector). |
| `OPENAI_API_KEY` | no | LLM provider key for LangChain workflows. |
| `LANGSMITH_API_KEY` / `LANGSMITH_PROJECT` | no | LangSmith tracing. |
| `AVELINE_LOG_FORMAT` | no (default `json`) | `json` for structured logs, `text` for local dev. |

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
| `POST /agents/ping` | internal token | Echo endpoint that verifies service-to-service auth and logs the forwarded user context. |

## Logging

Structured JSON logs (`app/core/logging.py`) are emitted to stdout with
`timestamp`, `level`, `logger`, `message`, and any structured fields (e.g.
`user_id`, `reason`, `action`) — ready to ship to ELK or Application Insights.

## Tests

```bash
pytest tests/ -q
```

Coverage gate: ≥ 90% (`--cov=app --cov-fail-under=90`).

## Further reading

- Internal service auth: `docs/ADR/ADR-009-internal-service-authentication.md`
