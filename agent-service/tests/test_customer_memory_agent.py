"""Tests for the Customer Memory Agent sub-graph (app/agents/customer_memory/).

Drives the compiled LangGraph with a fake ``ToolRegistry`` so behavior is asserted with plain,
rule-based checks (no LLM, no live backend). Covers the golden cases: a wedding inquiry with a
resolved, consenting customer; revoked consent short-circuiting; a missing customer context; and
explicit preference extraction.
"""


import json

import httpx

from app.agents.customer_memory.graph import build_memory_graph
from app.agents.customer_memory.parsing import parse_message
from app.events.block_builders import build_ava_blocks


class FakeRegistry:
    """Records calls and returns canned backend responses."""

    def __init__(self) -> None:
        self.saved_memories: list[dict] = []
        self.saved_preferences: list[dict] = []
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
        self.search_min_similarity: float | None = None
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

    async def get_customer_memories(self, org_id, customer_id, query, top_k=5, min_similarity=0.0):
        if self.memories_raise:
            raise RuntimeError("semantic search backend unavailable")
        self.search_calls += 1
        self.search_min_similarity = min_similarity
        return [
            {
                "id": "mem-old",
                "content": "Prefers emerald silk",
                "category": "preference",
                "source": "conversation",
                "isExplicit": True,
                "confidence": 0.9,
                "similarity": 0.98,
            }
        ]

    async def save_customer_memory(
        self,
        org_id,
        customer_id,
        content,
        category,
        source=None,
        is_explicit=None,
        confidence=None,
        metadata_json=None,
    ):
        self.saved_memories.append(
            {
                "customer_id": customer_id,
                "content": content,
                "category": category,
                "source": source,
                "is_explicit": is_explicit,
                "confidence": confidence,
            }
        )
        return {"id": f"mem-{len(self.saved_memories)}"}

    async def save_customer_preference(
        self, org_id, customer_id, key, value, source=None, is_explicit=None, confidence=None
    ):
        self.saved_preferences.append(
            {
                "customer_id": customer_id,
                "key": key,
                "value": value,
                "source": source,
                "is_explicit": is_explicit,
                "confidence": confidence,
            }
        )
        return {"id": f"pref-{len(self.saved_preferences)}"}

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
    event_memories = [m for m in registry.saved_memories if m["category"] == "event"]
    assert any("wedding" in m["content"] for m in event_memories)
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

    pref_memories = [m for m in registry.saved_memories if m["category"] == "preference"]
    assert pref_memories, "expected at least one preference memory to be saved"
    assert any("silk" in m["content"].lower() for m in pref_memories)
    assert result["status"] == "success"


async def test_negative_preference_saved_as_dislikes():
    registry = FakeRegistry()
    await _run(registry, message="I don't like flashy blouses at all.")

    pref_memories = [m for m in registry.saved_memories if m["category"] == "preference"]
    assert any("dislikes flashy" in m["content"].lower() for m in pref_memories)


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
        #: Everything the agent sent, so a test can assert what the prompt promised.
        self.messages: list = []

    async def ainvoke(self, messages):
        self.messages.append(messages)
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
    # Token usage captured from the LLM responses (surfaced on state, not the schema output). An
    # inbound turn now makes two calls - fact extraction, then the draft - and ADR-010 reports one
    # figure per run, so the two are summed rather than the last one winning. This fake answers both
    # calls with the same reply, so each contributes its 12/6.
    assert result["usage"] == {"input_tokens": 24, "output_tokens": 12}


