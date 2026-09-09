"""Tests for the Customer Memory Agent sub-graph (app/agents/customer_memory/).

Drives the compiled LangGraph with a fake ``ToolRegistry`` so behavior is asserted with plain,
rule-based checks (no LLM, no live backend). Covers the golden cases: a wedding inquiry with a
resolved, consenting customer; revoked consent short-circuiting; a missing customer context; and
explicit preference extraction.
"""


from app.agents.customer_memory.graph import build_memory_graph
from app.agents.customer_memory.parsing import parse_message


class FakeRegistry:
    """Records calls and returns canned backend responses."""

    def __init__(self) -> None:
        self.saved_memories: list[tuple[str, str, str]] = []
        self.consent_status = "granted"
        self.profile = {
            "customerId": "cust-sarah",
            "phoneNumber": "+94771234567",
            "fullName": "Sarah Perera",
            "status": "vip",
            "tags": ["vip"],
        }
        self.identify_calls = 0
        self.search_calls = 0

    async def identify_customer(self, org_id, phone_number, full_name=None):
        self.identify_calls += 1
        return self.profile

    async def get_customer_consent(self, org_id, customer_id):
        return {"consentStatus": self.consent_status}

    async def get_customer_memories(self, org_id, customer_id, query, top_k=5):
        self.search_calls += 1
        return [{"id": "mem-old", "content": "Prefers emerald silk", "category": "preference", "similarity": 0.98}]

    async def save_customer_memory(self, org_id, customer_id, content, category):
        self.saved_memories.append((customer_id, content, category))
        return {"id": f"mem-{len(self.saved_memories)}"}


WEDDING_MESSAGE = "Hi! I have a wedding on Saturday. Do you have anything bluish in my size?"


async def _run(registry, *, customer_id="cust-sarah", phone=None, message=WEDDING_MESSAGE, org_id="org-1"):
    graph = build_memory_graph(registry)
    state = {
        "org_id": org_id,
        "customer_id": customer_id,
        "phone_number": phone,
        "customer_name": "Sarah Perera",
        "message": message,
        "intent_type": "item_search",
        "channel": "whatsapp",
        "direction": "inbound",
    }
    return await graph.ainvoke(state)


async def test_wedding_inquiry_success_with_consenting_customer():
    registry = FakeRegistry()
    result = await _run(registry)

    assert result["status"] == "success"
    assert result["output"]["status"] == "success"
    # Structured intent extracted.
    assert result["output"]["parsed_intent"]["intent_type"] == "item_search"
    assert result["output"]["parsed_intent"]["occasion"] == "wedding"
    # Detected event persisted as an event memory.
    event_memories = [m for m in registry.saved_memories if m[2] == "event"]
    assert any("wedding" in content for _, content, _ in event_memories)
    # Semantic context was retrieved and a draft produced.
    assert registry.search_calls == 1
    assert "emerald silk" in result["output"]["interaction_brief"]
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")
    assert result["output"]["action_required"] == "send_whatsapp"


async def test_revoked_consent_short_circuits():
    registry = FakeRegistry()
    registry.consent_status = "revoked"
    result = await _run(registry)

    assert result["status"] == "skipped"
    assert "consent" in result["reason"].lower()
    # Nothing persisted, nothing retrieved, no draft.
    assert registry.saved_memories == []
    assert registry.search_calls == 0
    assert result["output"].get("draft_response") is None


async def test_missing_customer_context_short_circuits():
    registry = FakeRegistry()
    result = await _run(registry, customer_id=None, phone=None)

    assert result["status"] == "skipped"
    assert "customer context" in result["reason"]
    assert registry.identify_calls == 0
    assert registry.saved_memories == []


async def test_identifies_customer_by_phone_when_only_phone_given():
    registry = FakeRegistry()
    result = await _run(registry, customer_id=None, phone="+94771234567")

    assert registry.identify_calls == 1
    assert result["status"] == "success"
    assert result["output"]["customer"]["customer_id"] == "cust-sarah"


async def test_explicit_preference_is_saved():
    registry = FakeRegistry()
    result = await _run(registry, message="I really like silk sarees, please.")

    pref_memories = [m for m in registry.saved_memories if m[2] == "preference"]
    assert pref_memories, "expected at least one preference memory to be saved"
    assert any("silk" in content.lower() for _, content, _ in pref_memories)
    assert result["status"] == "success"


async def test_negative_preference_saved_as_dislikes():
    registry = FakeRegistry()
    await _run(registry, message="I don't like flashy blouses at all.")

    pref_memories = [m for m in registry.saved_memories if m[2] == "preference"]
    assert any("dislikes flashy" in content.lower() for _, content, _ in pref_memories)


# ---------------------------------------------------------------------------
# Pure parsing unit tests (rule-based)
# ---------------------------------------------------------------------------


def test_parse_extracts_wedding_intent_and_size():
    parsed = parse_message("Do you have a blue saree in M for a wedding?")
    intent = parsed["parsed_intent"]
    assert intent["intent_type"] == "item_search"
    assert intent["occasion"] == "wedding"
    assert intent["color"] == "blue"
    assert intent["size"] == "M"
    assert parsed["event_type"] == "wedding"


def test_parse_detects_budget():
    parsed = parse_message("What sarees do you have under 45000?")
    assert parsed["parsed_intent"]["budget"] == 45000


def test_parse_no_event_no_preference():
    parsed = parse_message("Hi, how are you?")
    assert parsed["event_type"] is None
    assert parsed["preference_signals"] == []
    assert parsed["parsed_intent"]["intent_type"] == "general_inquiry"
