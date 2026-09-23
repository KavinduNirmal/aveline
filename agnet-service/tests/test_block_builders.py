"""Tests for translating real agent output into Salon content blocks.

Each specialist agent (memory, visual, commerce) produces structured output that these
builders map into the typed content blocks documented in ``docs/architecture/inbox.md`` §5
so the Salon renders real content instead of placeholder text (Issues #150 and #151).
"""

from app.events.block_builders import (
    build_ava_blocks,
    build_aveline_blocks,
    build_clarification_blocks,
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


# ------------------------------------------------------------------- Aveline (reply)


def test_aveline_renders_the_supervisors_reply():
    output = {
        "intent": "general_inquiry",
        "reply": "Hello! I'm Aveline. What are you looking for today?",
        "memory": _memory_skipped(),
        "visual": None,
        "commerce": None,
    }
    blocks = build_aveline_blocks(output)

    assert blocks == [{"type": "text", "text": "Hello! I'm Aveline. What are you looking for today?"}]


def test_aveline_is_silent_when_there_is_nothing_to_say():
    # The reported defect: an unroutable message produced the meta note "Treated this as a general
    # inquiry." A routing summary is not content, so with no reply Aveline posts nothing at all.
    output = {"intent": "general_inquiry", "memory": _memory_skipped(), "visual": None, "commerce": None}

    assert build_aveline_blocks(output) == []


def test_aveline_never_renders_a_routing_summary():
    for intent in ("item_search", "pricing_query", "event_query", "general_inquiry", "order_placement"):
        blocks = build_aveline_blocks({"intent": intent})
        text = " ".join(b.get("text", "") for b in blocks)
        assert "Treated this as" not in text
        assert "Intent:" not in text


def test_aveline_reply_is_withheld_when_a_specialist_spoke():
    # One answer per message: if a specialist produced content, Aveline's fallback reply is dropped.
    output = {
        "intent": "general_inquiry",
        "reply": "Hello! I'm Aveline.",
        "memory": _memory_with_customer(),
        "visual": None,
        "commerce": None,
    }

    assert build_aveline_blocks(output, specialist_spoke=True) == []


def test_a_clarification_still_wins_over_a_reply():
    output = {
        "intent": "general_inquiry",
        "reply": "Hello!",
        "clarification": {
            "kind": "asked",
            "question": "Which one did you mean - the silk or the linen?",
        },
    }
    blocks = build_aveline_blocks(output, specialist_spoke=True)

    assert blocks[0]["text"] == "Which one did you mean - the silk or the linen?"



# ------------------------------------------------------------------ Clarification (Aveline)


def _ambiguous_clarification() -> dict:
    return {
        "kind": "ambiguous",
        "candidates": [
            {"customer_id": "c1", "full_name": "Samantha Arias", "status": "vip", "last_visit_at": "2026-08-20"},
            {"customer_id": "c2", "full_name": "Samantha Ranaweera", "status": "returning", "last_visit_at": None},
        ],
    }


def test_aveline_renders_ambiguous_as_a_choice_block():
    output = {"intent": "event_query", "clarification": _ambiguous_clarification()}
    blocks = build_aveline_blocks(output)

    choice = next(b for b in blocks if b["type"] == "choice")
    assert len(choice["options"]) == 2
    assert choice["options"][0]["customerId"] == "c1"
    assert choice["options"][0]["fullName"] == "Samantha Arias"
    assert choice["options"][0]["status"] == "vip"
    assert choice["options"][0]["lastVisitAt"] == "2026-08-20"


def test_clarification_ambiguous_without_candidates_renders_nothing():
    assert build_clarification_blocks({"kind": "ambiguous", "candidates": []}) == []


def test_aveline_renders_not_found_as_an_ask_for_phone_text_block():
    output = {"intent": "event_query", "clarification": {"kind": "not_found"}}
    blocks = build_aveline_blocks(output)

    text = next(b for b in blocks if b["type"] == "text")
    assert "phone number" in text["text"]


def test_clarification_ignores_other_kinds():
    assert build_clarification_blocks({"kind": "resolved", "customer_id": "c1"}) == []
    assert build_clarification_blocks(None) == []
    assert build_clarification_blocks({}) == []


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

    assert not any(b["type"] == "piece" for b in blocks)
    assert any(b["type"] == "look" for b in blocks)


def test_elle_blocks_staff_query_emits_text_block_no_suggestion():
    staff_visual = {
        "agent": "visual",
        "ran": True,
        "status": "success",
        "text": "Found 2 matching items in boutique inventory for 'silk saree'.",
        "suggestion": None,
        "items": [
            {
                "itemId": "i1",
                "name": "Silk Slip Dress",
                "price": 24000,
                "size": "M",
                "stock": 2,
            }
        ],
        "looks": [],
    }
    blocks = build_elle_blocks(staff_visual)

    assert blocks[0]["type"] == "text"
    assert "Found 2 matching items" in blocks[0]["text"]
    assert not any(b["type"] == "suggestion" for b in blocks)
    assert any(b["type"] == "piece" for b in blocks)


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


# ------------------------------------------------------- Aveline (handbook citation, ADR-025)


def test_aveline_emits_the_reply_and_a_sources_block():
    output = {
        "intent": "aveline_help",
        "reply": "Of course - invite them from Team.",
        "handbook_sources": [
            {
                "sourceKey": "web-docs/team",
                "title": "Team",
                "url": "/docs/team",
                "heading": "Invitations",
            }
        ],
    }

    blocks = build_aveline_blocks(output)

    assert blocks[0] == {"type": "text", "text": "Of course - invite them from Team."}
    # The citation is structured, so a frontend can turn it into links. It is deliberately not
    # appended to the prose: a frontend cannot reliably find a link inside model-written text.
    assert blocks[1] == {
        "type": "sources",
        "items": [{"title": "Team", "url": "/docs/team", "heading": "Invitations"}],
    }


def test_aveline_omits_the_sources_block_without_sources():
    output = {"intent": "general_inquiry", "reply": "Hello, I'm Aveline."}

    assert build_aveline_blocks(output) == [{"type": "text", "text": "Hello, I'm Aveline."}]


def test_the_sources_block_comes_from_the_chunks_not_the_reply():
    # The citation is built from what was actually retrieved, so a model that names a page it did
    # not use cannot put that page in the thread.
    output = {
        "intent": "aveline_help",
        "reply": "See the Billing page for that.",
        "handbook_sources": [{"title": "Team", "url": "/docs/team"}],
    }

    blocks = build_aveline_blocks(output)

    assert blocks[1]["items"] == [{"title": "Team", "url": "/docs/team"}]
    assert "Sources" not in blocks[0]["text"]


def test_the_sources_block_names_each_page_once():
    output = {
        "intent": "aveline_help",
        "reply": "x",
        "handbook_sources": [{"title": "Team"}, {"title": "Team"}, {"title": "Salon"}],
    }

    items = build_aveline_blocks(output)[1]["items"]

    assert [item["title"] for item in items] == ["Team", "Salon"]


def test_a_source_without_a_url_still_renders_as_a_citation():
    output = {"intent": "aveline_help", "reply": "x", "handbook_sources": [{"title": "Team"}]}

    assert build_aveline_blocks(output)[1]["items"] == [{"title": "Team"}]
