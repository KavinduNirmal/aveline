#!/usr/bin/env python3
"""Summarise DeepSeek Harness (DSH) session logs for one workspace.

DSH keeps one directory per session under

    $DSH_HOME/sessions/--<workspace-slug>--/<session-id>/session.v3.jsonl.zstd

where the slug is the absolute workspace path with ``/`` replaced by ``-``. Each
file is a zstd-compressed JSONL stream of typed records: ``session``,
``user/message``, ``assistant/message``, ``tool/call``, ``tool/result``,
``session/title``, ``request/context``, and others. This script reads those files
and prints a Markdown summary; it never writes to the session store.

Usage
-----
    python3 scripts/summarize_dsh_sessions.py
    python3 scripts/summarize_dsh_sessions.py --workspace /path/to/workspace
    python3 scripts/summarize_dsh_sessions.py --since 2026-09-01
    python3 scripts/summarize_dsh_sessions.py --out docs/ai-usage/dsh-session-summary.md
    python3 scripts/summarize_dsh_sessions.py --json /tmp/sessions.json

Requires the ``zstd`` command-line tool (``zstdcat`` is used for streaming).
Only the Python standard library is used otherwise.

Caveats worth knowing before you quote a number from the output
---------------------------------------------------------------
* Token counts are summed per request, which is how the provider counts them: a
  request re-reads the conversation, so the same context is counted again every
  time it is sent. The per-request identity
  ``totalTokens == inputTokens + cacheReadTokens + outputTokens`` holds for every
  assistant message in the store, so the sums reconcile with a provider usage
  dashboard. ``--json`` carries per-session sums; compare them against the
  provider console rather than against the size of the log files.
* Only sessions that ran on *this machine under this DSH home* are visible.
  Work done in another editor or by another team member is not here.
* A session that is still open keeps growing, so a summary taken while it runs
  reports that session mid-flight.
* ``user/message`` records include prompts injected by the harness itself
  (runtime context, the skill catalog). Only records whose ``source.kind`` is
  ``user`` are counted as human prompts.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from collections import Counter
from collections.abc import Iterator
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

# Records whose payload is large and irrelevant to a summary. Skipping them by a
# substring test keeps the JSON parse off the hot path: tool results dominate the
# byte count of a session file.
SKIP_TYPES = ("tool/result",)

PROMPT_PREVIEW = 110


def workspace_slug(workspace: Path) -> str:
    """Return the DSH session-directory name for a workspace path."""
    return "--" + str(workspace).strip("/").replace("/", "-") + "--"


def sessions_root(home: Path, workspace: Path) -> Path:
    return home / "sessions" / workspace_slug(workspace)


def iter_records(path: Path) -> Iterator[dict[str, Any]]:
    """Yield parsed records from one session file, skipping bulky payloads."""
    try:
        proc = subprocess.Popen(
            ["zstdcat", "--", str(path)],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    except FileNotFoundError as exc:  # zstd not installed
        raise SystemExit("zstd is required: install the 'zstd' package") from exc

    assert proc.stdout is not None
    with proc.stdout:
        for line in proc.stdout:
            if not line.startswith("{"):
                continue
            if SKIP_TYPES and any(f'"type":"{t}"' in line[:64] for t in SKIP_TYPES):
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                continue
    proc.wait()


def text_of(content: Any) -> str:
    """Flatten a message content field into plain text."""
    if isinstance(content, str):
        return content
    if not isinstance(content, list):
        return ""
    parts: list[str] = []
    for block in content:
        if isinstance(block, dict) and block.get("type") == "text":
            parts.append(str(block.get("text", "")))
    return "\n".join(parts)


def one_line(text: str, limit: int = PROMPT_PREVIEW) -> str:
    flat = " ".join(text.split())
    if len(flat) <= limit:
        return flat
    return flat[: limit - 1].rstrip() + "…"


def summarise_session(session_dir: Path) -> dict[str, Any] | None:
    """Read one session directory into a summary record."""
    log = session_dir / "session.v3.jsonl.zstd"
    if not log.exists():
        return None

    summary: dict[str, Any] = {
        "session_id": session_dir.name,
        "title": None,
        "created_at": None,
        "last_at": None,
        "cwd": None,
        "agent_preset": None,
        "origin": None,
        "parent_session": None,
        "delegation_depth": 0,
        "provider": None,
        "model": None,
        "prompts": 0,
        "assistant_messages": 0,
        "turns": 0,
        "steps": 0,
        "tool_calls": 0,
        "tool_names": Counter(),
        "subagents": 0,
        "input_tokens": 0,
        "output_tokens": 0,
        "reasoning_tokens": 0,
        # These are all per-request quantities and are summed. The identity
        # totalTokens == inputTokens + cacheReadTokens + outputTokens holds for
        # every assistant message in the store, which is what makes the sum a
        # figure comparable with a provider's own usage dashboard.
        "cache_read_tokens": 0,
        "total_tokens": 0,
        # The largest prompt (fresh + cache read) any single request in the
        # session reached: the context high-water mark, not a token total.
        "peak_context_tokens": 0,
        "first_prompt": "",
        "first_prompt_at": None,
    }

    for record in iter_records(log):
        kind = record.get("type")
        data = record.get("data") or {}
        when = record.get("time")

        if when and (summary["last_at"] is None or when > summary["last_at"]):
            summary["last_at"] = when

        if kind == "session":
            summary["created_at"] = record.get("createdAt") or when
            summary["cwd"] = record.get("cwd")
            summary["agent_preset"] = record.get("agentPreset")
            summary["origin"] = record.get("origin") or "top"
            summary["parent_session"] = record.get("parentSession")
            summary["delegation_depth"] = int(record.get("delegationDepth") or 0)
        elif kind == "session/title":
            title = data.get("title")
            source = (data.get("source") or {}).get("kind")
            # A provider-generated title beats the truncated first-prompt fallback.
            if title and (summary["title"] is None or source == "provider"):
                summary["title"] = title
        elif kind == "request/context":
            summary["provider"] = data.get("provider") or summary["provider"]
            summary["model"] = data.get("model") or summary["model"]
        elif kind == "user/message":
            if (data.get("source") or {}).get("kind") == "user":
                summary["prompts"] += 1
                if not summary["first_prompt"]:
                    summary["first_prompt"] = one_line(text_of(data.get("content")))
                    summary["first_prompt_at"] = when
        elif kind == "assistant/message":
            summary["assistant_messages"] += 1
            usage = data.get("usage") or {}
            fresh = int(usage.get("inputTokens") or 0)
            cached = int(usage.get("cacheReadTokens") or 0)
            written = int(usage.get("outputTokens") or 0)
            summary["input_tokens"] += fresh
            summary["output_tokens"] += written
            summary["reasoning_tokens"] += int(usage.get("reasoningTokens") or 0)
            summary["cache_read_tokens"] += cached
            summary["total_tokens"] += int(usage.get("totalTokens") or 0)
            summary["peak_context_tokens"] = max(
                summary["peak_context_tokens"], fresh + cached
            )
        elif kind == "tool/call":
            summary["tool_calls"] += 1
            name = data.get("name") or "unknown"
            summary["tool_names"][name] += 1
        elif kind == "turn/start":
            summary["turns"] += 1
        elif kind == "step/start":
            summary["steps"] += 1
        elif kind == "subagent/descriptor":
            summary["subagents"] += 1

    summary["tool_names"] = dict(summary["tool_names"])
    if summary["created_at"] is None:
        summary["created_at"] = summary["last_at"]
    if not summary["title"]:
        summary["title"] = one_line(summary["first_prompt"]) or session_dir.name
    return summary


def collect(root: Path, since: datetime | None) -> list[dict[str, Any]]:
    if not root.is_dir():
        raise SystemExit(
            f"no session directory for this workspace:\n  {root}\n"
            "check --workspace and --home, or run once in that workspace"
        )
    out: list[dict[str, Any]] = []
    for session_dir in sorted(root.iterdir()):
        if not session_dir.is_dir():
            continue
        record = summarise_session(session_dir)
        if record is None:
            continue
        created = record.get("created_at")
        if since is not None and created is not None:
            if datetime.fromtimestamp(created / 1000, tz=UTC) < since:
                continue
        out.append(record)
    out.sort(key=lambda r: r.get("created_at") or 0)
    return out


def fmt_time(ms: int | None) -> str:
    if not ms:
        return "—"
    return datetime.fromtimestamp(ms / 1000).strftime("%Y-%m-%d %H:%M")


def fmt_day(ms: int | None) -> str:
    if not ms:
        return "—"
    return datetime.fromtimestamp(ms / 1000).strftime("%Y-%m-%d")


def fmt_duration(start: int | None, end: int | None) -> str:
    if not start or not end or end < start:
        return "—"
    minutes = (end - start) / 60000
    if minutes < 60:
        return f"{minutes:.0f}m"
    return f"{minutes / 60:.1f}h"


def fmt_int(value: int) -> str:
    return f"{value:,}"


def fmt_tokens(value: float) -> str:
    """Compact token figure: 24.7M, 155.7M, 4.89B."""
    if value >= 1e9:
        return f"{value / 1e9:.2f}B"
    if value >= 1e6:
        return f"{value / 1e6:.2f}M"
    if value >= 1e3:
        return f"{value / 1e3:.1f}k"
    return f"{value:.0f}"


def mean(values: list[int]) -> float:
    return sum(values) / len(values) if values else 0.0


def percentile(values: list[int], fraction: float) -> float:
    """Nearest-rank percentile over an already sorted list."""
    if not values:
        return 0.0
    index = min(len(values) - 1, int(len(values) * fraction))
    return float(values[index])


def render(sessions: list[dict[str, Any]], meta: dict[str, Any]) -> str:
    lines: list[str] = []
    add = lines.append

    total_tools = sum(s["tool_calls"] for s in sessions)
    total_output = sum(s["output_tokens"] for s in sessions)
    total_input = sum(s["input_tokens"] for s in sessions)
    total_cache = sum(s["cache_read_tokens"] for s in sessions)
    total_tokens = sum(s["total_tokens"] for s in sessions)
    peak_context = max((s["peak_context_tokens"] for s in sessions), default=0)
    top_level = [s for s in sessions if s["origin"] != "subagent"]
    subagents = [s for s in sessions if s["origin"] == "subagent"]
    tools = Counter()
    models = Counter()
    presets = Counter()
    for s in sessions:
        tools.update(s["tool_names"])
        if s["model"]:
            models[f'{s["provider"] or "?"} / {s["model"]}'] += 1
        if s["agent_preset"]:
            presets[s["agent_preset"]] += 1

    days = sorted({fmt_day(s["created_at"]) for s in sessions if s["created_at"]})

    add("# DSH session summary")
    add("")
    add(f"**Workspace:** `{meta['workspace']}`  ")
    add(f"**Session store:** `{meta['root']}`  ")
    add(f"**Generated:** {meta['generated']} by `scripts/summarize_dsh_sessions.py`  ")
    add(f"**Scope:** {len(sessions)} sessions"
        + (f", {days[0]} to {days[-1]}" if days else ""))
    add("")
    add("> These figures come from the DeepSeek Harness session store on this "
        "machine only. Work done in another editor, or on another machine, is not "
        "counted here. The session you run this in is still being written, so its "
        "own row is a snapshot taken mid-session.")
    add("")

    add("## Totals")
    add("")
    add("| Measure | Value |")
    add("|---|---|")
    add(f"| Top-level sessions | {fmt_int(len(top_level))} |")
    add(f"| Subagent sessions | {fmt_int(len(subagents))} |")
    add(f"| Days with activity | {fmt_int(len(days))} |")
    add(f"| Human prompts (top-level) | {fmt_int(sum(s['prompts'] for s in top_level))} |")
    add(f"| Tool calls | {fmt_int(total_tools)} |")
    add(f"| Total tokens (all requests) | {fmt_int(total_tokens)} |")
    add(f"| — fresh input tokens | {fmt_int(total_input)} |")
    add(f"| — cache-read tokens | {fmt_int(total_cache)} |")
    add(f"| — output tokens | {fmt_int(total_output)} |")
    add(f"| Largest single request | {fmt_int(peak_context)} |")
    add("")
    add("Token figures are summed per request, which is how a provider counts "
        "them: each request re-reads the conversation, so the context is counted "
        "again every time it is sent, once as a cache read and once as fresh "
        "input. The identity total = input + cache + output holds exactly, and "
        "the three components above reconcile to the total. Largest single "
        "request is a context high-water mark, not a total.")
    add("")

    add("## Distribution across sessions")
    add("")
    add("Per-session spread matters if you intend to extrapolate from this store "
        "to work it does not cover. These figures are the reason a single "
        "per-session average should not be multiplied by a count of sessions "
        "recorded somewhere else.")
    add("")
    add("| Session group | Sessions | Mean | Median | p90 | Max |")
    add("|---|---|---|---|---|---|")
    for label, group in (
        ("Top-level, total tokens", top_level),
        ("Subagent, total tokens", subagents),
        ("Top-level, output tokens", top_level),
        ("Subagent, output tokens", subagents),
    ):
        field = "total_tokens" if "total tokens" in label else "output_tokens"
        values = sorted(s[field] for s in group)
        cells = (
            fmt_int(len(values)),
            fmt_tokens(mean(values)),
            fmt_tokens(percentile(values, 0.5)),
            fmt_tokens(percentile(values, 0.9)),
            fmt_tokens(values[-1] if values else 0),
        )
        add(f"| {label} | " + " | ".join(cells) + " |")
    add("")

    if models:
        add("## Models")
        add("")
        add("| Provider / model | Sessions |")
        add("|---|---|")
        for name, count in models.most_common():
            add(f"| {name} | {fmt_int(count)} |")
        add("")

    if presets:
        add("## Agent presets")
        add("")
        add("| Preset | Sessions |")
        add("|---|---|")
        for name, count in presets.most_common():
            add(f"| `{name}` | {fmt_int(count)} |")
        add("")

    add("## Tools used")
    add("")
    if tools:
        add("| Tool | Calls |")
        add("|---|---|")
        for name, count in tools.most_common():
            add(f"| `{name}` | {fmt_int(count)} |")
    else:
        add("No tool calls recorded.")
    add("")

    add("## Sessions by day")
    add("")
    add("| Day | Top-level | Subagents | Prompts | Tool calls | Output tokens |")
    add("|---|---|---|---|---|---|")
    by_day: dict[str, list[dict[str, Any]]] = {}
    for s in sessions:
        by_day.setdefault(fmt_day(s["created_at"]), []).append(s)
    for day in sorted(by_day):
        group = by_day[day]
        add(
            "| {day} | {n} | {sub} | {p} | {t} | {o} |".format(
                day=day,
                n=fmt_int(sum(1 for g in group if g["origin"] != "subagent")),
                sub=fmt_int(sum(1 for g in group if g["origin"] == "subagent")),
                p=fmt_int(sum(g["prompts"] for g in group)),
                t=fmt_int(sum(g["tool_calls"] for g in group)),
                o=fmt_int(sum(g["output_tokens"] for g in group)),
            )
        )
    add("")

    add("## Session index")
    add("")
    add(
        "| Started | Kind | Title | Prompts | Tools | Output tokens | Duration |"
    )
    add("|---|---|---|---|---|---|---|")
    for s in sorted(sessions, key=lambda r: r.get("created_at") or 0, reverse=True):
        title = (s["title"] or "").replace("|", "\\|")
        kind = "top" if s["origin"] != "subagent" else f"sub +{s['delegation_depth']}"
        add(
            "| {when} | {kind} | {title} | {p} | {t} | {o} | {d} |".format(
                when=fmt_time(s["created_at"]),
                kind=kind,
                title=title,
                p=fmt_int(s["prompts"]),
                t=fmt_int(s["tool_calls"]),
                o=fmt_int(s["output_tokens"]),
                d=fmt_duration(s["created_at"], s["last_at"]),
            )
        )
    add("")

    add("## First prompt of each top-level session")
    add("")
    for s in sorted(top_level, key=lambda r: r.get("created_at") or 0, reverse=True):
        if not s["first_prompt"]:
            continue
        add(f"- **{fmt_time(s['created_at'])} — {s['title']}**: {s['first_prompt']}")
    add("")

    add("## How to re-run")
    add("")
    add("```bash")
    add("python3 scripts/summarize_dsh_sessions.py \\")
    add("    --out docs/ai-usage/dsh-session-summary.md")
    add("```")
    add("")
    add("`--since YYYY-MM-DD` limits the window, `--json FILE` writes the raw "
        "records, and `--workspace` / `--home` override the locations below.")
    add("")
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Summarise DSH session logs for one workspace."
    )
    parser.add_argument(
        "--workspace",
        default=os.getcwd(),
        help="workspace whose sessions to read (default: current directory)",
    )
    parser.add_argument(
        "--home",
        default=os.environ.get("DSH_HOME", str(Path.home() / ".dsh")),
        help="DSH home (default: $DSH_HOME)",
    )
    parser.add_argument("--since", help="only sessions on or after this date (YYYY-MM-DD)")
    parser.add_argument(
        "--top-level",
        action="store_true",
        help="exclude subagent sessions (one entry per human-started session)",
    )
    parser.add_argument("--out", help="write Markdown here instead of stdout")
    parser.add_argument("--json", dest="json_out", help="also write raw records as JSON")
    args = parser.parse_args(argv)

    workspace = Path(args.workspace).resolve()
    home = Path(args.home).expanduser().resolve()
    root = sessions_root(home, workspace)

    since = None
    if args.since:
        since = datetime.strptime(args.since, "%Y-%m-%d").replace(tzinfo=UTC)

    sessions = collect(root, since)
    if args.top_level:
        sessions = [s for s in sessions if s["origin"] != "subagent"]
    meta = {
        "workspace": workspace,
        "root": root,
        "generated": datetime.now().strftime("%Y-%m-%d"),
    }
    markdown = render(sessions, meta)

    if args.out:
        out = Path(args.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(markdown, encoding="utf-8")
        print(f"wrote {out} ({len(sessions)} sessions)", file=sys.stderr)
    else:
        print(markdown)

    if args.json_out:
        Path(args.json_out).write_text(
            json.dumps({"meta": meta, "sessions": sessions}, indent=2, default=str),
            encoding="utf-8",
        )
        print(f"wrote {args.json_out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
