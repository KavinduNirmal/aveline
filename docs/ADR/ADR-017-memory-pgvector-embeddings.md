# ADR-017: Customer Memory & pgvector Embeddings

## Status
Accepted

## Context

The Customer Concierge & Memory slice (Slice 1) stores a "living memory" per customer
(preferences, events, complaints, facts) that must be searched by *meaning* ("emerald silk
wedding") rather than by exact match. ADR-003 committed to PostgreSQL + pgvector as the single
datastore. The remaining questions were:

1. What dimension / provider produces the memory embeddings?
2. How does EF Core (in-memory in the test suite) coexist with a pgvector `vector` column?
3. Where does the embedding + cosine search run — the .NET API or the Python agent service?

## Options Considered

### 1. Embeddings: OpenAI `text-embedding-3-small` at 1536 dimensions (chosen)
A 1536-dimension vector is stored in a `vector(1536)` column with an HNSW `vector_cosine_ops`
index. The chat model (DeepSeek) and the embedding model are independent; embeddings are a
separate provider concern behind an `IEmbeddingService` seam.

### 2. pgvector column management: keep it outside the EF model (chosen)
The `embedding vector(1536)` column and its HNSW index are **not** part of the EF Core model.
EF Core's in-memory test provider cannot map the pgvector `vector` type, so adding it to the
entity broke model validation for every in-memory test in the suite. The column + index are
created by the EF migration via raw SQL, and written/searched through raw SQL in
`CustomerMemoryRepository`.

- Alternative considered: a dedicated `Pgvector.EntityFrameworkCore` CLR `Vector` type on the
  entity — rejected because it breaks the in-memory provider used by ~400 unit tests.

### 3. Search location: .NET API via raw SQL (chosen)
The .NET `CustomerMemoryRepository.SearchSemanticAsync` runs
`ORDER BY embedding <=> CAST(@q AS vector)` scoped to org + customer. Real-Postgres behaviour
is verified with Testcontainers against `pgvector/pgvector:pg16` (CI has Docker), because the
in-memory provider cannot exercise the vector column.

- Alternative considered: running pgvector search in the Python agent service (it already has a
  SQLAlchemy connection + `pgvector`). Rejected in favour of keeping business logic in the .NET
  API behind the internal endpoints the Python service already calls.

## Decision

1. Embeddings are **OpenAI-compatible `text-embedding-3-small`, 1536-dim**, generated behind an
   injectable `IEmbeddingService` (HTTP provider; stubbed in tests).
2. The `Customer_Memory.embedding vector(1536)` column + HNSW `vector_cosine_ops` index are
   created by the `AddCustomerConciergeEntities` migration via raw SQL and are external to the
   EF model.
3. Semantic search lives in `CustomerMemoryRepository` (raw SQL cosine), behind the
   `/internal/customers/{id}/memories/search` endpoint.
4. Real-DB behaviour is gated behind Testcontainers Postgres integration tests; in-memory unit
   tests cover everything that does not need the vector column.

## Consequences

- The agent (and any caller) must supply or request a real embedding; the search endpoint
  embeds the query text server-side via `IEmbeddingService`.
- CI's `build-api` job must be able to run Docker (Testcontainers) for the pgvector tests.
- Because the column is external to EF, the `.NET` model never reads the raw vector into memory —
  search returns projected `(content, category, similarity)` results, never the embedding blob.
- `Embeddings:ApiKey`/`Embeddings:BaseUrl`/`Embeddings:Model` must be configured before the
  search/save endpoints can call the provider.

## Related
- [ADR-002](ADR-002-agent-framework.md) — LangGraph agent layer consuming these endpoints.
- [ADR-003](ADR-003-database-strategy.md) — PostgreSQL + pgvector single datastore.
- [ADR-009](ADR-009-internal-service-authentication.md) — internal endpoints use `X-Internal-Token`.
