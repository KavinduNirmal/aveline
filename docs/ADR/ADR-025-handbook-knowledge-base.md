# ADR-025: The Handbook Knowledge Base (Hybrid Retrieval)

## Status
Accepted

## Context

The concierge could answer questions about a boutique's clients, catalogue and orders, but not about
Aveline itself. A boutique owner asking "How do I invite a staff member?", "What does the Orchid plan
include?" or "Can I message my clients from Aveline?" got a guess or a generic reply, because no
product documentation reached any prompt.

The material existed. `frontend/web/src/docs/` holds fifteen plain-GFM pages (1,879 lines, 104 `H2`
sections, ~314 table rows carrying the load-bearing facts), and the company's own facts — support,
plans, terms, the controller/processor split — lived in the marketing app. What did not exist was any
way for the agent to reach it.

[ADR-023](ADR-023-conversation-context-and-supervisor.md) named this lane and deferred it to its own
ADR, requiring a decision between grounded retrieval and a static prompt block, and noting that the
knowledge source did not exist yet. It does now.

Three constraints shaped the answer:

1. **ADR-017 already decided where vector work lives.** Embeddings are generated in the `.NET` API
   behind `IEmbeddingService` (OpenAI-compatible `text-embedding-3-small`, 1536 dimensions), the
   pgvector column sits outside the EF model because the in-memory test provider cannot map it, and
   semantic search runs in the `.NET` API via raw SQL behind an internal endpoint. Running pgvector
   search in Python was explicitly rejected.
2. **There is no LLM tool-calling in the agent service.** No `bind_tools`, `ToolNode` or
   `create_react_agent` call site exists: every "tool" is a deterministic node step calling
   `ToolRegistry`. A design that assumes the model can decide to search would be a new architecture,
   not a feature.
3. **Offline determinism is a hard guarantee.** With `agent_llm_enabled=false` or no key the workflow
   is fully rule-based, and CI depends on it.

## Options Considered

### 1. A static prompt block
Paste the documentation into the system prompt.
- **Pros:** no infrastructure.
- **Cons:** ~1,900 lines and roughly 25k tokens on every call, unbounded and growing, stale the moment
  a page changes, and it defeats the point of a bounded context window. Rejected.

### 2. A separate vector store, owned by the agent service (Chroma, Qdrant, …)
- **Pros:** keeps the corpus out of the application database.
- **Cons:** `ADR-003` chose one PostgreSQL with pgvector, and `ADR-017` already rejected a second search
  path. A second store is new ops (backups, migrations, secrets, a second consistency story) for a
  capability Postgres has. It would also need a second embedding client and a second provider
  configuration. Rejected.

### 3. LLM tool-calling: `bind_tools(["search_handbook"])`
- **Pros:** the model decides when to search.
- **Cons:** it does not exist in this codebase. It would add a ReAct loop, unbounded tool calls, and
  non-determinism to a workflow whose routing is deliberately deterministic, and it breaks the offline
  contract. Rejected.

### 4. `.NET`-owned pgvector plus Postgres full-text, fused, feeding the supervisor's reply (chosen)
- **Pros:** reuses the established embedding seam, the raw-SQL pattern, the internal-endpoint
  contract and the Testcontainers harness; keeps business logic in the API as ADR-017 requires;
  retrieval is a deterministic node, so the offline path is unchanged; one model call, because the
  supervisor is already consulted for exactly these messages.
- **Cons:** touches `.NET` (model, configuration, migration, repository, service, endpoints, tests);
  a hybrid query is more code than a cosine-only one; indexing becomes a release step to remember.

## Decision

### 1. The corpus is global, and audience is an explicit dimension
`HandbookChunk` is **not** tenant-scoped: the product documentation and the company facts describe
Aveline, not a boutique, so there is no `OrganizationId`. `Audience` (`staff` | `customer` | `both`)
exists from the first migration and retrieval always filters on it. The handbook is staff-facing
today; the column means a later customer-facing retrieval is a filter change rather than a migration,
and a staff-only page about billing cannot leak into a client conversation by omission.

### 2. Search stays in the `.NET` API, with both legs outside the EF model
Per ADR-017, the two search columns are raw SQL and are excluded from the EF model:

- `embedding vector(1536)` with an HNSW `vector_cosine_ops` index — the **dense** leg.
- `SearchVector tsvector` (`GENERATED ALWAYS AS`, weighted A/B/C over title, heading trail and body)
  with a GIN index — the **lexical** leg.

The weights are what make a label-shaped query ("Code Expiration") rank the page that carries the
label, without the heading being duplicated into every chunk body.

### 3. Retrieval is hybrid, fused with Reciprocal Rank Fusion
One SQL statement runs both legs over the same filter, each contributing a candidate pool, then fuses
them:

```
score(d) = Σ_leg 1 / (60 + rank_leg(d))
```

