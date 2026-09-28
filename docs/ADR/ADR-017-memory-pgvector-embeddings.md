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
  search returns a projection (`content`, `category`, `source`, `confidence`, `isExplicit`,
  `similarity`), never the embedding blob. `source` and `isExplicit` are selected because the
  stated-versus-inferred distinction is the one a reader acts on, and it used to be unrecoverable
  from a search round trip.
- Search accepts a `minSimilarity` floor and applies it as a predicate rather than a post-filter,
  alongside the tenant, customer, soft-delete and expiry predicates. Ranking alone always returns
  `topK` rows however irrelevant, which is the wrong answer for an agent asking for context.
- `Embeddings:ApiKey`/`Embeddings:BaseUrl`/`Embeddings:Model` must be configured before the
  search/save endpoints can call the provider.

## Related
- [ADR-002](ADR-002-agent-framework.md) — LangGraph agent layer consuming these endpoints.
- [ADR-003](ADR-003-database-strategy.md) — PostgreSQL + pgvector single datastore.
- [ADR-009](ADR-009-internal-service-authentication.md) — internal endpoints use `X-Internal-Token`.
- [ADR-025](ADR-025-handbook-knowledge-base.md) — the hybrid (dense + lexical, RRF-fused) retrieval pattern customer memory now adopts.

## Follow-up (Slice 1 finalization, Issues #163–#167)

The LLM is now live in the running path (previously the memory agent was rule-based scaffolding
and `create_chat_model` was dead code):

- **Draft generation** uses `create_chat_model` when `AGENT_LLM_ENABLED` and an `LLM_API_KEY` +
  `LLM_MODEL` are configured (`agent-service/app/llm/runtime.py`). A deterministic template is the
  fallback whenever no model/key is present or the provider call fails, so CI and keyless local
  dev stay green and production degrades gracefully.
- **Usage / Blossoms** are reported for every completed workflow run to `/internal/usage/record`
  (ADR-010): real provider/model + langchain `usage_metadata` token split when the LLM ran, else a
  `rule-based` sentinel with zero tokens. Reporting is best-effort and never fails a query.
- **Agent outputs** are validated against the `MemoryAgentOutput` Pydantic schema in the running
  path (`app/schemas/customer_memory.py`), so contract drift between the graph's dicts and the
  typed schema fails loudly instead of silently.

## Implementation note: customer memory adopts the hybrid pattern (ADR-025)

Customer-memory search was the last dense-only retrieval in the system. It now mirrors
[ADR-025](ADR-025-handbook-knowledge-base.md): one statement runs a pgvector cosine leg and a
PostgreSQL full-text leg over the same tenant + customer + live + unexpired filter, and fuses them
with Reciprocal Rank Fusion.

**Why adopt it here.** Parity is not the reason by itself - the reason is that the two stores answer
different query shapes and memory was only serving one of them. Memory queries are frequently exact
tokens: a garment or product name a customer mentioned ("Kanjeevaram", "Banarasi silk", "bridal
lehenga"), a label-like phrase, or a term a paraphrase-trained embedding ranks mid-pack. The
handbook's eval showed the lexical leg is what carries those (competitive with the dense leg on
exact queries, and collapsed on paraphrases), and the fused result was never worse than either leg.

- **The store is small and customer-scoped, and that is the point.** The lexical leg is not earning
  its place on corpus size (the handbook's corpus needed `websearch_to_tsquery` precision across
  many sources). Its recall is bounded to one customer's live notes, so the candidate set is tiny
  and the GIN lookup is cheap; what the leg buys is exact-token *precision* on the vocabulary the
  customer actually used. A dense leg alone blurs a specific garment name into a general "elegant
  clothing" region, which a boutique experiences as "it forgot the saree I told it about".
- **RRF is rank-based for the same reason it is in the handbook.** Cosine similarity and
  `ts_rank_cd` are not on a comparable scale, so a hit found by only one leg must still place.

