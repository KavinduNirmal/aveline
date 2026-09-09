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
        self.recorded_interactions: list[tuple[str, str, str, str, str | None]] = []
        self.added_events: list[tuple[str, str, str, str | None]] = []
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
        self.brief_calls = 0
        self.memories_raise = False

    async def identify_customer(self, org_id, phone_number, full_name=None):
        self.identify_calls += 1
        return self.profile

    async def get_customer_consent(self, org_id, customer_id):
        return {"consentStatus": self.consent_status}

    async def get_customer_memories(self, org_id, customer_id, query, top_k=5):
        if self.memories_raise:
            raise RuntimeError("semantic search backend unavailable")
        self.search_calls += 1
        return [{"id": "mem-old", "content": "Prefers emerald silk", "category": "preference", "similarity": 0.98}]

    async def save_customer_memory(self, org_id, customer_id, content, category):
        self.saved_memories.append((customer_id, content, category))
        return {"id": f"mem-{len(self.saved_memories)}"}

    async def record_customer_interaction(
        self, org_id, customer_id, channel, direction, message_content, parsed_intent_json=None
    ):
        self.recorded_interactions.append(
            (customer_id, channel, direction, message_content, parsed_intent_json)
        )
        return {"id": f"int-{len(self.recorded_interactions)}"}

    async def add_customer_event(self, org_id, customer_id, event_type, event_date, description=None):
        self.added_events.append((customer_id, event_type, event_date, description))
        return {"id": f"evt-{len(self.added_events)}"}

    async def get_customer_events(self, org_id, customer_id):
        return [{"id": "evt-1", "eventType": "wedding", "eventDate": "2026-12-01"}]

    async def generate_interaction_brief(self, org_id, customer_id):
        self.brief_calls += 1
        return {
            "customerId": customer_id,
            "customerName": "Sarah Perera",
            "status": "vip",
            "preferenceSummary": None,
            "upcomingEvents": "wedding on 2026-12-01",
            "tags": ["vip"],
        }


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


async def test_inbound_interaction_is_recorded_with_parsed_intent():
    """The persist node logs the inbound message with its parsed intent (Issue #164)."""
    registry = FakeRegistry()
    result = await _run(registry)

    assert result["status"] == "success"
    assert len(registry.recorded_interactions) == 1
    customer_id, channel, direction, content, intent_json = registry.recorded_interactions[0]
    assert customer_id == "cust-sarah"
    assert channel == "whatsapp"
    assert direction == "inbound"
    assert "wedding" in content
    assert intent_json is not None
    assert '"intent_type": "item_search"' in intent_json


async def test_revoked_consent_does_not_record_interaction():
    registry = FakeRegistry()
    registry.consent_status = "revoked"
    await _run(registry)

    assert registry.recorded_interactions == []
    assert registry.saved_memories == []


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
# LLM draft generation (Issue #163) — an injected chat model drives the draft.
# ---------------------------------------------------------------------------


class _Msg:
    def __init__(self, content, input_tokens=12, output_tokens=6):
        self.content = content
        self.usage_metadata = {"input_tokens": input_tokens, "output_tokens": output_tokens}


class FakeChatModel:
    """Minimal chat-model double exposing only ``ainvoke``."""

    def __init__(self, draft="LLM-generated draft for Sarah.", fail=False):
        self.draft = draft
        self.fail = fail

    async def ainvoke(self, messages):
        if self.fail:
            raise RuntimeError("provider unavailable")
        return _Msg(self.draft)


async def _llm_state():
    return {
        "org_id": "org-1",
        "customer_id": "cust-sarah",
        "customer_name": "Sarah Perera",
        "message": WEDDING_MESSAGE,
        "intent_type": "item_search",
        "channel": "whatsapp",
        "direction": "inbound",
    }


async def test_llm_produces_draft_and_usage_when_injected():
    registry = FakeRegistry()
    llm = FakeChatModel(draft="Ava LLM draft.")
    graph = build_memory_graph(registry, llm=llm)
    result = await graph.ainvoke(await _llm_state())

    assert result["status"] == "success"
    assert result["output"]["draft_response"] == "Ava LLM draft."
    # Token usage captured from the LLM response (surfaced on state, not the schema output).
    assert result["usage"] == {"input_tokens": 12, "output_tokens": 6}


