#!/usr/bin/env python3
"""Predict total AI token usage for work the Harness store cannot see.

The DeepSeek Harness records exact token usage for its own sessions. The
hand-written logs in ``docs/ai-usage/`` record work episodes for other tools
(Antigravity, opencode) with no token figures at all. This script produces a
*guestimation* for those episodes, with an explicit interval, by treating the
Harness store as a calibration sample.

Model
-----
1. Every session in the store is folded into an **episode**: a top-level session
   plus the subagent sessions it spawned, however deep. An episode is the unit
   compared against one logged work episode.
2. Each unmeasured log entry draws ``k`` episodes, ``k ~ Poisson(mode)`` clipped
   to at least one, so ``--sessions-per-entry`` is the mean and the measured
   ratio can be passed straight in.
3. Each episode draws its token cost, with replacement, from the observed
   episode-cost distribution.
4. Draws are summed over every entry, ``--trials`` times. The median is the
   point estimate; the 2.5 / 97.5 percentiles form a 95 % prediction interval.

The result is a **prediction, not a measurement**. Its width is the honest part
of it, and the sensitivity table at the end shows which assumption dominates.
Read ``docs/ai-usage/token-usage-analysis.md`` before quoting anything it prints.

Usage
-----
    python3 scripts/predict_ai_usage.py --entries Dilud=82 --entries Kaveesha=13
    python3 scripts/predict_ai_usage.py --entries Dilud=82 --entries Kaveesha=13 \\
        --sessions-per-entry 1 --comparability 0.5 --cache-hit 0.95

On the cache-hit flag: it does **not** change predicted volume. The prompt tokens
a request sends are fixed by the tool's context policy; a cache hit only decides
how many of them are recorded as a cache read rather than as fresh input. The
flag therefore re-splits the predicted total, and it is the fresh-input share
that carries cost. A lower hit rate means more tokens billed at the full rate for
the same amount of work.
"""

from __future__ import annotations

import argparse
import importlib.util
import math
import random
import statistics as st
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load_summariser():
    """Import summarize_dsh_sessions.py from this directory."""
    spec = importlib.util.spec_from_file_location(
        "summarize_dsh_sessions", HERE / "summarize_dsh_sessions.py"
    )
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def episode_costs(sessions: list[dict]) -> list[int]:
    """Fold sessions into episodes and return each episode's token cost.

    An episode is a top-level session plus every session descending from it,
    however deep. A subagent's tokens are not part of its parent's own total in
    the store, so they are added here.
    """
    own = {s["session_id"]: s["total_tokens"] for s in sessions}
    parent = {s["session_id"]: s["parent_session"] for s in sessions}

    children: dict[str, list[str]] = {}
    for sid, pid in parent.items():
        if pid and pid in own:
            children.setdefault(pid, []).append(sid)

    def cost(sid: str) -> int:
        return own.get(sid, 0) + sum(cost(c) for c in children.get(sid, []))

    return [cost(s["session_id"]) for s in sessions if s["origin"] != "subagent"]


def poisson(lam: float, rng: random.Random) -> int:
    """Knuth's method; lambda here is small (single digits)."""
    limit = math.exp(-lam)
    k, p = 0, 1.0
    while True:
        p *= rng.random()
        if p <= limit:
            return k
        k += 1


def predict(
    pool: list[int],
    entries: int,
    trials: int,
    sessions_per_entry: float,
    comparability: float,
    rng: random.Random,
) -> list[float]:
    """Monte Carlo over entries: episodes per entry, then cost per episode."""
    totals: list[float] = []
    for _ in range(trials):
        total = 0.0
        for _ in range(entries):
            k = max(1, poisson(sessions_per_entry, rng))
            for _ in range(k):
                total += rng.choice(pool) * comparability
        totals.append(total)
    return totals


