"""Run the eight golden behaviour cases and write a pass-rate report (gap A2).

The SE3110 agentic-AI chapter lists eight named behaviour cases, GC-01..GC-08
(``docs/final_document/se3110/chapters/11-agentic-ai.tex:81-105``, table ``tab:ai-golden``), and
records that "nothing aggregates them" - eight named tests are a checklist, not a measurement.
Each case is carried by a real pytest function, tagged with the ``golden_behaviour`` marker. This
runner selects exactly those node ids, runs them through ``pytest.main()``, and writes the result as
JSON with a pass rate, so the checklist becomes a number a reviewer (or CI) can gate on.

Usage::

    cd agent-service
    .venv/bin/python scripts/run_behaviour_cases.py                 # writes reports/behaviour-cases.json
    .venv/bin/python scripts/run_behaviour_cases.py --out /tmp/gc.json
    .venv/bin/python scripts/run_behaviour_cases.py --stdout        # also print the JSON

The exit code is non-zero when any case fails **or** does not run, so a CI step can gate on it
without parsing the file. No third-party dependency is added: the report is collected by a tiny
in-process pytest plugin implementing ``pytest_runtest_logreport``.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import pytest

#: The marker the eight cases carry (registered in ``pyproject.toml``).
MARKER = "golden_behaviour"

#: The agent-service root, so the runner works from any working directory.
SERVICE_ROOT = Path(__file__).resolve().parent.parent

#: The default artefact. ``agent-service/reports/`` is a generated-output directory (see the
#: ``.gitignore`` beside it): the JSON is evidence a CI job uploads, not a source file to commit.
DEFAULT_OUT = SERVICE_ROOT / "reports" / "behaviour-cases.json"

#: GC-01..GC-08 mapped to the node ids that carry them, transcribed from ``tab:ai-golden``.
#:
#: GC-02 and GC-04 are named by *file* in the table, so each named file contributes its best
#: single carrier (the supervisor's typed plan; the closed response envelope; a schema violation;
#: a coercion that fails closed). GC-07 names three functions, and all three are listed. Every
#: entry is asserted present in the table's own text - nothing here is invented.
CASES: dict[str, dict[str, Any]] = {
    "GC-01": {
        "property": "Planning and delegation: an unrecognised agent name is dropped, not executed",
        "nodes": [
            "tests/test_supervisor.py::test_drops_unknown_agent_names_but_keeps_valid_ones",
        ],
    },
    "GC-02": {
        "property": "Structured planning: the supervisor emits a typed plan with named, "
        "allow-listed steps",
        "nodes": [
            "tests/test_supervisor.py::test_reads_the_routing_flags",
            "tests/test_response_schema.py::test_agent_response_rejects_extra_fields",
        ],
    },
    "GC-03": {
        "property": "Allow-listed tool use with validated inputs: each specialist reaches only "
        "its registered client",
        "nodes": [
            "tests/test_tool_registry.py::test_registry_calculate_margin",
        ],
    },
    "GC-04": {
        "property": "Structured output: a schema violation fails once rather than returning a "
        "partial result",
        "nodes": [
            "tests/test_response_schema.py::test_agent_output_rejects_invalid_status",
            "tests/test_output_coercion.py::test_invalid_status_returns_none",
        ],
    },
    "GC-05": {
        "property": "Deterministic validation: a margin below the floor is refused by code, not "
        "by the model",
        "nodes": [
            "tests/test_commerce_tools.py::TestCommerceTools"
            "::test_validate_business_rules_low_margin_breached",
        ],
    },
    "GC-06": {
        "property": "Business rules: exhausted stock raises a sourcing request, not an order line",
        "nodes": [
            "tests/agents/test_visual_insight_graph.py::test_visual_insight_graph_out_of_stock_sourcing",
        ],
    },
    "GC-07": {
        "property": "Approval enforcement: an over-threshold purchase pauses, settles or cancels",
        "nodes": [
            "tests/test_hitl_resume.py::test_a_purchase_over_the_threshold_pauses_for_approval",
            "tests/test_hitl_resume.py::test_an_approval_settles_the_deal",
            "tests/test_hitl_resume.py::test_a_rejection_cancels_the_deal",
        ],
    },
    "GC-08": {
        "property": "Safe failure: a denied image analysis ends the run without a business write",
        "nodes": [
            "tests/agents/test_visual_insight_graph.py"
            "::test_analyze_image_node_surfaces_a_denied_analysis_distinctly",
        ],
    },
}


def registered_node_ids() -> list[str]:
    """Every node id the eight cases name, in GC order."""
    return [node for case in CASES.values() for node in case["nodes"]]


def _case_for_nodeid(nodeid: str) -> str | None:
    for case_id, case in CASES.items():
        for node in case["nodes"]:
            if nodeid == node or nodeid.endswith(node):
                return case_id
    return None


class _OutcomeCollector:
    """A minimal pytest plugin: one outcome per test node, keyed by node id.

    ``when == "call"`` is the test body. A setup/teardown failure or a skip has no call report, so
    it is recorded from the phase that produced it - that way a test that never ran is a failure in
    the report rather than a silent absence.
    """

    def __init__(self) -> None:
        self.outcomes: dict[str, str] = {}

    def pytest_runtest_logreport(self, report) -> None:  # noqa: ANN001 - pytest hook signature
        if report.when == "call" or report.outcome != "passed":
            self.outcomes[report.nodeid] = report.outcome
            return
        self.outcomes.setdefault(report.nodeid, report.outcome)


def build_report(outcomes: dict[str, str]) -> dict[str, Any]:
    """Shape the collected outcomes into the tracked artefact."""
    cases: list[dict[str, Any]] = []
    total = 0
    passed = 0
    failed = 0

    for case_id, case in CASES.items():
        nodes = []
        for node in case["nodes"]:
            outcome = next(
                (
                    value
                    for nodeid, value in outcomes.items()
                    if nodeid == node or nodeid.endswith(node)
                ),
                "not_run",
            )
            total += 1
            if outcome == "passed":
                passed += 1
            else:
                failed += 1
            nodes.append({"nodeId": node, "outcome": outcome})
        cases.append({"case": case_id, "property": case["property"], "nodes": nodes})

    return {
        "generatedAt": datetime.now(UTC).isoformat(timespec="seconds"),
        "marker": MARKER,
        "total": total,
        "passed": passed,
        "failed": failed,
        "passRate": round(passed / total, 4) if total else 0.0,
        "cases": cases,
    }


def run(node_ids: list[str], collector: _OutcomeCollector) -> int:
    """Run exactly ``node_ids`` in-process and return pytest's exit code."""
    # The runner may be invoked from anywhere; pytest resolves ``testpaths``/``pythonpath`` against
    # the rootdir, which is agent-service.
    os.chdir(SERVICE_ROOT)
    return int(
        pytest.main(
            ["-q", "-p", "no:cacheprovider", "--no-header", *node_ids],
            plugins=[collector],
        )
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--out",
        type=Path,
        default=DEFAULT_OUT,
        help=f"where to write the JSON report (default: {DEFAULT_OUT})",
    )
    parser.add_argument(
        "--stdout",
        action="store_true",
        help="also print the JSON report to stdout",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="suppress the one-line summary",
    )
    args = parser.parse_args(argv)

    node_ids = registered_node_ids()
    collector = _OutcomeCollector()
    exit_code = run(node_ids, collector)

    report = build_report(collector.outcomes)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    if args.stdout:
        print(json.dumps(report, indent=2))

    if not args.quiet:
        print(
            f"{report['passed']}/{report['total']} golden behaviour cases passed "
            f"(pass rate {report['passRate']:.2%}); report written to {args.out}"
        )

    return 0 if report["failed"] == 0 and exit_code == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
