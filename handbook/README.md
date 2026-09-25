# The Aveline Handbook

The knowledge base Aveline answers platform and company questions from. Two corpora feed it:

| Corpus | Where it lives | How it is found |
|---|---|---|
| **Product documentation** | `frontend/web/src/docs/*.md` | discovered automatically; read **in place**, never copied |
| **Company knowledge** | `handbook/company/*.md` | listed explicitly in `agent-service/app/handbook/sources.py` |

The product docs are the source of truth for how the app works, and reading them in place is what
stops the index from drifting from the documentation it describes. The company pages exist because
some facts are not product documentation: support, plans, Blossoms, the legal terms, and an honest
page about what the handbook does not cover.

## Writing a company page

- **Start with an `H1`.** It becomes the source title.
- **Use `H2` for each distinct question.** The chunker emits one chunk per `H2`; an `H3` stays
  inside its parent unless the page is listed in `PROMOTE_H3`.
- **Put facts in tables** where they are tabular. Tables are never split across chunks.
- **Write the answer, not the topic.** "Support is by email and carries no guaranteed response time"
  retrieves better than "Support channels".
- **Do not invent numbers.** If a figure is not established, say the handbook does not cover it and
  point at support. An invented price or SLA is worse than an absent one.
- **Use `{{SUPPORT_EMAIL}}`** for the support address. The seeder substitutes it from
  `SUPPORT_EMAIL`/`--support-email` before hashing, so a production address change is a re-seed
  rather than a content edit.

## Adding a company page

1. Add the markdown file to `handbook/company/`.
2. Add a `CompanyPage` entry to `COMPANY_PAGES` in `agent-service/app/handbook/sources.py` with its
   key, title, URL and audience. A page in the manifest but missing from disk fails the seed loudly;
   an unlisted page is simply not indexed.
3. Re-seed with `--source company/<slug>` and, when retiring a page, `--prune`.

## Seeding

```bash
cd agent-service
python scripts/seed_handbook.py --dry-run                      # no HTTP at all
python scripts/seed_handbook.py --token "$INTERNAL_API_TOKEN"  # upsert everything
python scripts/seed_handbook.py --source web-docs/salon --prune
```

Seeding is idempotent: the server skips a chunk whose content hash is unchanged, so re-running
against unchanged sources writes nothing. Re-seeding is a **release step** — an index that lags the
documentation is the most likely way this feature goes quietly wrong.

## Retrieval

Search is hybrid: a dense leg (pgvector cosine over `embedding`) and a lexical leg (PostgreSQL
full-text over the generated `SearchVector`), fused with Reciprocal Rank Fusion. The agent asks for
hybrid; the single-leg modes exist so retrieval quality can be measured per leg. See
`docs/architecture/handbook.md` and ADR-025.
