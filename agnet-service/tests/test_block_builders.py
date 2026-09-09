"""Tests for translating real agent output into Salon content blocks.

The Customer Memory Agent produces rich output (interaction brief, extracted memories,
detected events, a customer-facing draft). These builders map that output into the typed
content blocks documented in ``docs/architecture/inbox.md`` §5 so the Salon renders real
content instead of placeholder text (Issue #150).
"""

from app.events.block_builders import build_ava_blocks, build_aveline_blocks


def _memory_with_customer() -> dict:
    """A realistic ``output["memory"]`` value from the concierge with a resolved customer."""
    return {
        "agent": "memory",
        "ran": True,
        "status": "success",
        "customer": {"customer_id": "c1", "full_name": "Michael", "status": "returning"},
        "parsed_intent": {"intent_type": "item_search", "occasion": "wedding", "color": "silk"},
        "interaction_brief": "Michael (returning)",
        "extracted_memories": [
            {
                "content": "Michael prefers silk dresses",
                "category": "preference",
                "is_explicit": True,
                "confidence": 0.9,
            },
            {
                "content": "Michael has a wedding on 2026-12-01",
                "category": "event",
                "is_explicit": True,
                "confidence": 0.9,
            },
        ],
        "detected_events": [{"event_type": "wedding", "event_date": "2026-12-01"}],
        "draft_response": "Hi Michael! We would love to help you find a silk option for your wedding.",
        "action_required": "send_whatsapp",
    }


def _memory_skipped() -> dict:
    """The memory sub-output when no customer context is available."""
    return {
        "agent": "memory",
        "ran": True,
        "status": "skipped",
        "reason": "no customer context available",
        "parsed_intent": {"intent_type": "general_inquiry"},
    }


# --------------------------------------------------------------------------- Ava (memory)


def test_ava_blocks_lead_with_the_interaction_brief():
    blocks = build_ava_blocks(_memory_with_customer())

    assert blocks[0]["type"] == "text"
    assert blocks[0]["text"] == "Michael (returning)"


def test_ava_blocks_include_memories_as_at_a_glance_table():
    blocks = build_ava_blocks(_memory_with_customer())

    table = next(b for b in blocks if b["type"] == "at_a_glance")
    assert table["columns"] == ["Category", "Content"]
    assert len(table["rows"]) == 2
    assert ["preference", "Michael prefers silk dresses"] in table["rows"]
    assert ["event", "Michael has a wedding on 2026-12-01"] in table["rows"]


def test_ava_blocks_include_the_draft_response_as_a_suggestion():
    blocks = build_ava_blocks(_memory_with_customer())

    suggestion = next(b for b in blocks if b["type"] == "suggestion")
    assert "silk option for your wedding" in suggestion["text"]


def test_ava_blocks_are_empty_when_memory_skipped():
    assert build_ava_blocks(_memory_skipped()) == []


def test_ava_blocks_are_empty_when_memory_output_is_absent():
    assert build_ava_blocks(None) == []
    assert build_ava_blocks({}) == []


def test_ava_blocks_skip_at_a_glance_when_no_memories():
    memory = _memory_with_customer()
    memory["extracted_memories"] = []
    memory["detected_events"] = []
    blocks = build_ava_blocks(memory)

    assert all(b["type"] != "at_a_glance" for b in blocks)


# ----------------------------------------------------------------------- Aveline (summary)


def test_aveline_summary_is_a_text_block_without_the_old_intent_template():
    output = {
        "intent": "item_search",
        "memory": _memory_with_customer(),
        "visual": None,
        "commerce": None,
    }
    blocks = build_aveline_blocks(output)

    assert len(blocks) == 1
    assert blocks[0]["type"] == "text"
    # The old stub "Intent: item_search" must be gone.
    assert "Intent:" not in blocks[0]["text"]


def test_aveline_summary_mentions_the_resolved_customer_when_present():
    output = {
        "intent": "item_search",
        "memory": _memory_with_customer(),
        "visual": None,
        "commerce": None,
    }
    text = build_aveline_blocks(output)[0]["text"]

    assert "Michael" in text


def test_aveline_summary_does_not_duplicate_ava_content_when_no_customer():
    output = {"intent": "general_inquiry", "memory": _memory_skipped(), "visual": None, "commerce": None}
    text = build_aveline_blocks(output)[0]["text"]

    assert text  # non-empty, still an acknowledgement
    assert "Michael" not in text