async def test_llm_usage_surfaces_the_cached_token_direction():
    """Slice 4a — cached prompt tokens ride on state so the additive metric can observe them."""
    registry = FakeRegistry()
    llm = FakeChatModel(draft="Ava LLM draft.", cached_tokens=64)
    graph = build_memory_graph(registry, llm=llm)
    result = await graph.ainvoke(await _llm_state())

    # Both calls (extraction + draft) report the cached direction, so the run's sum carries 128.
    assert result["usage"]["cached_tokens"] == 128
    assert result["usage"]["input_tokens"] == 24


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
    assert any(m["category"] == "event" for m in registry.saved_memories)


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
    assert any(m["category"] == "event" for m in registry.saved_memories)


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
        {
            "content": "Prefers emerald silk",
            "category": "preference",
            # Provenance now travels with the note, so the block can say a fact was stated rather
            # than inferred (gaps B2, B3).
            "source": "conversation",
            "is_explicit": True,
            "confidence": 0.9,
            "similarity": 0.98,
        }
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

    def prompt_text(self, index: int = -1) -> str:
        """One call's rendered prompt (the newest by default).

        The graph makes up to two calls per inbound turn now - fact extraction, then the draft - so
        "the prompt" is ambiguous and the caller has to say which call it means. The default is the
        newest one, which is the draft.
        """
        return "\n".join(
            getattr(message, "content", None) or str(message) for message in self.calls[index]
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

    async def memories(org_id, customer_id, query, top_k=5, min_similarity=0.0):
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

    async def memories(org_id, customer_id, query, top_k=5, min_similarity=0.0):
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


# ---------------------------------------------------------------------------
# A staff instruction to record a note
# ---------------------------------------------------------------------------
#
# The headline defect: staff typed "Please add a note for this customer: He prefers green tea over
# coffee" and got the ordinary customer brief, with no memory written and no explanation. There was
# no path from a staff instruction to a memory at all.

NOTE_MESSAGE = "Please add a note for this customer: He prefers green tea over coffee"


class _FailingNoteRegistry(_UpdateRegistry):
    """A registry whose memory store is down, to prove a failed note write is reported."""

    async def save_customer_memory(self, *args, **kwargs):
        raise RuntimeError("memory store unavailable")


async def test_a_staff_note_instruction_is_stored_and_confirmed():
    registry = _UpdateRegistry()

    result = await _run_update(registry, message=NOTE_MESSAGE)

    # The customer's words reach the store as the staff member wrote them, with staff provenance.
    assert registry.saved_memories == [
        {
            "customer_id": "cust-kasha",
            "content": "He prefers green tea over coffee",
            "category": "note",
            "source": "staff",
            "is_explicit": True,
            "confidence": 1.0,
        }
    ]
    # A note is not a rename: no identity field was touched.
    assert registry.updates == []

    brief = result["output"]["interaction_brief"]
    assert brief == "Noted for this customer: He prefers green tea over coffee."
    assert "Nothing was changed" not in brief
    # Still no customer-facing draft / Suggestion: this was an instruction, not a customer message.
    assert result["output"]["draft_response"] is None
    assert result["output"]["action_required"] is None


async def test_a_note_keeps_its_own_punctuation_without_doubling_it():
    registry = _UpdateRegistry()

    result = await _run_update(registry, message="add a note: she prefers green tea.")

    assert [m["content"] for m in registry.saved_memories] == ["she prefers green tea."]
    assert result["output"]["interaction_brief"] == "Noted for this customer: she prefers green tea."


async def test_a_note_instruction_without_a_customer_is_refused_not_guessed():
    """The bound-customer rule holds for notes exactly as it does for identity updates."""
    from app.agents.customer_memory.nodes import CustomerMemoryAgent

    registry = _UpdateRegistry()
    agent = CustomerMemoryAgent(registry)

    result = await agent.apply_staff_update(
        {
            "org_id": "org-1",
            "customer_id": None,
            "message": NOTE_MESSAGE,
            "staff_query": True,
        }
    )

    assert "could not tell which customer" in result["customer_update_error"]
    assert registry.saved_memories == []


async def test_an_inbound_customer_message_never_records_a_staff_note():
    registry = _UpdateRegistry()

    result = await _run_update(registry, message=NOTE_MESSAGE, staff_query=False)

    assert registry.saved_memories == []
    # The customer's own message still gets a customer-facing draft.
    assert result["output"]["draft_response"] is not None


async def test_a_failed_note_write_is_reported_and_never_silently_dropped():
    registry = _FailingNoteRegistry()

    result = await _run_update(registry, message=NOTE_MESSAGE)

    brief = result["output"]["interaction_brief"]
    assert "nothing was noted" in brief.lower()
    assert "Noted for this customer" not in brief


async def test_a_note_and_an_identity_change_in_one_instruction_both_apply():
    registry = _UpdateRegistry()

    result = await _run_update(
        registry,
        message=(
            "Please update this customer with the name Kasha Vivian Perera and number 0771234567 "
            "and add a note: she prefers green tea"
        ),
    )

    assert registry.updates == [("org-1", "cust-kasha", "Kasha Vivian Perera", "0771234567")]
    assert [m["content"] for m in registry.saved_memories] == ["she prefers green tea"]
    brief = result["output"]["interaction_brief"]
    assert "Kasha Vivian Perera" in brief
    assert "Noted for this customer: she prefers green tea." in brief


# ---------------------------------------------------------------------------
# The customer description leads the brief
# ---------------------------------------------------------------------------
#
# The backend interaction brief carries `description`, the customer-level prose the brief exists to
# surface. The agent fetched it and never read it, so a staff answer led with a mechanical fact dump.


BRIEF_WITH_DESCRIPTION = {
    "customerName": "Sarah Perera",
    "status": "vip",
    "description": "Sarah is a regular who loves emerald silk.",
    "preferenceSummary": None,
    "upcomingEvents": "wedding on 2026-12-01",
    "tags": ["vip"],
}


async def test_the_inbound_brief_leads_with_the_customer_description():
    registry = FakeRegistry()

    async def brief(org_id, customer_id):
        return dict(BRIEF_WITH_DESCRIPTION)

    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(await _llm_state())

    assert result["output"]["interaction_brief"] == (
        "Sarah is a regular who loves emerald silk. | "
        "Sarah Perera (vip) | Context: Prefers emerald silk | "
        "Upcoming: wedding on 2026-12-01 | Tags: vip"
    )


async def test_the_inbound_brief_is_unchanged_without_a_description():
    """No description means exactly the brief the agent produced before the field was read."""
    registry = FakeRegistry()
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(await _llm_state())

    assert result["output"]["interaction_brief"] == (
        "Sarah Perera (vip) | Context: Prefers emerald silk | "
        "Upcoming: wedding on 2026-12-01 | Tags: vip"
    )


async def test_a_note_is_refused_for_a_customer_who_revoked_consent():
    """Consent is a property of the customer, not of who is asking.

    ``apply_staff_update`` runs *before* ``check_consent`` and short-circuits the run when it
    answers, so without its own guard a staff instruction would store a note - or apply a rename -
    for a customer who had revoked. ``persist`` already refuses in that case, and the documented
    contract is that revocation means nothing is stored.
    """
    registry = _UpdateRegistry()
    registry.consent_status = "revoked"

    result = await _run_update(registry, message=NOTE_MESSAGE)

    assert registry.saved_memories == []
    assert registry.updates == []

    brief = result["output"]["interaction_brief"]
    assert "revoked consent" in brief
    assert "Noted for this customer" not in brief


async def test_an_update_is_refused_when_consent_cannot_be_read():
    """A privacy control that cannot read its own state must not write (fail closed)."""
    registry = _UpdateRegistry()
    registry.consent_error = RuntimeError("consent backend unavailable")

    result = await _run_update(registry, message=UPDATE_MESSAGE)

    assert registry.updates == []
    assert registry.saved_memories == []
    assert "could not check" in result["output"]["interaction_brief"]


async def test_the_staff_answer_leads_with_the_customer_description():
    registry = FakeRegistry()

    async def brief(org_id, customer_id):
        return {
            "customerName": "Sarah Perera",
            "status": "returning",
            "description": "Sarah is a regular who loves emerald silk.",
            "preferenceSummary": "colour: emerald",
            "upcomingEvents": None,
            "tags": [],
        }

    registry.generate_interaction_brief = brief
    graph = build_memory_graph(registry)

    result = await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": "cust-sarah",
            "customer_name": "Sarah Perera",
            "message": "what do we know about @sarah?",
            "intent_type": "general_inquiry",
            "channel": "whatsapp",
            "direction": None,
            "staff_query": True,
        }
    )

    assert result["output"]["interaction_brief"] == (
        "Sarah is a regular who loves emerald silk. "
        "Sarah Perera (returning) - preferences: colour: emerald. One note is on file."
    )


