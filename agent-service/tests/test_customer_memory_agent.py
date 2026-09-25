"""Tests for the Customer Memory Agent sub-graph (app/agents/customer_memory/).

Drives the compiled LangGraph with a fake ``ToolRegistry`` so behavior is asserted with plain,
rule-based checks (no LLM, no live backend). Covers the golden cases: a wedding inquiry with a
resolved, consenting customer; revoked consent short-circuiting; a missing customer context; and
explicit preference extraction.
"""


import httpx

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
        self.consent_error: Exception | None = None

    async def identify_customer(self, org_id, phone_number, full_name=None):
        self.identify_calls += 1
        return self.profile

    async def get_customer_consent(self, org_id, customer_id):
        if self.consent_error is not None:
            raise self.consent_error
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


async def test_revoked_consent_is_surfaced_on_the_sub_graph_state():
    """1.3: the orchestrator can only block downstream agents if the status escapes the sub-graph."""
    registry = FakeRegistry()
    registry.consent_status = "revoked"

    result = await _run(registry)

    assert result["consent_status"] == "revoked"


async def test_consent_check_failure_fails_closed_without_raising():
    """1.4: a backend failure used to raise through the node and 500 the whole query.

    Fail closed: personalization (retrieval + writes + draft) must not happen for a customer
    whose consent could not be read, and the node must report a skip rather than an exception.
    """
    registry = FakeRegistry()
    registry.consent_error = RuntimeError("consent backend unavailable")

    result = await _run(registry)

    assert result["status"] == "skipped"
    assert "unavailable" in result["reason"].lower()
    assert result["consent_status"] == "unavailable"
    assert registry.saved_memories == []
    assert registry.search_calls == 0
    assert registry.recorded_interactions == []
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
    def __init__(self, content, input_tokens=12, output_tokens=6, cached_tokens=0):
        self.content = content
        self.usage_metadata = {"input_tokens": input_tokens, "output_tokens": output_tokens}
        if cached_tokens:
            self.usage_metadata["input_token_details"] = {"cache_read": cached_tokens}


class FakeChatModel:
    """Minimal chat-model double exposing only ``ainvoke``."""

    def __init__(self, draft="LLM-generated draft for Sarah.", fail=False, cached_tokens=0):
        self.draft = draft
        self.fail = fail
        self.cached_tokens = cached_tokens

    async def ainvoke(self, messages):
        if self.fail:
            raise RuntimeError("provider unavailable")
        return _Msg(self.draft, cached_tokens=self.cached_tokens)


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


async def test_llm_usage_surfaces_the_cached_token_direction():
    """Slice 4a — cached prompt tokens ride on state so the additive metric can observe them."""
    registry = FakeRegistry()
    llm = FakeChatModel(draft="Ava LLM draft.", cached_tokens=64)
    graph = build_memory_graph(registry, llm=llm)
    result = await graph.ainvoke(await _llm_state())

    assert result["usage"]["cached_tokens"] == 64
    assert result["usage"]["input_tokens"] == 12


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
# Staff query vs inbound customer (ADR-019 two modes)
# ---------------------------------------------------------------------------


async def test_staff_event_query_answers_directly_with_no_suggestion():
    """A STAFF question ('any events for @x?') is answered directly, with no customer draft."""
    registry = FakeRegistry()
    registry.consent_status = "granted"
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1",
        "customer_id": "cust-sarah",
        "customer_name": "Sarah Perera",
        "message": "any upcoming events for @sarah perera?",
        "intent_type": "event_query",
        "channel": "whatsapp",
        "direction": None,          # no inbound direction -> staff query
        "staff_query": True,
    }
    result = await graph.ainvoke(state)

    assert result["status"] == "success"
    out = result["output"]
    # No customer-facing draft / Suggestion for a staff query.
    assert out["draft_response"] is None
    assert out["action_required"] is None
    assert out["interaction_brief"]  # Ava still answers
    # FakeRegistry backend reports an upcoming event, so the direct answer reflects it.
    assert "Upcoming events" in out["interaction_brief"]


