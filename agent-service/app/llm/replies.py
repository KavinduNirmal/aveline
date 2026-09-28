"""Recovering plain prose from an LLM completion (shared by the specialist agents).

The universal system prompt (``app/prompts/SYSTEM_PROMPT.md``) instructs every agent to emit its
result as a JSON envelope and never to return free-form text as a final result. That is right for
an agent's structured output, but several calls inside the workflow are *not* structured: a drafting
call, a styling commentary. Those prompts override the instruction, and models do not always comply.

The failure is user-visible rather than loud. A model that answers a commentary call with
``{"status": "success", "output": {..., "styling_notes": {...}}}`` has its reply written into a
display field verbatim, and the Salon renders a wall of JSON to the customer.

So there are two layers of defence, and both are needed:

1. Ask for prose explicitly (the caller's job).
2. Never trust it: unwrap an envelope, and if structured content arrives anyway, extract the prose
   worth showing or fall back. This module is that second layer.
"""

import json
import logging
from typing import Any

logger = logging.getLogger("aveline.agent.llm.replies")

#: Keys an enveloped reply may nest the human-readable text under, most specific first.
_TEXT_KEYS = ("assistant_reply", "draft_response", "reply", "text", "summary", "commentary")

#: Keys that may hold a *body* of prose worth flattening when no single text key matched. Values
#: may be strings or one level of nested string mapping (e.g. ``styling_notes``).
_PROSE_CONTAINER_KEYS = ("styling_notes", "notes", "rationale", "highlights")


def _strip_code_fence(text: str) -> str:
    """Remove a Markdown code fence wrapping the whole reply, including a language tag."""
    if not text.startswith("```"):
        return text

    lines = text.splitlines()
    if lines and lines[0].strip().startswith("```"):
        lines = lines[1:]
    if lines and lines[-1].strip().startswith("```"):
        lines = lines[:-1]
    return "\n".join(lines).strip()


def _flatten_prose(value: Any, out: list[str]) -> None:
    """Collect readable strings from a value, one level deep."""
    if isinstance(value, str):
        if value.strip():
            out.append(value.strip())
    elif isinstance(value, dict):
        for nested in value.values():
            if isinstance(nested, str) and nested.strip():
                out.append(nested.strip())
    elif isinstance(value, list):
        for nested in value:
            if isinstance(nested, str) and nested.strip():
                out.append(nested.strip())


def unwrap_reply(content: Any, *, fallback: str = "", max_chars: int = 900) -> str:
    """Return the human-readable prose in ``content``, or ``fallback``.

    Handles, in order: a code fence, a JSON envelope carrying a known text key, a JSON body with
    prose containers worth flattening, and finally plain text. Structured content that yields no
    prose returns ``fallback`` rather than a JSON dump - showing a customer raw JSON is worse than
    showing nothing.

    Args:
        content: The model's ``content``, which may be any type.
        fallback: Returned when nothing readable can be recovered.
        max_chars: Cap on the returned prose, so one verbose reply cannot dominate a thread.
    """
    if not isinstance(content, str) or not content.strip():
        return fallback

    text = _strip_code_fence(content.strip())

    try:
        parsed = json.loads(text)
    except (ValueError, TypeError):
        return _cap(text, max_chars) or fallback

    if not isinstance(parsed, dict):
        # A JSON array or scalar is not prose; do not render it.
        logger.debug("Discarding a non-object JSON reply for a prose field.")
        return fallback

    output = parsed.get("output")
    candidates = output if isinstance(output, dict) else parsed

    if isinstance(candidates, dict):
        for key in _TEXT_KEYS:
            value = candidates.get(key)
            if isinstance(value, str) and value.strip():
                return _cap(value.strip(), max_chars)

        collected: list[str] = []
        for key in _PROSE_CONTAINER_KEYS:
            if key in candidates:
                _flatten_prose(candidates[key], collected)
        if collected:
            return _cap(" ".join(collected), max_chars)

    logger.debug("Structured reply carried no readable prose; using the fallback.")
    return fallback


def _cap(text: str, max_chars: int) -> str:
    """Trim to a sentence-ish boundary at or below ``max_chars``."""
    if max_chars <= 0 or len(text) <= max_chars:
        return text

    window = text[:max_chars]
    # Prefer ending on a sentence rather than mid-word.
    for terminator in (". ", "! ", "? "):
        cut = window.rfind(terminator)
        if cut > max_chars // 2:
            return window[: cut + 1].strip()
    return window.rstrip() + "..."
