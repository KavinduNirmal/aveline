# Tests — Agent Service

This folder contains all tests for the Aveline Agent Service.

## Structure

```
tests/
├── agents/     # Tests for each LangGraph agent graph
└── tools/      # Tests for each tool implementation
```

## What belongs here

- **`agents/`** — Tests that run the agent graphs end-to-end (or node-by-node)
  with mocked tools. Verify graph routing, state transitions, and interrupt behavior.
- **`tools/`** — Unit tests for individual tool functions. Mock the DB and external
  services. Test input validation, output shape, and error handling.
- **`conftest.py`** (root of tests) — Shared pytest fixtures (mock DB session,
  mock `httpx` client, sample customer data, etc.)

## Running tests

```bash
# From agnet-service/ root:
pytest tests/ -v
```

## What does NOT belong here

- Integration tests against a real database (mark these with `@pytest.mark.integration`
  and exclude from CI unless a test DB is available)
- Test data fixtures committed as large files (use factories instead)