def quantile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--entries",
        action="append",
        required=True,
        metavar="NAME=N",
        help="unmeasured log entries, repeatable (e.g. Dilud=82)",
    )
    parser.add_argument("--workspace", default=str(Path.cwd()))
    parser.add_argument("--home", default=str(Path.home() / ".dsh"))
    parser.add_argument(
        "--sessions-per-entry",
        type=float,
        default=3.0,
        help="mean episodes per logged entry (measured ratio is about 3)",
    )
    parser.add_argument(
        "--comparability",
        type=float,
        default=1.0,
        help="scale factor: how heavy another tool's episode is versus this store",
    )
    parser.add_argument("--cache-hit", type=float, default=0.95)
    parser.add_argument("--trials", type=int, default=20000)
    parser.add_argument("--seed", type=int, default=20260922)
    args = parser.parse_args(argv)

    named = []
    for item in args.entries:
        name, _, count = item.partition("=")
        named.append((name, int(count)))
    entries = sum(n for _, n in named)

    summariser = load_summariser()
    workspace = Path(args.workspace).resolve()
    home = Path(args.home).expanduser().resolve()
    sessions = summariser.collect(summariser.sessions_root(home, workspace), None)
    if not sessions:
        raise SystemExit(f"no sessions found for {workspace}")

    fmt = summariser.fmt_tokens
    pool = episode_costs(sessions)
    measured = sum(s["total_tokens"] for s in sessions)
    cached_measured = sum(s["cache_read_tokens"] for s in sessions)
    input_measured = sum(s["input_tokens"] for s in sessions)
    observed_hit = cached_measured / (cached_measured + input_measured)

    print("=" * 74)
    print("AI usage prediction for unmeasured work episodes")
    print("=" * 74)
    print(f"Calibration store : {workspace}")
    print(f"Measured total    : {fmt(measured)} over {len(sessions)} sessions in "
          f"{len(pool)} episodes")
    print(f"Episode cost pool : median {fmt(st.median(pool))}, "
          f"mean {fmt(st.mean(pool))}, max {fmt(max(pool))}")
    print(f"Observed cache hit: {observed_hit:.1%}")
    print()
    print("Unmeasured entries:")
    for name, count in named:
        print(f"  {name:14s} {count:4d}")
    print(f"  {'total':14s} {entries:4d}")
    print()

    rng = random.Random(args.seed)
    totals = predict(
        pool, entries, args.trials, args.sessions_per_entry, args.comparability, rng
    )
    lo, mid, hi = (
        quantile(totals, 0.025),
        st.median(totals),
        quantile(totals, 0.975),
    )

    print(f"Base case: {args.sessions_per_entry:g} episodes per entry (mean), "
          f"comparability {args.comparability:g}")
    print("-" * 74)
    print(f"{'':24s} {'low (2.5%)':>15s} {'median':>15s} {'high (97.5%)':>15s}")
    print("-" * 74)
    print(f"{'Unmeasured tools':24s} {fmt(lo):>15s} {fmt(mid):>15s} {fmt(hi):>15s}")
    print(f"{'Measured (Harness)':24s} {fmt(measured):>15s} {fmt(measured):>15s} "
          f"{fmt(measured):>15s}")
    print(f"{'Projected total':24s} {fmt(lo + measured):>15s} "
          f"{fmt(mid + measured):>15s} {fmt(hi + measured):>15s}")
    print("-" * 74)
    print()

    print("Sensitivity of the median (each row re-runs the model):")
    print()
    print(f"{'episodes/entry':>15s} {'comparability':>14s} {'unmeasured':>13s} "
          f"{'projected':>13s}")
    for spe in (1, 2, 3, 5):
        for comp in (0.25, 0.5, 1.0):
            draws = predict(
                pool, entries, args.trials, spe, comp, random.Random(args.seed)
            )
            print(f"{spe:>15g} {comp:>14g} "
                  f"{fmt(st.median(draws)):>13s} "
                  f"{fmt(st.median(draws) + measured):>13s}")
    print()

    print("Cache-hit sensitivity (base-case median). A cache hit re-splits the")
    print("prompt; it does not change how many tokens the tool sends.")
    print()
    print(f"{'assumed hit rate':>17s} {'fresh input':>15s} {'cache reads':>15s}")
    for rate in (0.90, 0.95, round(observed_hit, 4)):
        print(f"{rate:>17.1%} {fmt(mid * (1 - rate)):>15s} {fmt(mid * rate):>15s}")
    print()
    print("A prediction, not a measurement. The interval and the sensitivity")
    print("table are the honest part of it.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
