# DB — Database Layer

This folder manages the PostgreSQL connection, SQLAlchemy models, and session lifecycle.

## What belongs here

- **`connection.py`** — Async SQLAlchemy engine and `AsyncSessionLocal` factory.
  All DB sessions used by tools are created here.
- **`models/`** — SQLAlchemy ORM model classes (one per domain table). These mirror
  the EF Core models in `Aveline.Api` — the schema is the same database.
- **`repositories/`** — (Optional) Python repository classes for complex queries,
  especially the pgvector similarity search on `Customer_Memory.embedding`.

## What does NOT belong here

- Business logic (goes in `app/services/`)
- FastAPI route logic (goes in `app/api/`)
- Migration scripts — migrations are managed by EF Core on the ASP.NET Core side.
  The Python service is a **read/write consumer** of the database, not the migration owner.

## pgvector

The `Customer_Memory` table has an `embedding` column of type `vector(1536)`.
Use the `pgvector` Python package to query it:
```python
from pgvector.sqlalchemy import Vector
# Then in the model:
embedding = Column(Vector(1536))
```

Similarity search uses the `<=>` cosine distance operator.

## Session management

Use `async with AsyncSessionLocal() as session` in tool functions or repository methods.
Never hold a session open across an `await` boundary at the API layer.