async def test_staff_event_query_no_events_reports_none():
    registry = FakeRegistry()
    async def brief(org_id, customer_id):
        return {"customerName": "Sarah Perera", "status": "vip", "upcomingEvents": None, "tags": []}
    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1", "customer_id": "cust-sarah", "customer_name": "Sarah Perera",
        "message": "any events for @sarah?", "intent_type": "event_query",
        "channel": "whatsapp", "direction": None, "staff_query": True,
    }
    result = await graph.ainvoke(state)
    assert result["status"] == "success"
    assert result["output"]["draft_response"] is None
    assert "no upcoming events" in result["output"]["interaction_brief"].lower()


async def test_staff_new_customer_note_is_answer_announces_new_customer():
    """A general staff note onboarding a brand-new customer yields a helpful direct answer."""
    registry = FakeRegistry()
    async def brief(org_id, customer_id):
        return {"customerName": "Jason smith", "status": "new", "upcomingEvents": None, "preferenceSummary": None, "tags": []}
    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1", "customer_id": "cust-jason", "customer_name": "Jason smith",
        "message": "A new customer dropped by, @Jason smith, #0751234567 he will come by tomorrow",
        "intent_type": "general_inquiry", "channel": "whatsapp", "direction": None, "staff_query": True,
    }
    result = await graph.ainvoke(state)
    assert result["status"] == "success"
    out = result["output"]
    assert out["draft_response"] is None
    text = out["interaction_brief"].lower()
    assert "new customer" in text
    assert "jason smith" in text
    assert "on file" in text


async def test_staff_general_query_summarizes_known_facts():
    registry = FakeRegistry()
    async def brief(org_id, customer_id):
        return {
            "customerName": "Sarah Perera", "status": "returning",
            "preferenceSummary": "colour: emerald", "upcomingEvents": "wedding on 2026-12-01", "tags": ["vip"],
        }
    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1", "customer_id": "cust-sarah", "customer_name": "Sarah Perera",
        "message": "what do we know about @sarah?", "intent_type": "general_inquiry",
        "channel": "whatsapp", "direction": None, "staff_query": True,
    }
    result = await graph.ainvoke(state)
    assert result["status"] == "success"
    text = result["output"]["interaction_brief"].lower()
    assert "sarah perera" in text
    assert "emerald" in text
    assert "wedding" in text


async def test_staff_query_does_not_record_customer_interaction():
    registry = FakeRegistry()
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1", "customer_id": "cust-sarah", "customer_name": "Sarah Perera",
        "message": "any events for @sarah?", "intent_type": "event_query",
        "channel": "whatsapp", "direction": None, "staff_query": True,
    }
    await graph.ainvoke(state)
    assert registry.recorded_interactions == []


async def test_customer_inbound_still_produces_draft():
    """An inbound customer message (direction present) still gets a customer-facing draft."""
    registry = FakeRegistry()
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1", "customer_id": "cust-sarah", "customer_name": "Sarah Perera",
        "message": "Hi, do you have a blue saree?", "intent_type": "item_search",
        "channel": "whatsapp", "direction": "inbound", "staff_query": False,
    }
    result = await graph.ainvoke(state)
    assert result["status"] == "success"
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")
    assert len(registry.recorded_interactions) == 1


async def test_persist_memory_failure_does_not_fail_run():
    """A failed memory save (e.g. embedding provider down) must not fail the inbound run."""
    registry = FakeRegistry()

    async def boom(org_id, customer_id, content, category):
        raise RuntimeError("embedding provider down")

    registry.save_customer_memory = boom
    graph = build_memory_graph(registry)
    state = {
        "org_id": "org-1", "customer_id": "cust-sarah", "customer_name": "Sarah Perera",
        "message": "I have a wedding on 2026-12-01 - I love silk sarees", "intent_type": "item_search",
        "channel": "whatsapp", "direction": "inbound", "staff_query": False,
    }
    result = await graph.ainvoke(state)
    assert result["status"] == "success"
    assert result["output"]["draft_response"]


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