async def test_llm_failure_falls_back_to_template():
    registry = FakeRegistry()
    graph = build_memory_graph(registry, llm=FakeChatModel(fail=True))
    result = await graph.ainvoke(await _llm_state())

    assert result["status"] == "success"
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")
    # No LLM usage recorded when the fallback ran.
    assert result["usage"] is None


async def test_llm_json_envelope_is_unwrapped_to_plain_reply():
    """The universal prompt can make the LLM return a JSON envelope; the draft must be plain text."""
    registry = FakeRegistry()
    enveloped = (
        '```json\n{"status": "success", "output": {"assistant_reply": "A plain warm reply."}}\n```'
    )
    graph = build_memory_graph(registry, llm=FakeChatModel(draft=enveloped))
    result = await graph.ainvoke(await _llm_state())

    assert result["status"] == "success"
    assert result["output"]["draft_response"] == "A plain warm reply."
    assert result["output"]["draft_response"].startswith("```") is False


async def test_no_llm_defaults_to_template_without_usage():
    registry = FakeRegistry()
    graph = build_memory_graph(registry)  # llm defaults to None
    result = await graph.ainvoke(await _llm_state())

    assert result["status"] == "success"
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")
    assert result.get("usage") is None


# ---------------------------------------------------------------------------
# Structured events + backend brief (Issue #166)
# ---------------------------------------------------------------------------


async def test_dated_detected_event_creates_structured_customer_event():
    registry = FakeRegistry()
    state = {
        "org_id": "org-1",
        "customer_id": "cust-sarah",
        "customer_name": "Sarah Perera",
        "message": "I have a wedding on 2026-12-01, do you have any bluish sarees?",
        "intent_type": "item_search",
        "channel": "whatsapp",
        "direction": "inbound",
    }
    result = await build_memory_graph(registry).ainvoke(state)

    assert result["status"] == "success"
    # A structured Customer_Event row is created when a date is present.
    assert len(registry.added_events) == 1
    customer_id, event_type, event_date, _description = registry.added_events[0]
    assert customer_id == "cust-sarah"
    assert event_type == "wedding"
    assert event_date == "2026-12-01"
    assert result["output"]["detected_events"][0]["event_type"] == "wedding"


async def test_undated_event_falls_back_to_text_memory_only():
    registry = FakeRegistry()
    state = {
        "org_id": "org-1",
        "customer_id": "cust-sarah",
        "customer_name": "Sarah Perera",
        "message": WEDDING_MESSAGE,  # "wedding on Saturday" — no ISO date
        "intent_type": "item_search",
        "channel": "whatsapp",
        "direction": "inbound",
    }
    result = await build_memory_graph(registry).ainvoke(state)

    assert result["status"] == "success"
    # No structured event (no date) — but an event text memory is still saved.
    assert registry.added_events == []
    assert any(m[2] == "event" for m in registry.saved_memories)


async def test_output_brief_uses_backend_events_and_semantic_context():
    registry = FakeRegistry()
    result = await _run(registry)

    assert result["status"] == "success"
    assert registry.brief_calls == 1
    brief = result["output"]["interaction_brief"]
    assert "wedding on 2026-12-01" in brief  # real events from the backend brief
    assert "emerald silk" in brief  # semantic context is preserved
    assert "vip" in brief


async def test_output_brief_falls_back_when_backend_fails():
    registry = FakeRegistry()

    async def boom(org_id, customer_id):
        raise RuntimeError("brief backend down")

    registry.generate_interaction_brief = boom
    result = await _run(registry)

    assert result["status"] == "success"
    # Still has a usable brief (name + status + semantic context) and no raise.
    assert result["output"]["interaction_brief"]


async def test_semantic_search_failure_degrades_gracefully():
    """A semantic-search (embedding) failure must not fail the whole agent run (retrieve fallback)."""
    registry = FakeRegistry()
    registry.memories_raise = True
    result = await _run(registry)

    assert result["status"] == "success"
    # No semantic context but the run still composes a brief and draft.
    assert result["output"]["interaction_brief"]
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")


async def test_output_is_schema_consistent_and_excludes_undated_events():
    """The assembled output validates against MemoryAgentOutput (Issue #167)."""
    registry = FakeRegistry()
    result = await _run(registry, message="I have a wedding on Saturday — any bluish sarees?")
    assert result["status"] == "success"
    # The undated "wedding on Saturday" event is saved as a memory but not emitted as a
    # structured DetectedEvent (which requires a date), keeping the output schema-valid.
    assert result["output"]["detected_events"] == []
    assert any(m[2] == "event" for m in registry.saved_memories)


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
