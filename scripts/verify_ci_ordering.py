#!/usr/bin/env python3
"""Assert the CI stage ordering contract in ``.github/workflows/ci.yml``.

Why this exists
---------------
The SE3110 delivery requires a fixed pipeline order:

    file-based tests  ->  E2E integration tests  ->  k6 performance tests  ->  application builds

That ordering is expressed entirely with ``needs:``, which means it is exactly the
kind of property that is easy to break by accident: a new build job that forgets
one ``needs`` entry looks fine, lints fine, and quietly ships an artifact from a
run whose tests never passed. Reviewers miss it because the YAML is long.

So the contract is a check, not a comment. This script parses the workflow, asserts
the stage membership and the dependency edges, and exits non-zero on the first
violation. ``ci.yml`` runs it as the last step of the ``hygiene`` job, so a pull
request that breaks the ordering fails before any test job is scheduled.

Usage::

    python3 scripts/verify_ci_ordering.py                 # checks .github/workflows/ci.yml
    python3 scripts/verify_ci_ordering.py path/to/ci.yml

Standard library only; no PyYAML dependency.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
from typing import Any

DEFAULT_WORKFLOW = Path(".github/workflows/ci.yml")

#: Stage 1 — the file-based suites that must pass before anything else runs.
STAGE_1_TESTS = ["test-api", "test-python", "test-web", "test-flutter", "perf-web"]

#: Stage 2 — the E2E integration jobs.
STAGE_2_E2E = ["e2e-browser", "e2e-integration", "test-flutter-integration"]

#: Stage 3 — the performance gate that runs after every E2E job.
STAGE_3_PERFORMANCE = "k6-performance"

#: Stage 4 — jobs that produce a shippable artifact. Each must wait for the
#: performance gate, which itself waits for every E2E job, which wait for every
#: file-based test job.
STAGE_4_BUILDS = ["build-api", "build-web", "build-flutter-apk", "release-ios"]


def parse_jobs(text: str) -> dict[str, dict[str, Any]]:
    """Return ``{job_id: {"needs": [...]}}`` for every job under ``jobs:``.

    Deliberately a small hand parser: the workflow is hand-written and the only
    thing this check needs is the top-level job ids and their ``needs``. Pulling in
    a YAML dependency for that would make the check harder to run, not safer.
    """
    lines = text.splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if re.match(r"^jobs:\s*$", line))
    except StopIteration:
        raise SystemExit("::error::no top-level `jobs:` key found in the workflow")

    jobs: dict[str, dict[str, Any]] = {}
    current: str | None = None

    for index in range(start + 1, len(lines)):
        line = lines[index]

        # A new top-level key ends the jobs block.
        if re.match(r"^[a-zA-Z_]", line):
            break

        job = re.match(r"^  ([A-Za-z0-9_-]+):\s*$", line)
        if job:
            current = job.group(1)
            jobs[current] = {"needs": []}
            continue

        if current is None:
            continue

        scalar = re.match(r"^    needs:\s*([^\[\s].*?)\s*$", line)
        if scalar:
            jobs[current]["needs"] = [scalar.group(1).strip()]
            continue

        inline = re.match(r"^    needs:\s*\[(.*?)\]\s*$", line)
        if inline:
            jobs[current]["needs"] = [n.strip() for n in inline.group(1).split(",") if n.strip()]
            continue

        if re.match(r"^    needs:\s*$", line):
            block: list[str] = []
            for follower in lines[index + 1 :]:
                item = re.match(r"^      - (.+?)\s*$", follower)
                if not item:
                    break
                block.append(item.group(1).strip())
            jobs[current]["needs"] = block

    return jobs


def check(jobs: dict[str, dict[str, Any]]) -> list[str]:
    """Return a list of violations; empty means the contract holds."""
    problems: list[str] = []

    def require(job: str, dependencies: list[str], why: str) -> None:
        if job not in jobs:
            problems.append(f"{job} is missing from the workflow, so the {why} cannot be checked")
            return
        missing = [d for d in dependencies if d not in jobs[job]["needs"]]
        if missing:
            problems.append(f"{job} must depend on {missing} ({why})")

    # Stage 1 must exist; stage 2 must wait for all of it.
    for job in STAGE_1_TESTS:
        if job not in jobs:
            problems.append(f"stage-1 test job {job} is missing from the workflow")
    for job in STAGE_2_E2E:
        require(job, STAGE_1_TESTS, "all file-based tests must pass before any E2E job")

    # Stage 3 must wait for all of stage 2.
    require(STAGE_3_PERFORMANCE, STAGE_2_E2E, "k6 runs after the E2E stage")

    # Stage 4 must wait for the performance gate, which transitively waits for
    # every earlier stage. This is the assertion that the ordering exists to make.
    for job in STAGE_4_BUILDS:
        require(
            job,
            [STAGE_3_PERFORMANCE, "e2e-integration"],
            "no application build may run before every test stage has passed",
        )

    # A build job must not be able to reach the runner without a test: at minimum
    # it must name at least one test job in its own needs.
    for job in STAGE_4_BUILDS:
        if job in jobs and not jobs[job]["needs"]:
            problems.append(f"{job} has no `needs:` at all, so it can run before the tests")

    return problems


def main(argv: list[str]) -> int:
    path = Path(argv[1]) if len(argv) > 1 else DEFAULT_WORKFLOW
    if not path.is_file():
        print(f"::error::workflow not found: {path}", file=sys.stderr)
        return 1

    jobs = parse_jobs(path.read_text(encoding="utf-8"))
    problems = check(jobs)

    print(f"Parsed {len(jobs)} jobs from {path}.")
    print(f"  stage 1 (file-based tests): {', '.join(STAGE_1_TESTS)}")
    print(f"  stage 2 (E2E integration):  {', '.join(STAGE_2_E2E)}")
    print(f"  stage 3 (performance):      {STAGE_3_PERFORMANCE}")
    print(f"  stage 4 (builds):           {', '.join(STAGE_4_BUILDS)}")

    if problems:
        print()
        for problem in problems:
            print(f"::error::CI ordering violation: {problem}")
        return 1

    print()
    print("CI ordering contract holds: file tests -> E2E -> k6 -> builds.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
