# Services — Business Logic Helpers

This folder contains **service modules** that perform business logic and external calls on
behalf of agents and tools. They sit between the tools and raw I/O (DB or HTTP).

## What belongs here

| Module | Purpose |
|---|---|
| `embedding_service.py` | Generate text embeddings (calls OpenAI / sentence-transformers) |
| `whatsapp_service.py` | Calls ASP.NET Core backend `/api/whatsapp/send` endpoint |
| `payment_service.py` | Calls ASP.NET Core backend payment gateway proxy |
| `courier_service.py` | Calls ASP.NET Core backend courier API proxy |
| `image_recognition_service.py` | Calls image recognition API for product photo analysis |

## Rules

- Services are plain Python classes or module-level functions — not FastAPI routes
- Use `httpx.AsyncClient` for all outbound HTTP calls
- All base URLs and API keys come from `app/core/config.py` — never hardcoded
- Services do NOT call the database — that is done in `app/db/`

## What does NOT belong here

- FastAPI route logic (goes in `app/api/`)
- LangGraph tool definitions (goes in `app/tools/`)
- Database session management (goes in `app/db/`)
