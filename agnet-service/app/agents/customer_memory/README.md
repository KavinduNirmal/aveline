# Agent: Customer Memory Agent (Slice 1)

> **Owner:** Student 1  
> **Domain:** Customer Concierge & Memory

## Responsibility

This agent understands **who the customer is and what they want**. It is a LangGraph
agent (graph) with the following core responsibilities:

1. Parse raw WhatsApp messages into structured intent
2. Identify the customer from their phone number
3. Retrieve relevant semantic memories using pgvector
4. Build a concise interaction brief for the associate
5. Draft a personalized customer response

## Tools available to this agent

Defined in `app/tools/customer/`:

- `search_customer_profile(customer_id)` — fetch full customer record + preferences
- `get_customer_memory(customer_id, query)` — semantic search over memory embeddings
- `save_customer_memory(customer_id, content, category)` — persist a new memory
- `extract_entities_from_message(message_text)` — extract intent, occasion, color, size, budget
- `generate_interaction_brief(customer_id)` — produce a staff-facing summary
- `send_whatsapp_message(customer_id, message)` — dispatch outbound message

## Input / Output contract

Defined in `app/schemas/customer_memory.py`.

## What belongs in this folder

- `graph.py` — The LangGraph `StateGraph` definition for this agent
- `state.py` — The typed state schema (`TypedDict` or Pydantic model) for the graph
- `nodes.py` — Individual node functions that the graph calls

## What does NOT belong here

- Tool implementations (those go in `app/tools/customer/`)
- Pydantic I/O schemas (those go in `app/schemas/`)
- FastAPI routes (those go in `app/api/`)