**What was added (migration `AddCustomerMemorySearchVector`).** A generated `SearchVector tsvector`
weighted `Content` `'A'` then `Category` `'B'`, plus a GIN index. Like the `embedding vector(1536)`
column, `SearchVector` is **not** part of the EF model - EF cannot map `tsvector`, and mapping it
would break the in-memory provider for the whole suite - so it is created by raw SQL and searched
through raw SQL in `CustomerMemoryRepository`. The GIN index is **partial on `DeletedAt IS NULL`**:
every search leg filters live rows, and rows are only ever soft-deleted except by a GDPR erase, so
indexing withdrawn notes would be dead weight (and the predicate matches the existing partial unique
index's definition of "present").

**Eval surface.** `MemorySearchRequest.Mode` is `hybrid` (default) | `lexical` | `vector`, and each
hit carries `VectorRank`, `LexicalRank` and the fused `Score` alongside the dense `Similarity`. An
unknown mode is refused (`400`) rather than defaulted: a misspelled single-leg request silently
answered by fusion would be read as a leg measurement. `MinSimilarity` continues to bound the
**dense leg only** - a lexical hit has no cosine to floor, and a post-fusion floor would delete
exactly the lexical-only hits the hybrid exists to surface. A failed query embedding degrades the
search to the lexical leg, as it does for the handbook, with `VectorRank` NULL so the degradation is
visible on the wire.

## Implementation note: model-driven fact extraction (the "nitbits")

**Why deterministic-only extraction was insufficient.** The rule-based parse recognises exactly
three shapes in the *current message*: a first-person preference (`"I like/prefer/hate …"`), a dated
event, and a quoted complaint. A live thread showed the ceiling of that approach. "nothing nylon, or
spandex, I dont like them, they make my skin itchy" produces a preference whose object is the
pronoun *them*; no regex can know what *them* is, so the signal is dropped (before the pronoun guard
it stored the false "prefers them"). "browsing a brown dress seen on Instagram" is an observation
and matches no shape at all, so it was never stored. Only the wedding was recorded, because a dated
event is the one shape that matched. The product requirement is that Ava write these small facts
herself "so she can find it regardless of her context" - a fact retrieval can only satisfy if it was
written down.

**Decision.** An `extract` node runs between `retrieve` and `persist`, and with a model configured
reads the small, durable facts out of an inbound message. It is a *task contract* beside the node,
not a new agent persona: it reuses the memory system prompt through the same `assemble_system_prompt`
layer the draft and staff-answer calls use, rather than a parallel prompt mechanism. The design
choices that make the facts trustworthy:

- **The transcript resolves references; it is not a source of facts.** The bounded window is handed
  over with that label in the prompt. A model given a conversation will summarise it, and a
  summarised conversation is not a fact about this customer. Each stored fact is one self-contained
  sentence that starts with the customer's name, so it makes sense with no context beside it - which
  is what retrieval across conversations requires.
- **The category vocabulary is closed** (`preference`, `event`, `observation`, `complaint`,
  `constraint`). A model free to name its own category invents a taxonomy one row at a time, and the
  at-a-glance table is read by people.
- **`stated` is the customer's distinction, not the boutique's.** A fact the customer asserted is
  `IsExplicit=true` and reads "Stated"; what the boutique observed (what they browsed) is `false` and
  reads "Inferred", so the column keeps meaning something.
- **The node is best-effort and owns no writes.** No model, a staff query, an empty message, a
  provider failure or an unparseable reply all yield no facts; `persist` owns every write, so a
  provider outage costs facts, never a run. Confidence is clamped to the store's 0..1 range, facts
  are capped per turn, and empty or over-long sentences and unknown categories are dropped on parse.
- **The regex classes still lead.** Extracted facts are written after them, skipping any whose
  normalised content a deterministic class already wrote this turn (`ContentKey` would refuse the
  duplicate anyway). An extracted preference carrying a key/value is also mirrored into
  `Customer_Preferences`, so the brief's summary cannot say "nothing recorded" while the note sits
  in the store.

**Consequences.** An inbound turn now makes up to two model calls, so the run's reported token usage
is their **sum** (ADR-010 reports per run, not per call). The offline and CI path is unchanged: with
no model there is no extraction call and the deterministic writes are byte-identical. The model can
still be wrong; the closed vocabulary, the caps, the reference-resolution framing, the explicit
"never invent" constraint and the provenance fields bound that risk, and a wrong model fact is a
normal, correctable memory row rather than a schema failure.

