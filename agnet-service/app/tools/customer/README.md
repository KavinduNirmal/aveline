# Tools: Customer Memory Agent

This folder contains tool implementations for the **Customer Memory Agent** (Slice 1).

## What belongs here

One Python file per tool (or a logical grouping). Each tool is a Python function decorated
with `@tool` from LangChain, enabling the agent to call it:

| Tool | File | Description |
|---|---|---|
| `search_customer_profile` | `profile_tools.py` | Fetch customer record + preferences from DB |
| `get_customer_memory` | `memory_tools.py` | Semantic search via pgvector embeddings |
| `save_customer_memory` | `memory_tools.py` | Persist new memory, generate embedding |
| `extract_entities_from_message` | `nlp_tools.py` | Parse intent/occasion/color from raw text |
| `generate_interaction_brief` | `brief_tools.py` | Generate staff-facing summary for the associate |
| `send_whatsapp_message` | `messaging_tools.py` | Call WhatsApp Business API via backend |

## Rules

- Each tool must have a clear docstring — LangGraph uses this as the tool description
- Tools must validate their inputs using Pydantic `@tool` argument schemas
- Tools must never call other tools directly — the agent graph handles sequencing
- All DB access goes through `app/db/` — never import raw psycopg2 here
- All HTTP calls go through `app/services/` — no `httpx` calls directly in tool files