# ---------------------------------------------------------------------------
# Learning the customer's name from their own introduction
# ---------------------------------------------------------------------------
#
# The customer stayed "Unknown customer" across messages because the name was never read out of the
# message. It only ever reached the backend if it arrived in org_context, which an inbound WhatsApp
# message never carries.


class _NamedRegistry(FakeRegistry):
    """A registry whose stored customer has no name yet, recording what it was offered."""

    def __init__(self, *, existing_name: str | None = None) -> None:
        super().__init__()
        self.profile = {
            "customerId": "cust-kasha",
            "phoneNumber": "94763475058",
            "fullName": existing_name,
            "status": "new",
            "tags": [],
        }
        self.offered_names: list[str | None] = []

    async def identify_customer(self, org_id, phone_number, full_name=None):
        self.identify_calls += 1
        self.offered_names.append(full_name)
        if full_name and not (self.profile.get("fullName") or "").strip():
            self.profile = {**self.profile, "fullName": full_name}
        return self.profile


async def _run_memory(
    registry, *, customer_id, phone, message, customer_name=None, intent_type="item_search"
):
    graph = build_memory_graph(registry)
    return await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": customer_id,
            "phone_number": phone,
            "customer_name": customer_name,
            "message": message,
            # The upstream gate's decision. In production this is what the memory agent echoes,
            # so it is the value that has to survive this agent's output schema.
            "intent_type": intent_type,
            "channel": "whatsapp",
            "direction": "inbound",
        }
    )


async def test_introduction_is_stored_when_the_customer_was_already_resolved_by_phone():
    """The reported bug: the phone resolved the customer, so identify was never called.

    An inbound message resolves the customer from the sender's own number before this node runs, so
    the ``not customer_id and phone`` branch is skipped - and with it the only place the name was
    ever offered for storage. The name must be backfilled on the existing-customer path too.
    """
    registry = _NamedRegistry(existing_name=None)

    await _run_memory(
        registry,
        customer_id="cust-kasha",
        phone="94763475058",
        message="I'm Kasha vivian, this is for a cocktail party",
    )

    assert registry.offered_names == ["Kasha vivian"]


async def test_introduction_is_stored_when_the_customer_is_resolved_for_the_first_time():
    registry = _NamedRegistry(existing_name=None)

    await _run_memory(
        registry,
        customer_id=None,
        phone="94763475058",
        message="I'm Kasha vivian, this is for a cocktail party",
    )

    assert registry.offered_names == ["Kasha vivian"]


async def test_an_existing_name_is_never_overwritten_by_a_later_guess():
    """Identify only backfills a missing name; a stored name wins."""
    registry = _NamedRegistry(existing_name="Kasha Vivian")

    await _run_memory(
        registry,
        customer_id="cust-kasha",
        phone="94763475058",
        message="I'm someone else entirely",
        customer_name="Kasha Vivian",
    )

    assert registry.offered_names == []


async def test_a_message_without_an_introduction_offers_no_name():
    """A product question must not be mined for a name, or prose would end up on the record."""
    registry = _NamedRegistry(existing_name=None)

    await _run_memory(
        registry,
        customer_id="cust-kasha",
        phone="94763475058",
        message="Hello there. Are there any pinkish gowns in your collection?",
    )

    assert registry.offered_names == []


# ---------------------------------------------------------------------------
# Applying an explicit staff update instruction
# ---------------------------------------------------------------------------
#
# Staff type "please update this customer with the name X and number Y". This is the only path that
# writes identity fields from chat, so the guards matter more than the happy path.


class _UpdateRegistry(FakeRegistry):
    """A registry recording customer updates and able to refuse one."""

    def __init__(self, *, fail_with: int | None = None) -> None:
        super().__init__()
        self.updates: list[tuple[str, str, str | None, str | None]] = []
        self._fail_with = fail_with

    async def update_customer(self, org_id, customer_id, full_name=None, phone_number=None):
        self.updates.append((org_id, customer_id, full_name, phone_number))
        if self._fail_with is not None:
            request = httpx.Request("PATCH", "http://api/internal/customers/cust-1")
            response = httpx.Response(self._fail_with, request=request, json={})
            raise httpx.HTTPStatusError("refused", request=request, response=response)
        return {**self.profile, "fullName": full_name or self.profile.get("fullName")}