# ---------------------------------------------------------------------------
# A staff query is answered by the model, with the template as the offline fallback
# ---------------------------------------------------------------------------


STAFF_STATE = {
    "org_id": "org-1",
    "customer_id": "cust-sarah",
    "customer_name": "Sarah Perera",
    "message": "what do we have on file for her?",
    "intent_type": "general_inquiry",
    "channel": "whatsapp",
    "direction": None,
    "staff_query": True,
}


async def test_a_staff_query_is_answered_by_the_model_when_one_is_configured():
    registry = FakeRegistry()
    llm = FakeChatModel(draft="Sarah has one note on file: she prefers emerald silk.")
    graph = build_memory_graph(registry, llm=llm)

    result = await graph.ainvoke(dict(STAFF_STATE))

    assert result["status"] == "success"
    out = result["output"]
    assert out["interaction_brief"] == "Sarah has one note on file: she prefers emerald silk."
    # Token usage reaches the same state path the customer draft reports on (ADR-010).
    assert result["usage"] == {"input_tokens": 12, "output_tokens": 6}
    # Still NO customer-facing draft / Suggestion: the staff/customer separation is deliberate.
    assert out["draft_response"] is None
    assert out["action_required"] is None


async def test_the_staff_answer_prompt_fences_the_record_from_the_conversation():
    """A recorded fact must not be confused with something merely said in the thread.

    Observed live: a staff question in a customer thread answered with the customer's own words
    ("prefers cotton, avoiding nylon and spandex") as though they were on file, while the record
    block beside it held only an event. A second thread called that event "the only preference we
    hold". The prompt must therefore name every record source, fence it, and say plainly that the
    conversation excerpt is not the record.
    """
    registry = FakeRegistry()
    llm = FakeChatModel(draft="Sarah prefers emerald silk.")
    graph = build_memory_graph(registry, llm=llm)

    await graph.ainvoke(dict(STAFF_STATE))

    prompt = "\n".join(str(message.content) for message in llm.messages[0])

    assert "ON FILE" in prompt
    # Each source is named, so a row cannot be silently reclassified on the way out.
    assert "- Upcoming events:" in prompt
    assert "- Tags:" in prompt
    assert "- Recorded notes:" in prompt
    # And the transcript is explicitly not the record.
    assert "it is NOT the record" in prompt
    assert "an upcoming event is not a preference" in prompt


