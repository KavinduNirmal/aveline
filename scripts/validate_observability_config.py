#!/usr/bin/env python3
"""Validate the committed observability configuration (metrics plan Slice 1).

The authoritative validator for Prometheus is ``promtool check config`` /
``promtool check rules``, which the ``observability-config`` CI job runs against the real
pinned image. This script is the fast, dependency-light layer that runs first and covers the
things promtool does not: the compose service wiring, the deprecated exporter flags, the
collector pipeline shapes, the empty scrape token, and the Grafana provisioning files once
they exist.

Usage::

    python3 scripts/validate_observability_config.py

Exit code 0 means every check passed; 1 lists the failures.
"""

from __future__ import annotations

import json
import pathlib
import re
import sys

try:
    import yaml
except ImportError:  # pragma: no cover - CI installs PyYAML explicitly.
    print("PyYAML is required: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)

ROOT = pathlib.Path(__file__).resolve().parents[1]
FAILURES: list[str] = []


def check(condition: bool, message: str) -> None:
    if not condition:
        FAILURES.append(message)


def load_yaml(relative: str) -> object:
    path = ROOT / relative
    check(path.exists(), f"{relative} is missing")
    if not path.exists():
        return None
    try:
        return yaml.safe_load(path.read_text())
    except yaml.YAMLError as error:
        FAILURES.append(f"{relative} does not parse: {error}")
        return None


def validate_prometheus() -> None:
    config = load_yaml("observability/prometheus/prometheus.yml")
    if not isinstance(config, dict):
        return

    scrape_configs = config.get("scrape_configs") or []
    check(len(scrape_configs) >= 4, "prometheus.yml must declare the four scrape jobs")

    job_names = {job.get("job_name") for job in scrape_configs}
    for required in ("aveline-api", "aveline-agent", "aveline-postgres", "prometheus"):
        check(required in job_names, f"prometheus.yml is missing job_name {required!r}")

    for job in scrape_configs:
        name = job.get("job_name")
        static = job.get("static_configs") or []
        targets = [target for entry in static for target in (entry.get("targets") or [])]
        check(bool(targets), f"scrape job {name!r} has no targets")

        credentials = (job.get("authorization") or {}).get("credentials_file")
        if credentials:
            # The secret itself is gitignored; the directory must exist so promtool (which stats
            # referenced files) and the compose bind mount have a target.
            check(
                (ROOT / "observability/prometheus/secrets").is_dir(),
                f"scrape job {name!r} references {credentials} but the secrets directory is missing",
            )

    retention = (
        ((config.get("storage") or {}).get("tsdb") or {}).get("retention") or {}
    ).get("time")
    check(
        retention == "15d",
        "retention belongs in prometheus.yml storage.tsdb.retention.time (the CLI flag is deprecated)",
    )

    rules_dir = ROOT / "observability/prometheus/rules"
    check(rules_dir.is_dir(), "observability/prometheus/rules/ is missing")
    check(
        bool(list(rules_dir.glob("*.yml"))) if rules_dir.is_dir() else False,
        "no rule files found; rule_files: /etc/prometheus/rules/*.yml would resolve to nothing",
    )

    validate_rules()


def validate_rules() -> None:
    rules_path = ROOT / "observability/prometheus/rules/aveline.yml"
    if not rules_path.exists():
        FAILURES.append("observability/prometheus/rules/aveline.yml is missing")
        return

    document = load_yaml("observability/prometheus/rules/aveline.yml")
    if not isinstance(document, dict):
        return

    for group in document.get("groups") or []:
        for rule in group.get("rules") or []:
            label = rule.get("alert") or rule.get("record") or "<unnamed>"
            check(bool(rule.get("expr")), f"rule {label!r} has no expr")
            if "alert" in rule:
                check(bool(rule.get("for")), f"alert {label!r} has no for duration")
                check(
                    bool((rule.get("labels") or {}).get("severity")),
                    f"alert {label!r} has no severity label",
                )


def validate_compose() -> None:
    text = (ROOT / "docker-compose.yml").read_text()
    # Strip full-line comments: they deliberately name the banned forms to warn against them.
    code = "\n".join(
        line for line in (raw.strip() for raw in text.splitlines()) if not line.startswith("#")
    )

    for banned in ("--metric-prefix", "--auto-discover-databases", "--disable-settings-metrics"):
        check(banned not in code, f"docker-compose.yml must never set {banned}")
    check(
        "--storage.tsdb.retention" not in code,
        "retention must live in prometheus.yml, not a deprecated CLI flag",
    )
    check("prom/prometheus:v3.5.0" not in code, "Prometheus 3.5 is end of life; pin the LTS line")
    check(
        re.search(r"image:\s*prom/prometheus:v3\.1[34]\.", code) is not None,
        "prom/prometheus must be pinned to 3.13.x (LTS) or 3.14.x",
    )
    check(
        "otel/opentelemetry-collector-contrib:0.161.0" in code,
        "the collector must be pinned, not left on :latest",
    )
    check("${METRICS_SCRAPE_TOKEN:?" in code, "an unset METRICS_SCRAPE_TOKEN must fail the stack")

    compose = load_yaml("docker-compose.yml")
    if isinstance(compose, dict):
        # Slice 6 adds the service; when it is present these constraints apply, and the .NET
        # PostgresExporterConfigTests makes its presence mandatory.
        exporter = (compose.get("services") or {}).get("postgres-exporter")
        if exporter is not None:
            check(not exporter.get("ports"), "postgres-exporter must not publish a host port")
            check(
                (exporter.get("environment") or {}).get("DATA_SOURCE_PASS_FILE"),
                "postgres-exporter credentials must come from a file, not an inline URL",
            )


def validate_collector() -> None:
    config = load_yaml("otel-collector-config.yaml")
    if not isinstance(config, dict):
        return

    pipelines = (config.get("service") or {}).get("pipelines") or {}
    check("traces" in pipelines, "the collector must keep its traces pipeline")
    check("metrics" in pipelines, "the collector needs a metrics pipeline or agent metrics go nowhere")

    exporters = config.get("exporters") or {}
    check("prometheus" in exporters, "the collector needs a prometheus exporter for agent metrics")
    check(
        "resource_to_telemetry_conversion" not in json.dumps(config),
        "resource_to_telemetry_conversion is deprecated; use resource_constant_labels",
    )
    if "prometheus" in exporters:
        prometheus = exporters["prometheus"] or {}
        check(
            "resource_constant_labels" in prometheus,
            "the prometheus exporter must include resource labels via resource_constant_labels",
        )


def validate_env_example() -> None:
    text = (ROOT / ".env.example").read_text()
    check(
        re.search(r"(?m)^METRICS_SCRAPE_TOKEN=\s*$", text) is not None,
        "METRICS_SCRAPE_TOKEN must ship empty in .env.example, never as a default",
    )


def validate_grafana() -> None:
    provisioning = ROOT / "observability/grafana/provisioning"
    dashboards = ROOT / "observability/grafana/dashboards"
    if not provisioning.exists() and not dashboards.exists():
        return  # Slice 3 has not landed yet.

    for relative in (
        "observability/grafana/provisioning/datasources/prometheus.yml",
        "observability/grafana/provisioning/dashboards/aveline.yml",
        "observability/grafana/provisioning/alerting/contactpoints.yml",
        "observability/grafana/provisioning/alerting/notification-policies.yml",
        "observability/grafana/provisioning/alerting/rules.yml",
    ):
        document = load_yaml(relative)
        if isinstance(document, dict):
            check(
                document.get("apiVersion") == 1,
                f"{relative} must declare apiVersion: 1",
            )

    for dashboard in sorted(dashboards.glob("*.json")):
        try:
            json.loads(dashboard.read_text())
        except json.JSONDecodeError as error:
            FAILURES.append(f"dashboard {dashboard.name} is not valid JSON: {error}")


def main() -> int:
    validate_prometheus()
    validate_compose()
    validate_collector()
    validate_env_example()
    validate_grafana()

    if FAILURES:
        print("Observability configuration validation FAILED:")
        for failure in FAILURES:
            print(f"  - {failure}")
        return 1

    print("Observability configuration validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
