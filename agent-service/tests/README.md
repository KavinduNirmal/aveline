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
# From agent-service/ root:
pytest tests/ -v

# With the coverage gate CI enforces:
pytest tests/ -q --cov=app --cov-fail-under=90

# The eight named golden behaviour cases only (GC-01..GC-08):
pytest -m golden_behaviour
```

## Golden behaviour cases and the adversarial corpus

- **`test_prompt_injection_corpus.py`** — the A1 adversarial corpus. It asserts *structural*
  invariants (routing cannot be widened, the tool registry has no dynamic dispatch, an
  over-threshold purchase still pauses, an unreadable photo writes nothing) rather than a model's
  politeness. The suite runs with `AGENT_LLM_ENABLED=false`, so read its module docstring before
  quoting a result: it proves the structural defence, **not** a real model's susceptibility to
  injection.
- **`test_customer_update_instruction.py`** — the extraction-layer negatives for the same gap: a
  message that merely contains "update"/"set" must not be read as a write instruction.
- **`golden_behaviour`** — the pytest marker applied to the twelve test functions that carry the
  eight golden behaviour cases of `tab:ai-golden`
  (`docs/final_document/se3110/chapters/11-agentic-ai.tex:81-105`). Run them together with
  `scripts/run_behaviour_cases.py`, which selects exactly those node ids, writes a pass-rate JSON
  report (default `agent-service/reports/behaviour-cases.json`) and exits non-zero when any case
  fails so CI can gate on it.
- **`test_handbook_eval.py`** — the retrieval evaluation's scoring arithmetic, plus the `--out`
  plumbing of `scripts/eval_handbook.py` (exercised against a mocked HTTP transport, so it needs no
  live server).

## What does NOT belong here

- Integration tests against a real database (mark these with `@pytest.mark.integration`
  and exclude from CI unless a test DB is available)
- Test data fixtures committed as large files (use factories instead)
- A model-path injection claim: that needs a separate opt-in suite that enables a real model and
  scores its output against the corpus above.