async def test_a_preference_and_an_event_are_labelled_apart_in_the_staff_prompt():
    """The exact conflation observed live: an event reported back as a preference.

    The thread held one upcoming event and no preferences, and the answer said "the only preference
    we hold is a wedding". With a preference summary present as well, the two must arrive as
    separate labelled lines rather than one unlabelled facts blob.
    """
    registry = FakeRegistry()

    async def brief(org_id, customer_id):
        return {
            "customerId": customer_id,
            "customerName": "Sarah Perera",
            "status": "vip",
            "preferenceSummary": "colour: emerald",
            "upcomingEvents": "wedding on 2026-12-01",
            "tags": [],
        }

    registry.generate_interaction_brief = brief
    llm = FakeChatModel(draft="She prefers emerald and has a wedding on 2026-12-01.")
    graph = build_memory_graph(registry, llm=llm)

    await graph.ainvoke(dict(STAFF_STATE))

    prompt = "\n".join(str(message.content) for message in llm.messages[0])

    assert "- Preferences: colour: emerald" in prompt
    assert "- Upcoming events: wedding on 2026-12-01" in prompt


async def test_a_staff_query_keeps_the_grounded_template_without_an_llm():
    """The offline guarantee: no model configured means today's template answer, byte for byte."""
    registry = FakeRegistry()
    graph = build_memory_graph(registry)  # llm defaults to None

    result = await graph.ainvoke(dict(STAFF_STATE))

    out = result["output"]
    assert out["interaction_brief"] == (
        "Sarah Perera (vip) - upcoming events: wedding on 2026-12-01; tags: vip. "
        "One note is on file."
    )
    assert out["draft_response"] is None
    assert result.get("usage") is None


async def test_a_staff_query_falls_back_to_the_template_when_the_provider_fails():
    registry = FakeRegistry()
    graph = build_memory_graph(registry, llm=FakeChatModel(fail=True))

    result = await graph.ainvoke(dict(STAFF_STATE))

    out = result["output"]
    assert out["interaction_brief"] == (
        "Sarah Perera (vip) - upcoming events: wedding on 2026-12-01; tags: vip. "
        "One note is on file."
    )
    assert out["draft_response"] is None
    assert result.get("usage") is None