async def _run_update(registry, *, message, customer_id="cust-kasha", staff_query=True):
    graph = build_memory_graph(registry)
    return await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": customer_id,
            "phone_number": "94763475058",
            "message": message,
            "channel": "whatsapp",
            "direction": None if staff_query else "inbound",
            "staff_query": staff_query,
        }
    )


UPDATE_MESSAGE = "Please update this customer with the name Kasha Vivian Perera and number 0771234567"


async def test_staff_instruction_applies_the_update_and_confirms():
    registry = _UpdateRegistry()

    result = await _run_update(registry, message=UPDATE_MESSAGE)

    assert registry.updates == [
        ("org-1", "cust-kasha", "Kasha Vivian Perera", "0771234567")
    ]
    # The answer states what changed, so staff can see it landed.
    assert "Kasha Vivian Perera" in result["output"]["interaction_brief"]
    assert "0771234567" in result["output"]["interaction_brief"]


async def test_a_handled_update_produces_no_customer_draft():
    """The staff member asked for a record change, not a reply to the customer."""
    registry = _UpdateRegistry()

    result = await _run_update(registry, message=UPDATE_MESSAGE)

    assert result["output"]["draft_response"] is None
    assert result["output"]["action_required"] is None


async def test_an_inbound_customer_message_never_updates_its_own_record():
    """A customer is not taken as instructing us to rewrite their identity."""
    registry = _UpdateRegistry()

    result = await _run_update(registry, message=UPDATE_MESSAGE, staff_query=False)

    assert registry.updates == []
    assert result["output"]["draft_response"] is not None


async def test_a_run_with_no_customer_context_at_all_updates_nothing():
    """No customer id and no phone: the run has nobody to act on and skips."""
    registry = _UpdateRegistry()
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": None,
            "phone_number": None,
            "message": UPDATE_MESSAGE,
            "channel": "whatsapp",
            "staff_query": True,
        }
    )

    assert registry.updates == []
    assert result["status"] == "skipped"


async def test_the_update_node_refuses_when_no_customer_is_bound():
    """The defensive guard, exercised directly: it is unreachable through the graph.

    ``resolve_customer`` either resolves a customer from the phone or skips the run entirely, so by
    the time this node runs a customer id exists. The guard stays because a future change to that
    resolution could otherwise let a chat message edit an arbitrary record.
    """
    from app.agents.customer_memory.nodes import CustomerMemoryAgent

    agent = CustomerMemoryAgent(_UpdateRegistry())
    result = await agent.apply_staff_update(
        {
            "org_id": "org-1",
            "customer_id": None,
            "message": UPDATE_MESSAGE,
            "staff_query": True,
        }
    )

    assert "could not tell which customer" in result["customer_update_error"]


async def test_a_duplicate_phone_is_explained_rather_than_silently_dropped():
    registry = _UpdateRegistry(fail_with=409)

    result = await _run_update(registry, message=UPDATE_MESSAGE)

    brief = result["output"]["interaction_brief"]
    assert "already has" in brief
    # The staff member needs to know it did NOT apply.
    assert result["output"]["draft_response"] is None


async def test_a_missing_customer_is_explained():
    registry = _UpdateRegistry(fail_with=404)

    result = await _run_update(registry, message=UPDATE_MESSAGE)

    assert "not in this boutique" in result["output"]["interaction_brief"]


async def test_an_ordinary_message_does_not_touch_the_customer_book():
    registry = _UpdateRegistry()

    await _run_update(registry, message="Do you have anything pink?", staff_query=True)

    assert registry.updates == []


