# Load & performance tests (k6)

Two k6 scripts, both **gates** rather than benchmarks. k6 exits non-zero when a
threshold is breached, and the CI job that runs them fails the pipeline on that
exit code.

| Script | What it gates |
|---|---|
| `k6-telemetry-overhead.js` | The telemetry-middleware budget for one measured endpoint against an excluded one (defect `D-11`). |
| `k6-critical-workflows.js` | Three scenarios: `health`, `browse` (balance + catalogue + Salon list) and `salon_write` (create, send, read back). |

`lib/config.js` holds the shared credential/base-URL handling.

## Why there is no default credential

The previous revision defaulted `AUTH_TOKEN` to the literal `test-token` and
`API_BASE_URL` to `http://localhost:5000`, and its check counted **401 as
success**. An unauthenticated run therefore passed. That is the defect the SE3110
gap analysis recorded as "the threshold is decorative". Both scripts now abort at
init time unless a real credential and `ORG_ID` are supplied:

- `AUTH_TOKEN` — a Clerk bearer token, or
- `API_KEY` — an `avl_...` key scoped to the org and to the scopes the measured
  routes need (`billing:view`, `catalog:view`, `conversations:view`).

Set exactly one. Set both and the script refuses to guess which principal the run
is measuring.

## Running locally

The stack must be up and seeded. Either `docker compose up -d postgres redis api agent`,
or the host-run equivalent used during development.

```bash
cd tests/load

# A pre-flight that finishes in seconds.
k6 run -e SMOKE=1 \
       -e API_BASE_URL=http://localhost:5091 \
       -e ORG_ID=<guid> -e API_KEY=avl_... \
       k6-telemetry-overhead.js

# The full run, retaining the raw series and the summary.
k6 run -e API_BASE_URL=http://localhost:5091 \
       -e ORG_ID=<guid> -e API_KEY=avl_... \
       --out json=k6-results.json \
       k6-critical-workflows.js
```

`k6-critical-workflows.js` writes `k6-critical-workflows-summary.json` beside
whatever directory you run it from. Both scripts print the threshold table on
exit; a `✗` next to a threshold means the run failed.

If k6 is not installed, the official image works and needs the host network to
reach a locally published API:

```bash
docker run --rm -i --network host -v "$PWD:/scripts" -w /scripts grafana/k6 \
  run -e API_BASE_URL=http://localhost:5091 -e ORG_ID=<guid> -e API_KEY=avl_... \
  tests/load/k6-telemetry-overhead.js
```

## In CI

Both scripts run in the `k6-performance` job of `.github/workflows/ci.yml`, after
the E2E integration job and before any application build step. That job:

1. brings up the composed stack and waits on `/health/ready`;
2. seeds the org, the API key and the customer (the same seeder the E2E job uses);
3. runs the telemetry gate, then the workflow gate;
4. uploads `results.json` and the summary as the `aveline-k6-performance`
   artefact — the evidence gap N3 said was "Not retained".

`SMOKE=1` is used for pull requests; a push to a stable branch runs the full
stages.

## Budgets

The single source of truth for each number is the script's own `thresholds`
block, and its comment states the same number. If you change a budget, change the
comment in the same commit — the whole point of gap N1 is that the file used to
disagree with itself (`p(99)<100` enforced while the comment claimed a 50 ms
budget).

### Measured against the composed stack, 2026-10-01

Both gates were run to a green exit against a real stack (PostgreSQL + the real
API + the real Python agent), with `SMOKE=1`:

| Endpoint | n | p50 | p95 | Budget |
|---|---:|---:|---:|---|
| `/health/live` (excluded path, control) | 20 | 1.9 ms | 2.8 ms | p95 < 100 ms |
| `/orgs/{id}/billing/burn-rate` (telemetry path) | 879 | 24.8 ms | 32.4 ms | p99 < 100 ms |
| `/orgs/{id}/catalog/items` | 246 | 17.3 ms | 22.6 ms | (browse scenario p95 < 300 ms) |
| `/orgs/{id}/conversations` | 246 | 24.8 ms | 35.9 ms | (browse scenario) |
| `POST …/conversations/{id}/messages` | 3 | 3,801 ms | 3,874 ms | p95 < 6,000 ms |
| `GET …/conversations/{id}/messages` | 3 | 7.0 ms | 8.2 ms | p95 < 500 ms |

The send is slow because it is **synchronous**: the API holds the request open
while the Python agent runs a full LangGraph turn. That number also sizes the
third API↔agent seam the gap plan could not assert without real latency data —
the API's agent `HttpClient` has no explicit timeout, so its bound is the .NET
default of **100 s**, roughly 25× the measured p95. The relationship is recorded
here rather than asserted in a test, because a ratio assertion would be brittle.