RRF is rank-based on purpose: cosine similarity and `ts_rank_cd` are not on a comparable scale and
their distributions differ per query, so rank fusion cannot be skewed by a leg whose raw scores happen
to be large. A document only one leg ranks still places, which is what makes the hybrid more
recall-capable than either leg alone — and the shipped test proves the case that matters: a chunk that
is only second on the dense leg but first on the lexical leg outranks the dense winner after fusion.

**Honesty about "BM25".** PostgreSQL's `ts_rank_cd` is lexical, but it is **not** BM25. The code and
`docs/architecture/handbook.md` call the leg "lexical" for that reason. If literal BM25 is ever
required, ParadeDB's `pg_search` extension is the upgrade path; it costs a custom database image, a
`docker-compose` change and a CI change, and it is confined to one SQL statement behind
`IHandbookRepository`.

### 4. Retrieval is a deterministic node; the answer is Aveline's
A new intent, **`aveline_help`**, names the person's goal. It is deliberately not `product_help`:
`product` already means the boutique's inventory in this codebase — it is Elle's entire lane — so
`product_help` would read as "help with our products".

`load_handbook` runs between `load_context` and `supervisor` and retrieves for exactly the two intents
the supervisor is consulted for (`general_inquiry`, `aveline_help`). The trigger is deterministic
rather than model-chosen, so the model is never handed a handbook it did not need and never has to
decide to search. The supervisor then answers from the excerpts in the same call that would otherwise
only route, using the `reply` field ADR-023 introduced.

**No subagent is added.** Aveline is the supervisor and the main voice; a platform question routes no
specialist, and `_route_after_resolve` sends it straight to `formulate_response`. The citation is
built from the chunks that were actually retrieved, never from the model's text, so a fabricated
source cannot reach a thread.

### 5. Ingestion reads the documentation in place
The seeder reads `frontend/web/src/docs/*.md` **in place**, so the index cannot drift from the
documentation it describes, and takes titles from each page's own `H1`. Company knowledge is authored
as `handbook/company/*.md` and listed explicitly, because those pages need metadata a file name cannot
carry. Ingestion is idempotent on `(SourceKey, Ordinal)` + `ContentHash`: re-running against unchanged
sources writes nothing at all.

The chunker emits an `H2`-level chunk per section, with the page's `H1` and intro as their own
"overview" chunk rather than prefixed onto every chunk. The first draft of this plan proposed
prefixing; with a lexical leg that would be actively harmful, because the intro's terms would appear
in every chunk and match every lexical query equally.

### 6. The lane is inert without a model
With `HANDBOOK_ENABLED=false`, or with no LLM configured, `load_handbook` makes no call at all,
`aveline_help` degrades to the conversational path, and the run is byte-identical to the behaviour
before this ADR. A retrieval failure is logged and ignored. The whole lane is an enhancement, never a
precondition.

## Consequences

- **One extra embedding call per consultable turn.** Retrieval runs for `general_inquiry` and
  `aveline_help` only, with a bounded candidate pool, so greetings and platform questions pay one
  cheap provider call and one indexed query each; product, pricing and order messages pay nothing.
- **Seeding is a release step.** An index that lags the documentation is the most likely way this
  feature goes quietly wrong. `GET /internal/handbook/sources` makes what is actually indexed visible,
  and `handbook/README.md` documents the re-seed.
- **The source documentation must be internally consistent.** Five contradictions are recorded as
  owner-supplied fixes; until they are resolved and the index re-seeded, retrieval can surface
  mutually exclusive answers for one question.
- **The prompt surface widens again.** Handbook excerpts are text in a privileged prompt, so the
  data-not-instruction rule in `SYSTEM_PROMPT.md` now names them, and the supervisor is instructed
  that excerpts are the only source for a platform question and that Blossom amounts and per-action
  costs are never quoted.
- **Recall is measured, not assumed.** The golden query set and `scripts/eval_handbook.py` report
  recall@1/@3 for the dense leg, the lexical leg and the hybrid separately, so "we added hybrid
  search" is falsifiable and the fusion can be tuned (`CandidatePool`, `FusionK`, chunk size) against
  evidence.
- **`aveline_help` classification needs tuning.** The rule patterns are deliberately high-precision; a
  false positive routes a message to the handbook, which answers it or says it cannot, and a false
  negative is the old behaviour. The golden set is where the precision/recall trade-off is settled.

## Related
- [ADR-003](ADR-003-database-strategy.md) — PostgreSQL with pgvector as the single datastore
- [ADR-009](ADR-009-internal-service-authentication.md) — the internal endpoint policy the handbook endpoints join
- [ADR-017](ADR-017-memory-pgvector-embeddings.md) — the embedding seam and the raw-SQL pgvector pattern this reuses
- [ADR-023](ADR-023-conversation-context-and-supervisor.md) — the supervisor and the deferred handbook lane
- [`docs/architecture/handbook.md`](../architecture/handbook.md) — corpus, chunking, retrieval, operations
- GitHub issue #397
