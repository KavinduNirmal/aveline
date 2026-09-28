# ---------------------------------------------------------------------------
# Aveline Agent Service — FastAPI + LangGraph (Python 3.12)
# Multi-stage: compile native deps in a builder, ship a tiny alpine runtime.
# ---------------------------------------------------------------------------

FROM python:3.12-alpine AS builder

# Build deps only needed to compile native wheels (asyncpg, psycopg2, etc.).
RUN apk add --no-cache gcc musl-dev python3-dev libffi-dev postgresql-dev

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir --prefix=/install -r requirements.txt

# --- minimal runtime image ------------------------------------------------
FROM python:3.12-alpine

# libpq is required at runtime by psycopg2 / asyncpg.
RUN apk add --no-cache libpq

# Run as an unprivileged user, mirroring the API image (which already runs as
# `app`). The agent only reads its bundled prompt files and talks to PostgreSQL
# and Redis over the network — it never writes to disk — so it never needs root.
RUN addgroup -S app && adduser -S -G app app \
    && mkdir -p /app && chown app:app /app

WORKDIR /app

COPY --from=builder --chown=app:app /install /usr/local
COPY --chown=app:app app ./app

USER app

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
