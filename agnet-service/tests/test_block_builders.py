"""Tests for translating real agent output into Salon content blocks.

Each specialist agent (memory, visual, commerce) produces structured output that these
builders map into the typed content blocks documented in ``docs/architecture/inbox.md`` §5
so the Salon renders real content instead of placeholder text (Issues #150 and #151).
"""

from app.events.block_builders import (
    build_ava_blocks,
    build_aveline_blocks,
    build_elle_blocks,
    build_lina_blocks,
)


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


# ----------------------------------------------------------------------- Elle (visual)


def _visual_with_content() -> dict:
    return {
        "agent": "visual",
        "ran": True,
        "status": "success",
        "suggestion": "These pieces look like they match Michael's brief.",
        "looks": [{"imageUrl": "https://cdn/outfit-1.jpg", "name": "Wedding look", "text": "Saree + blouse"}],
        "items": [
            {
                "itemId": "i1",
                "name": "Silk Slip Dress",
                "price": 24000,
                "size": "M",
                "stock": 2,
                "imageUrl": "https://cdn/piece-1.jpg",
            }
        ],
    }


def _visual_stub() -> dict:
    return {"agent": "visual", "ran": True, "status": "stub", "note": "Not wired yet (Slice 2)."}


def test_elle_blocks_include_the_leading_suggestion():
    blocks = build_elle_blocks(_visual_with_content())

    assert blocks[0]["type"] == "suggestion"
    assert "match Michael" in blocks[0]["text"]


def test_elle_blocks_include_looks_as_look_blocks():
    blocks = build_elle_blocks(_visual_with_content())

    look = next(b for b in blocks if b["type"] == "look")
    assert look["imageUrl"] == "https://cdn/outfit-1.jpg"
    assert look["name"] == "Wedding look"


def test_elle_blocks_include_items_as_piece_blocks():
    blocks = build_elle_blocks(_visual_with_content())

    piece = next(b for b in blocks if b["type"] == "piece")
    assert piece["name"] == "Silk Slip Dress"
    assert piece["price"] == 24000
    assert piece["size"] == "M"


def test_elle_blocks_are_empty_for_a_stub():
    assert build_elle_blocks(_visual_stub()) == []


def test_elle_blocks_are_empty_when_visual_output_is_absent():
    assert build_elle_blocks(None) == []
    assert build_elle_blocks({}) == []


def test_elle_blocks_skip_piece_when_no_items():
    visual = _visual_with_content()
    visual["items"] = []
    blocks = build_elle_blocks(visual)

    assert all(b["type"] != "piece" for b in blocks)


# ----------------------------------------------------------------------- Lina (commerce)


def _commerce_with_payment() -> dict:
    return {
        "agent": "commerce",
        "ran": True,
        "status": "success",
        "summary": "Payment is ready for this order.",
        "payment": {"amount": 48000, "status": "pending", "url": "https://pay/order-1"},
        "courier": None,
    }


def _commerce_stub() -> dict:
    return {"agent": "commerce", "ran": True, "status": "stub", "note": "Not wired yet (Slice 3)."}


def test_lina_blocks_include_a_summary_text():
    blocks = build_lina_blocks(_commerce_with_payment())

    text = next(b for b in blocks if b["type"] == "text")
    assert "Payment is ready" in text["text"]


def test_lina_blocks_include_payment_as_a_payment_block():
    blocks = build_lina_blocks(_commerce_with_payment())

    payment = next(b for b in blocks if b["type"] == "payment")
    assert payment["amount"] == 48000
    assert payment["status"] == "pending"


def test_lina_blocks_include_courier_when_present():
    commerce = _commerce_with_payment()
    commerce["courier"] = {"status": "booked", "carrier": "DHL"}
    blocks = build_lina_blocks(commerce)

    courier = next(b for b in blocks if b["type"] == "courier")
    assert courier["status"] == "booked"


def test_lina_blocks_are_empty_for_a_stub():
    assert build_lina_blocks(_commerce_stub()) == []


def test_lina_blocks_are_empty_when_commerce_output_is_absent():
    assert build_lina_blocks(None) == []
    assert build_lina_blocks({}) == []


def test_lina_blocks_do_not_emit_sign_off_from_the_generic_builder():
    # SignOff is a first-class HITL message (kind=SignOff) handled by the commerce
    # approval flow, not by generic persona messages. The generic builder must never
    # emit a sign_off card.
    commerce = {
        "agent": "commerce",
        "ran": True,
        "status": "pending_approval",
        "needs_approval": True,
        "approval": {"amount": 50000, "reason": "above limit"},
    }
    assert all(b["type"] != "sign_off" for b in build_lina_blocks(commerce))
