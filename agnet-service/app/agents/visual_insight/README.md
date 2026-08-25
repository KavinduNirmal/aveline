# Agent: Visual Insight Agent (Slice 2)

> **Owner:** Student 2  
> **Domain:** Visual Intelligence & Sourcing

## Responsibility

This agent understands **what the boutique has and can source**. It is a LangGraph
agent (graph) with the following core responsibilities:

1. Analyze product images and extract structured attributes
2. Search inventory for items matching customer preferences
3. Match new arrivals to existing customers
4. Compose outfit suggestions for a given occasion
5. Search supplier catalogs and create sourcing requests

## Tools available to this agent

Defined in `app/tools/inventory/`:

- `analyze_product_image(image_url)` — call image recognition API, return attributes
- `search_inventory(criteria)` — semantic/structured search over `Inventory_Items`
- `match_customers_to_item(item_id)` — find customers whose preferences match this item
- `compose_outfit(customer_id, occasion)` — build a complete outfit from available stock
- `search_supplier_catalog(query)` — query external supplier API
- `create_sourcing_request(customer_id, image_url)` — initiate a sourcing workflow
- `check_stock(item_id)` — return current quantity

## Input / Output contract

Defined in `app/schemas/visual_insight.py`.

## What belongs in this folder

- `graph.py` — The LangGraph `StateGraph` definition for this agent
- `state.py` — The typed state schema for the graph
- `nodes.py` — Individual node functions

## What does NOT belong here

- Tool implementations (those go in `app/tools/inventory/`)
- Pydantic I/O schemas (those go in `app/schemas/`)
- FastAPI routes (those go in `app/api/`)