# ---------------------------------------------------------------------------
# Model-driven fact extraction - the "nitbits"
# ---------------------------------------------------------------------------
#
# The deterministic parse covers three shapes and only three: a first-person preference, a dated
# event, and a quoted complaint. Everything else a customer says that is worth keeping matched no
# shape and was never stored at all. The live thread behind this feature: "nothing nylon, or
# spandex, I dont like them, they make my skin itchy" (the object is the pronoun "them", so the
# preference signal is dropped) and "browsing a brown dress seen on Instagram" (an observation, no
# shape at all). A model reading the message can resolve the reference and record the fact; the
# transcript is handed to it for that resolution only, because retrieval must find a stored fact
# regardless of which conversation is asking.


class ScriptedChatModel:
    """A chat-model double that answers each call with the next scripted reply.

    The graph makes up to two calls per inbound turn (fact extraction, then the draft), and one fixed
    reply cannot exercise both: the extraction would be handed draft prose and the draft JSON.
    Scripting by call keeps each test's intent - and its token arithmetic - visible.
    """

    def __init__(self, replies, *, fail=False, usages=None):
        self.replies = list(replies)
        self.fail = fail
        self.usages = list(usages) if usages is not None else [(12, 6)] * len(self.replies)
        self.calls: list[list] = []

    async def ainvoke(self, messages):
        index = len(self.calls)
        self.calls.append(messages)
        if self.fail:
            raise RuntimeError("provider unavailable")
        reply = self.replies[min(index, len(self.replies) - 1)]
        input_tokens, output_tokens = self.usages[min(index, len(self.usages) - 1)]
        return _Msg(reply, input_tokens=input_tokens, output_tokens=output_tokens)

    def prompt_text(self, index: int = -1) -> str:
        """One call's rendered prompt (the newest by default)."""
        return "\n".join(
            getattr(message, "content", None) or str(message) for message in self.calls[index]
        )


def _facts_reply(*facts) -> str:
    """A model extraction reply carrying exactly ``facts``."""
    return json.dumps({"facts": list(facts)})


def _fact(content, category="preference", stated=True, confidence=0.9, **extra):
    return {"content": content, "category": category, "stated": stated, "confidence": confidence, **extra}


NITBITS_MESSAGE = "nothing nylon, or spandex, I dont like them"


async def _run_extraction(registry, llm, *, message=NITBITS_MESSAGE, staff_query=False, **state_extra):
    graph = build_memory_graph(registry, llm=llm)
    state = {
        "org_id": "org-1",
        "customer_id": "cust-sarah",
        "customer_name": "Sarah Perera",
        "message": message,
        "intent_type": "general_inquiry",
        "channel": "whatsapp",
        "direction": None if staff_query else "inbound",
        "staff_query": staff_query,
    }
    state.update(state_extra)
    return await graph.ainvoke(state)


def test_the_memory_state_declares_the_extraction_outputs():
    """LangGraph drops undeclared keys silently, so this is guarded like the context transport.

    Without the declaration the extract node would run, be paid for, and its facts would reach
    neither `persist` (so nothing is stored) nor `compose_output` (so the tokens vanish from the
    usage report) - a failure no mocked-sub-graph test can see.
    """
    from app.agents.customer_memory.state import MemoryAgentState

    assert "_extracted_facts" in MemoryAgentState.__annotations__
    assert "_extraction_usage" in MemoryAgentState.__annotations__


async def test_extraction_resolves_a_pronoun_into_self_contained_facts():
    """The reported case: "I dont like them" - the regex cannot know what "them" is, the model can."""
    registry = FakeRegistry()
    llm = ScriptedChatModel(
        [
            _facts_reply(
                _fact("Sarah Perera dislikes nylon"),
                _fact("Sarah Perera dislikes spandex"),
            ),
            "Hi Sarah! Noted.",
        ]
    )

    result = await _run_extraction(registry, llm, message=NITBITS_MESSAGE)

    contents = [m["content"] for m in registry.saved_memories]
    assert contents == ["Sarah Perera dislikes nylon", "Sarah Perera dislikes spandex"]
    # The fact stands alone: nothing stored refers to "them", so another conversation can retrieve it.
    assert "them" not in " ".join(contents).lower()
    assert result["status"] == "success"


