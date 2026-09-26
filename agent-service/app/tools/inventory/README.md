# Tools: Visual Insight Agent

This folder contains tool implementations for the **Visual Insight Agent** (Slice 2).

## What belongs here

| Tool | File | Description |
|---|---|---|
| `analyze_product_image` | `image_tools.py` | Call image recognition API, extract attributes |
| `search_inventory` | `inventory_tools.py` | Semantic/structured inventory search |
| `match_customers_to_item` | `matching_tools.py` | Find customers whose preferences match an item |
| `compose_outfit` | `outfit_tools.py` | Build a complete outfit from available stock |
| `search_supplier_catalog` | `supplier_tools.py` | Query external supplier API |
| `create_sourcing_request` | `sourcing_tools.py` | Initiate a sourcing request in the DB |
| `check_stock` | `inventory_tools.py` | Return current quantity for an item |

## Rules

- Each tool must have a clear docstring — LangGraph uses this as the tool description
- Tools must validate their inputs using Pydantic `@tool` argument schemas
- All DB access goes through `app/db/` — never import raw psycopg2 here
- External API calls go through `app/services/` — no `httpx` calls directly in tool files
