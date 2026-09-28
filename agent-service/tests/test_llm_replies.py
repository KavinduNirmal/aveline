"""Tests for recovering prose from an LLM completion (app/llm/replies.py).

The universal system prompt instructs every agent to emit a JSON envelope, so a call that is asked
for plain prose may still answer with structured content. That is not a loud failure: the reply is
written into a display field and the Salon renders a wall of JSON to the customer. This happened -
Elle's styling commentary arrived as a ``curated_look`` object and was shown verbatim.
"""

import json

import pytest

from app.llm.replies import unwrap_reply

# ---------------------------------------------------------------------------
# Plain prose passes through
# ---------------------------------------------------------------------------


def test_plain_prose_is_returned_unchanged():
    assert unwrap_reply("A crisp editorial look for the evening.") == (
        "A crisp editorial look for the evening."
    )


def test_a_code_fence_is_stripped():
    assert unwrap_reply("```\nA sculptural silhouette.\n```") == "A sculptural silhouette."


def test_a_language_tagged_fence_is_stripped():
    assert unwrap_reply("```text\nGround the fuchsia with neutrals.\n```") == (
        "Ground the fuchsia with neutrals."
    )


# ---------------------------------------------------------------------------
# Envelopes
# ---------------------------------------------------------------------------


def test_an_envelope_with_a_known_text_key_is_unwrapped():
    reply = json.dumps({"status": "success", "output": {"assistant_reply": "Hello there."}})
    assert unwrap_reply(reply) == "Hello there."


def test_a_top_level_text_key_is_unwrapped_when_there_is_no_output_envelope():
    assert unwrap_reply(json.dumps({"text": "Direct answer."})) == "Direct answer."


# ---------------------------------------------------------------------------
# The real regression: a structured look object
# ---------------------------------------------------------------------------


def test_styling_notes_are_flattened_into_readable_prose():
    """The reported case. The reply was a curated-look object and was rendered verbatim."""
    reply = json.dumps(
        {
            "status": "success",
            "output": {
                "type": "curated_look",
                "hero_piece": {"name": "Fuchsia Pink Bodycon Mini Dress"},
                "styling_notes": {
                    "silhouette": "A bodycon mini is sculptural and unapologetic.",
                    "palette": "Fuchsia is a saturated, high-voltage hue. Ground it with neutrals.",
                },
                "suggested_companion_pieces": [
                    {"piece": "Nude patent pump", "availability": "unknown"}
                ],
            },
        }
    )

    out = unwrap_reply(reply, fallback="RULE FALLBACK")

    assert "sculptural" in out
    assert "Ground it with neutrals" in out
    # The JSON envelope itself must not survive into a display field.
    assert "{" not in out
    assert "curated_look" not in out


def test_a_structured_reply_with_no_prose_falls_back_rather_than_dumping_json():
    """Showing raw JSON is worse than showing nothing."""
    reply = json.dumps({"status": "success", "output": {"type": "curated_look", "items": []}})
    assert unwrap_reply(reply, fallback="RULE FALLBACK") == "RULE FALLBACK"


@pytest.mark.parametrize("content", ["[1, 2, 3]", '"a bare string"', "42", "null", "true"])
def test_non_object_json_falls_back(content):
    # A JSON array or scalar is not prose and must never be rendered as if it were.
    assert unwrap_reply(content, fallback="FB") == "FB"


# ---------------------------------------------------------------------------
# Bounds and degenerate input
# ---------------------------------------------------------------------------


def test_long_prose_is_capped_at_a_sentence_boundary():
    prose = ("This is a full sentence about drape. " * 60).strip()
    out = unwrap_reply(prose, max_chars=120)

    assert len(out) <= 120
    assert out.endswith(".")


def test_capping_never_returns_an_empty_string():
    out = unwrap_reply("x" * 500, max_chars=50)
    assert out
    assert len(out) <= 53  # 50 chars plus the ellipsis


@pytest.mark.parametrize("content", [None, "", "   ", 0, {}, []])
def test_unusable_content_returns_the_fallback(content):
    assert unwrap_reply(content, fallback="FB") == "FB"


def test_a_malformed_fence_does_not_lose_the_text():
    # An unterminated fence still carries prose worth keeping.
    assert unwrap_reply("```\nA draped silhouette.") == "A draped silhouette."