async def test_staff_query_surfaces_what_is_actually_on_file():
    """A question about the customer must reflect the memories the boutique has stored.

    The interaction brief carries preferences, dated events and tags - not semantic memories. So
    "what do we have on file for her?" answered "nothing on file yet" while memories sat in the
    store.

    They now travel in their own field rather than inside the sentence. Reciting them inline is what
    produced "on file: The customer has a party; The customer has a party; Kasha vivian has a party"
    in a real thread: a list in a sentence, repeating every near-duplicate the store had
    accumulated. The sentence summarises; the publisher renders the notes as a content block.
    """
    registry = FakeRegistry()

    async def brief(org_id, customer_id):
        # The brief knows nothing, exactly as the backend does for a customer whose only facts are
        # undated memories.
        return {
            "customerName": "Kasha Vivian",
            "status": "new",
            "upcomingEvents": None,
            "preferenceSummary": None,
            "tags": [],
        }

    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": "cust-kasha",
            "message": "what do we have on file for her?",
            "intent_type": "general_inquiry",
            "channel": "whatsapp",
            "direction": None,
            "staff_query": True,
        }
    )

    brief_text = result["output"]["interaction_brief"]
    # FakeRegistry.get_customer_memories returns "Prefers emerald silk".
    assert "emerald silk" not in brief_text, "the brief must summarise, not recite"
    assert "One note is on file." in brief_text
    assert result["output"]["memories_on_file"] == [
        {"content": "Prefers emerald silk", "category": "preference"}
    ]
    assert "nothing on file yet" not in brief_text


async def test_purchase_intent_does_not_fail_the_memory_agent():
    """The reported symptom: a purchase-intent message made Ava go silent.

    The gate emits `order_placement` for "purchase"/"buy"/"order", but the memory output schema did
    not allow it, so `MemoryAgentOutput` validation failed and the agent emitted an error instead of
    a brief - which is why the thread showed Aveline's summary and no Ava block for
    "Is the emerald green saree available for purchase?".
    """
    registry = FakeRegistry()

    result = await _run_memory(
        registry,
        customer_id="cust-kasha",
        phone=None,
        message="Is the emerald green saree available for purchase?",
        intent_type="order_placement",
    )

    assert result["status"] == "success", f"memory agent errored: {result.get('reason')}"
    output = result["output"]
    assert output["status"] == "success"
    assert output["parsed_intent"]["intent_type"] == "order_placement"
    assert output["interaction_brief"]


async def test_customer_name_matches_the_brief_when_the_resolver_took_the_fast_path():
    """Aveline's summary names the customer; it must not say "The customer" beside a named brief.

    When the conversation already carries a customer id, the resolver's fast path returns no
    profile, so `full_name` had nothing to fall back on but the placeholder - while the backend
    brief named them. Both appear in one message, so the mismatch was visible to staff.
    """
    registry = FakeRegistry()

    async def brief(org_id, customer_id):
        return {"customerName": "Kasha Vivian Perera", "status": "new", "tags": []}

    registry.generate_interaction_brief = brief

    result = await _run_memory(
        registry,
        customer_id="cust-kasha",
        phone=None,
        message="Do you still have the emerald green saree?",
    )

    assert result["output"]["customer"]["full_name"] == "Kasha Vivian Perera"


# ---------------------------------------------------------------------------
# Conversation-context transport (ADR-023) — the window must reach the draft prompt.
# ---------------------------------------------------------------------------


class _CapturingChatModel:
    """A chat-model double that records every message list it is invoked with."""

    def __init__(self) -> None:
        self.calls: list[list] = []

    async def ainvoke(self, messages):
        self.calls.append(messages)
        return _Msg("Ava LLM draft.")

    def prompt_text(self) -> str:
        return "\n".join(
            getattr(message, "content", None) or str(message) for message in self.calls[0]
        )