async def test_an_observation_is_inferred_and_a_stated_fact_is_stated():
    """`stated` decides the Known column, so an observation is not dressed as the customer's words."""
    registry = FakeRegistry()
    llm = ScriptedChatModel(
        [
            _facts_reply(
                _fact(
                    "Sarah Perera browsed a brown dress seen on Instagram",
                    category="observation",
                    stated=False,
                    confidence=0.6,
                ),
                _fact("Sarah Perera prefers cotton", category="preference", stated=True),
            ),
            "Hi Sarah!",
        ]
    )

    result = await _run_extraction(registry, llm)

    table = next(b for b in build_ava_blocks(result["output"]) if b["type"] == "at_a_glance")
    assert [
        "Observation",
        "Sarah Perera browsed a brown dress seen on Instagram",
        "Inferred",
    ] in table["rows"]
    assert ["Preference", "Sarah Perera prefers cotton", "Stated"] in table["rows"]


async def test_no_llm_performs_no_extraction_and_writes_only_the_deterministic_memories(monkeypatch):
    """The offline guarantee (I3): keyless/CI runs call no extraction and write exactly as before."""
    built_prompts: list[dict] = []
    monkeypatch.setattr(
        "app.agents.customer_memory.nodes.render_context_block",
        lambda **kwargs: built_prompts.append(kwargs) or "",
    )

    registry = FakeRegistry()
    result = await _run(registry)  # llm defaults to None

    # No prompt is assembled for extraction (or for the draft) without a model, so no call is made.
    assert built_prompts == []
    assert registry.saved_memories == [
        {
            "customer_id": "cust-sarah",
            "content": "Sarah Perera has a wedding",
            "category": "event",
            "source": "conversation",
            "is_explicit": True,
            "confidence": 0.9,
        }
    ]
    assert result.get("_extracted_facts") in (None, [])
    assert result.get("_extraction_usage") is None


async def test_extraction_never_fails_the_run_on_a_provider_failure_or_a_prose_reply():
    failed = FakeRegistry()
    result = await _run_extraction(failed, ScriptedChatModel([_facts_reply()], fail=True))

    assert result["status"] == "success"
    assert failed.saved_memories == []
    # The deterministic draft template still answered.
    assert result["output"]["draft_response"].startswith("Hi Sarah Perera!")

    prose = FakeRegistry()
    result = await _run_extraction(
        prose, ScriptedChatModel(["I could not find any facts to record.", "Hi Sarah!"])
    )

    assert result["status"] == "success"
    assert prose.saved_memories == []


def test_the_fact_parser_drops_anything_malformed():
    from app.agents.customer_memory.nodes import parse_extracted_facts

    assert parse_extracted_facts("not json at all") == []
    assert parse_extracted_facts(None) == []
    assert parse_extracted_facts('{"facts": "not a list"}') == []
    assert parse_extracted_facts(json.dumps({"facts": ["not an object"]})) == []
    # An unknown category is dropped rather than stored under a taxonomy a model invented.
    assert parse_extracted_facts(_facts_reply(_fact("A", category="style_note"))) == []
    # Empty and over-long sentences are not facts.
    assert parse_extracted_facts(_facts_reply(_fact(""))) == []
    assert parse_extracted_facts(_facts_reply(_fact("x" * 400))) == []
    # A non-boolean `stated` is not a statement; it takes the honest default.
    parsed = parse_extracted_facts(_facts_reply(_fact("A", stated="yes")))
    assert parsed[0]["stated"] is False


def test_the_fact_parser_keeps_only_the_closed_category_vocabulary():
    from app.agents.customer_memory.nodes import parse_extracted_facts

    for category in ("preference", "event", "observation", "complaint", "constraint"):
        parsed = parse_extracted_facts(_facts_reply(_fact("A", category=category)))
        assert parsed[0]["category"] == category
    # Category matching is case-insensitive, because a model capitalising it is not an error.
    assert parse_extracted_facts(_facts_reply(_fact("A", category="Preference")))[0]["category"] == (
        "preference"
    )


def test_the_fact_parser_clamps_confidence_and_caps_the_turn():
    from app.agents.customer_memory.nodes import MAX_EXTRACTED_FACTS, parse_extracted_facts

    high = parse_extracted_facts(_facts_reply(_fact("A", confidence=1.7)))[0]
    low = parse_extracted_facts(_facts_reply(_fact("A", confidence=-3)))[0]
    missing = parse_extracted_facts(_facts_reply(_fact("A", confidence="very")))[0]
    assert high["confidence"] == 1.0
    assert low["confidence"] == 0.0
    assert missing["confidence"] == 0.5

    many = _facts_reply(*[_fact(f"fact {index}") for index in range(9)])
    assert len(parse_extracted_facts(many)) == MAX_EXTRACTED_FACTS


