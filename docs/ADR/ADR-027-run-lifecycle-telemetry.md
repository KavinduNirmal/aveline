# ADR-027: Run Lifecycle Telemetry — A Run Row Exists While the Run Does

## Status
Accepted

## Context

`agent.runs_running` was a lie, and it had been since the metric was introduced.

The panel labelled "Runs running" plots `aveline_agent_runs_running_count`, which
`SystemMetricCollector` computes as `COUNT(*) FROM AgentWorkflowRuns WHERE Status = 'Running'`. But
`AgentWorkflowRuns` had **no writer that could ever produce a `Running` row**: the agent service
reported a run exactly once, after it had finished, through `_report_usage_best_effort` — and
`usage_reporter`'s own docstring described the payload as "a **complete** agent workflow run and
steps". So every row was created already terminal.

Measured against the local database: **42 runs, every one `Succeeded`**. Not one row in any other
state, across every run the platform had ever recorded.

Everything downstream had been written for the missing half:

- `AgentWorkflowRun.Status` **defaults** to `AgentRunStatus.Running`.
- `AgentRunIngestService.IsTerminal` treats `Running` as non-terminal, and `ValidateRun` explicitly
  permits it (requiring `StartedAt` and forbidding `CompletedAt`).
- `PublishAsync` maps `Running` to `agent.run.started` (or `agent.run.resumed` when it follows a
  pause) — two event types nothing could ever emit.
- `AgentStatisticsService` computes a `running` count in two places.

So the design intent was always a row opened at the start and closed at the end; only the first half
was never wired. The consequence is not cosmetic: a dashboard whose whole purpose is to answer "is
work in flight?" read zero while the agent answered every message, and the reader concluded the
metrics were broken — which is how this was found.

Two adjacent gaps surfaced while establishing this:

1. **There was no activity metric at all.** The exported agent metrics were `runs_running` and
   `paused_count` (instantaneous gauges) plus `success_rate` and `steps_per_run` (ratios). Nothing
   counted runs *over time*, so a healthy system and a dead one looked equally flat.
2. **The stale-run sweep only covered pauses.** `StaleAgentRunJob` reaped `PausedForApproval` runs
   past `PausedRunTimeoutHours`, and nothing else. That was complete while every row was terminal on
   creation — and becomes incomplete the moment a `Running` row can outlive its process.

## Options Considered

### 1. Delete the panel and stop pretending
- **Pros:** honest immediately, no new writes.
- **Cons:** throws away a genuinely useful signal (a stuck run *is* worth alerting on), and leaves
  the dead `Running` scaffolding in place for the next reader to puzzle over.

### 2. Derive "running" from something else — e.g. recent rows without a `CompletedAt`
- **Pros:** no new write.
- **Cons:** there is no such row today either, so it would be a second metric over the same absent
  state. It also cannot distinguish "in flight" from "abandoned".

### 3. Report a `Running` row when the run starts, and add the missing sweep (chosen)
- **Pros:** makes the existing metric, the existing contract, and the existing dead event types
  correct together; a stuck run becomes visible *and* reapable; it is what the ingest service was
  already built to accept.
- **Cons:** one extra HTTP write per run, awaited before the workflow starts.

## Decision

### 1. The agent opens the run row before it works
`agents_query` posts a `Running` report — no steps, no completion, the collector's `start_utc` —
before calling `run_concierge`. The completion report then upserts the same row, because ingest keys
on `WorkflowId`.

### 2. The start report is awaited, not fired and forgotten
It is a deliberate blocking call with a **short** timeout (2 s, against the completion report's
10 s). Fire-and-forget was rejected for a specific reason: a start report that lost the race to the
completion report would arrive at an already-terminal row, and `IngestAsync` would refuse it as a
conflict. `Report_StartedAfterTheRunHasFinished_IsRefusedRatherThanReopeningIt` pins that the API
refuses it — which is the *correct* behaviour, and exactly why the ordering must be deterministic
instead of lucky. A failure is logged and swallowed: telemetry never fails a query.

### 3. A `Running` row must be reapable, or the gauge sticks
The sweep now reaps two kinds of abandonment:

| Kind | Cutoff | `ErrorCode` |
|---|---|---|
| Paused on an approval nobody answered | `PausedAt < now - PausedRunTimeoutHours` (72) | `approval_timeout` |
| A process that died before reporting | `StartedAt < now - RunningRunTimeoutHours` (1) | `run_abandoned` |

The distinct error codes matter: an operator reading the row must be able to tell a dead process
from an unanswered question. Without this arm a crash would hold `runs_running` above zero forever —
a permanent false alarm on the one panel that is supposed to mean something.

### 4. A cumulative counter supplies the view the gauges cannot
`aveline.agent.runs_total` = terminal runs, bridged as a Prometheus counter, with a "Runs completed"
panel plotting `increase()` over the interval. It is computed in `SystemMetricCollector` beside the
two counts it complements.

**It is cumulative over the retention window, not forever.** Runs are pruned after
`AgentStats:RunRetentionDays` (400), so the value steps *down* when a day of runs ages out. Prometheus
reads that as a counter reset: `increase()` restarts its accumulation, so the one interval spanning a
prune under-reports and no interval invents a spike. A DB-derived counter was preferred to an
in-process one because the value then survives an API restart.

### 5. The streaming endpoint is left alone, deliberately
`/agents/query/stream` relays `astream_events` and ingests **nothing** — no run, no steps. Adding a
start report there without also adding a terminal one would leave a `Running` row per streamed
request for the sweep to collect an hour later, which is strictly worse than the current silence.
Streaming runs are counted only in-process (`record_stream_run`). Making them first-class is a
separate change with its own cost question.

## Consequences

- **`agent.runs_running` now has a producer**, observed live: a real query showed `Running = 1`
  mid-flight and the row closed as `Succeeded` with its 7 step rows, leaving no orphan.
- **One extra write per run, awaited.** A local HTTP round trip before the workflow starts — a few
  milliseconds on the compose network. The cost is bounded by the 2 s timeout, which is the real
  trade: if the API is degraded, every agent query waits up to that long before the workflow begins.
  Accepted because the alternative is nondeterministic ordering (see Decision 2), and because the
  report is skipped entirely when the request carries no organisation — there would be nothing to
  attribute the row to.
- **Three full-table counts per collector pass** on `AgentWorkflowRuns` (`runs_running`,
  `paused_count`, `runs_total`). The first two already existed, so this adds one. If the table grows
  enough to matter, the daily rollup is the bounded source.
- **A crash mid-run is now visible rather than invisible**: a `Running` row appears immediately and
  turns into `TimedOut`/`run_abandoned` within the hour. That is a new alertable condition, which is
  the point.
- **The dashboard says which question each panel answers.** "Runs running" describes a state and will
  read zero between runs; "Runs completed" describes activity. Reading either as the other is the
  mistake this ADR exists to prevent.

## Related

- [ADR-010](ADR-010-usage-tracking-architecture.md) — the usage record a run links to
- [ADR-014](ADR-014-redis-pubsub-event-bus.md) — the bus carrying `agent.run.started` and friends
- [ADR-024](ADR-024-conversation-orders-and-hitl-resume.md) — the pause this sweep's other arm reaps
- [`docs/backend/statistics-catalog.md`](../backend/statistics-catalog.md) — the metric definitions