async def test_history_reaches_the_draft_prompt():
    """A follow-up such as "the pink one" must carry its referent into the model.

    This fails for *both* missing halves: the state schema must declare ``history`` (LangGraph
    drops undeclared keys silently) and the node must render it. It is the load-bearing test for
    the schema half of the defect.
    """
    registry = FakeRegistry()
    llm = _CapturingChatModel()
    graph = build_memory_graph(registry, llm=llm)

    state = await _llm_state()
    state["message"] = "the pink one"
    state["history"] = [
        {"authorKind": "Customer", "text": "Any pinkish gowns?"},
        {"authorKind": "Agent", "text": "We have three."},
    ]
    state["thread_summary"] = "She is shopping for a December wedding."
    state["pinned_slots"] = {"budget": "50k"}

    result = await graph.ainvoke(state)

    assert result["status"] == "success"
    assert llm.calls, "the LLM was never invoked, so there is no prompt to inspect"
    prompt = llm.prompt_text()
    assert "Any pinkish gowns?" in prompt
    assert "December wedding" in prompt
    assert "budget: 50k" in prompt


async def test_history_is_dropped_without_an_llm_but_the_run_still_succeeds():
    """The deterministic path must be unchanged: no LLM, no context render, same template draft."""
    registry = FakeRegistry()
    graph = build_memory_graph(registry, llm=None)

    state = await _llm_state()
    state["history"] = [{"authorKind": "Customer", "text": "Any pinkish gowns?"}]

    result = await graph.ainvoke(state)

    assert result["status"] == "success"
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")


async def test_dialogue_context_is_not_rendered_without_an_llm(monkeypatch):
    """No LLM means no prompt, so the block must not be computed at all (no wasted work)."""
    calls: list[dict] = []
    monkeypatch.setattr(
        "app.agents.customer_memory.nodes.render_context_block",
        lambda **kwargs: calls.append(kwargs) or "",
    )

    registry = FakeRegistry()
    graph = build_memory_graph(registry, llm=None)
    state = await _llm_state()
    state["history"] = [{"authorKind": "Customer", "text": "Any pinkish gowns?"}]

    await graph.ainvoke(state)

    assert calls == []


async def test_stored_notes_are_collapsed_in_the_staff_answer():
    """The repetition in a real thread: the store held the same fact four times.

    "Kasha Vivian Pera is a new customer - on file: The customer has a party; The customer has a
    party; The customer has a party; Kasha vivian has a party." The store has no write-time
    de-duplication, so the answer repeated every row. The duplicates are collapsed, and the count
    in the sentence matches the notes the block shows.
    """
    registry = FakeRegistry()

    async def memories(org_id, customer_id, query, top_k=5):
        return [
            {"content": "The customer has a party", "category": "event"},
            {"content": "The customer has a party", "category": "event"},
            {"content": "The customer has a party", "category": "event"},
            {"content": "  the customer has a party.  ", "category": "event"},
        ]

    registry.get_customer_memories = memories

    async def brief(org_id, customer_id):
        return {
            "customerName": "Kasha Vivian Pera",
            "status": "new",
            "upcomingEvents": None,
            "preferenceSummary": None,
            "tags": [],
        }

    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": "cust-kasha",
            "message": "what do we know about this customer",
            "intent_type": "general_inquiry",
            "channel": "whatsapp",
            "direction": None,
            "staff_query": True,
        }
    )

    assert result["output"]["interaction_brief"] == (
        "Kasha Vivian Pera is a new customer. One note is on file."
    )
    assert result["output"]["memories_on_file"] == [
        {"content": "The customer has a party", "category": "event"}
    ]


async def test_the_staff_answer_counts_the_notes_it_is_about_to_show():
    registry = FakeRegistry()

    async def memories(org_id, customer_id, query, top_k=5):
        return [
            {"content": "Prefers emerald silk", "category": "preference"},
            {"content": "Has a party on 2026-12-01", "category": "event"},
        ]

    registry.get_customer_memories = memories

    async def brief(org_id, customer_id):
        return {
            "customerName": "Nadia",
            "status": "returning",
            "upcomingEvents": None,
            "preferenceSummary": None,
            "tags": [],
        }

    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": "cust-nadia",
            "message": "what do we know about this customer",
            "intent_type": "general_inquiry",
            "channel": "whatsapp",
            "direction": None,
            "staff_query": True,
        }
    )

    assert result["output"]["interaction_brief"] == "Nadia (returning). 2 notes are on file."
    assert len(result["output"]["memories_on_file"]) == 2