def test_the_fact_parser_keeps_a_preference_key_value_pair_and_accepts_a_code_fence():
    from app.agents.customer_memory.nodes import parse_extracted_facts

    fenced = (
        "```json\n"
        + _facts_reply(_fact("Sarah Perera prefers cotton", key="fabric", value="cotton"))
        + "\n```"
    )

    parsed = parse_extracted_facts(fenced)

    assert parsed[0]["key"] == "fabric"
    assert parsed[0]["value"] == "cotton"
    # A key/value pair is meaningless on another category, so it is not carried there.
    observation = parse_extracted_facts(
        _facts_reply(
            _fact("Sarah Perera browsed a dress", category="observation", key="fabric", value="cotton")
        )
    )
    assert "key" not in observation[0]


async def test_a_fact_the_regex_already_wrote_is_not_written_twice():
    """The model is asked for the same small facts the regexes target, so they will overlap."""
    registry = FakeRegistry()
    llm = ScriptedChatModel(
        [
            _facts_reply(_fact("Sarah Perera prefers silk sarees")),
            "Hi Sarah!",
        ]
    )

    await _run_extraction(registry, llm, message="I really like silk sarees, please.")

    matching = [
        m for m in registry.saved_memories if m["content"] == "Sarah Perera prefers silk sarees"
    ]
    assert len(matching) == 1


async def test_an_extracted_preference_mirrors_into_the_preferences_table():
    """A note the brief's summary knows nothing about is the disagreement the mirror closes."""
    registry = FakeRegistry()
    llm = ScriptedChatModel(
        [
            _facts_reply(
                _fact(
                    "Sarah Perera prefers cotton",
                    confidence=0.8,
                    key="fabric",
                    value="cotton",
                )
            ),
            "Hi Sarah!",
        ]
    )

    result = await _run_extraction(registry, llm)

    assert registry.saved_preferences == [
        {
            "customer_id": "cust-sarah",
            "key": "fabric",
            "value": "cotton",
            "source": "conversation",
            "is_explicit": True,
            "confidence": 0.8,
        }
    ]
    assert result["output"]["extracted_memories"][0]["confidence"] == 0.8


async def test_a_staff_query_performs_no_extraction():
    """A question about a customer is not a customer message, so there is nothing to extract from."""
    registry = FakeRegistry()
    llm = ScriptedChatModel(["Sarah Perera is a vip."])

    result = await _run_extraction(
        registry, llm, message="what do we know about Sarah?", staff_query=True
    )

    assert len(llm.calls) == 1, "only the staff answer should have called the model"
    assert result.get("_extracted_facts") in (None, [])
    assert registry.saved_memories == []


async def test_extraction_and_draft_token_usage_are_summed():
    """ADR-010 reports one figure per run, and an inbound LLM turn now makes two calls."""
    registry = FakeRegistry()
    llm = ScriptedChatModel(
        [
            _facts_reply(_fact("Sarah Perera dislikes nylon")),
            "Hi Sarah!",
        ],
        usages=[(10, 4), (12, 6)],
    )

    result = await _run_extraction(registry, llm)

    assert result["usage"] == {"input_tokens": 22, "output_tokens": 10}


async def test_a_single_call_still_reports_only_its_own_usage():
    registry = FakeRegistry()
    llm = ScriptedChatModel(["Sarah Perera is a vip."], usages=[(7, 3)])

    result = await _run_extraction(
        registry, llm, message="what do we know about Sarah?", staff_query=True
    )

    assert result["usage"] == {"input_tokens": 7, "output_tokens": 3}


async def test_the_extraction_prompt_carries_the_name_message_and_a_labelled_transcript():
    registry = FakeRegistry()
    llm = ScriptedChatModel([_facts_reply(), "Hi Sarah!"])

    await _run_extraction(
        registry,
        llm,
        message="the pink one",
        history=[{"authorKind": "Customer", "text": "Any pinkish gowns?"}],
        thread_summary="She is shopping for a December wedding.",
        pinned_slots={"budget": "50k"},
    )

    prompt = llm.prompt_text(index=0)
    assert "Sarah Perera" in prompt
    assert "the pink one" in prompt
    assert "Any pinkish gowns?" in prompt
    assert "December wedding" in prompt
    assert "resolve references" in prompt, "the transcript must be labelled as reference-resolution only"
