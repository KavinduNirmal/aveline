# ADR-003: PostgreSQL with pgvector as the Single Datastore

## Status
Accepted

## Context
Aveline stores relational business data (customers, orders, inventory) and
semantic memory (customer preferences, product embeddings) that must be searched
by vector similarity. The demo budget is strict, and the team is small.

## Options Considered
1. **PostgreSQL 16 + pgvector (single instance)** — one datastore for relational rows and embeddings. Pros: one database to operate, ACID transactions across both, pgvector ANN indexes, cost-effective, managed options exist. Cons: pgvector recall/perf differs from purpose-built vector DBs at very large scale.
2. **Dedicated vector database (Pinecone/Qdrant)** — Pros: purpose-built ANN. Cons: second system to run/pay for, sync complexity.
3. **Document store (MongoDB)** — Pros: flexible schema. Cons: weak relational modeling for orders/inventory, no native pgvector-style ANN.

## Decision
**PostgreSQL 16 + `pgvector`**, single instance, single schema. Embeddings are
stored in `vector` columns and queried with ANN indexes.

## Consequences
- Relational + vector queries share the same transaction and backup story.
- One managed Postgres instance on Azure fits the < $100 budget.
- Vector search scale is bounded by one instance — acceptable for a boutique.
