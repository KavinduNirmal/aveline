"""Golden cases for the Customer Memory Agent's write and retrieval contract.

Two defects this file exists to keep closed, both from the AVA gap analysis:

* **A1/C3** - the agent computed provenance (``is_explicit``, ``confidence``) and the tool it
  called could not carry it, so every agent-written memory landed as an unvouched-for inference.
* **B1/B2** - the search had no similarity floor and returned nothing but content and category, so
  a customer whose notes said nothing about the message was handed the nearest unrelated ones, and
  the score that would have said so was thrown away.

The four cases below are the plan's golden set (wedding, discount/purchase, out-of-scope, revoked
consent), each asserting the write contract rather than the prose.
"""

import pytest

from app.agents.customer_memory.graph import build_memory_graph

WEDDING = "Hi! I have a wedding on Saturday. Do you have anything bluish in my size?"
DISCOUNT = "Is this available for purchase? Also do you offer a discount?"


class RecordingRegistry:
    """A registry double that records exactly what the agent asked the backend to store."""

    def __init__(self) -> None:
        self.writes: list[dict] = []
        self.events: list[dict] = []
        self.search_calls: list[dict] = []
        self.consent_status = "granted"
        self.memories: list[dict] = []

    async def identify_customer(self, org_id, phone_number, full_name=None):
        return {"customerId": "cust-1", "phoneNumber": phone_number, "fullName": full_name}

    async def get_customer_consent(self, org_id, customer_id):
        return {"consentStatus": self.consent_status}

    async def get_customer_memories(self, org_id, customer_id, query, top_k=5, min_similarity=0.0):
        self.search_calls.append({"query": query, "top_k": top_k, "min_similarity": min_similarity})
        return self.memories

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
        self.writes.append(
            {
                "content": content,
                "category": category,
                "source": source,
                "is_explicit": is_explicit,
                "confidence": confidence,
            }
        )
        return {"id": f"mem-{len(self.writes)}"}

    async def add_customer_event(self, org_id, customer_id, event_type, event_date, description=None):
        self.events.append({"event_type": event_type, "event_date": event_date})
        return {"id": f"evt-{len(self.events)}"}

    async def record_customer_interaction(self, *args, **kwargs):
        return {"id": "int-1"}

    async def generate_interaction_brief(self, org_id, customer_id):
        return {"customerName": "Sarah Perera", "status": "new"}


async def _run(registry, *, message, intent_type):
    graph = build_memory_graph(registry)
    return await graph.ainvoke(
        {
            "org_id": "org-1",
            "customer_id": "cust-1",
            "message": message,
            "intent_type": intent_type,
            "channel": "whatsapp",
            "direction": "inbound",
        }
    )


@pytest.mark.asyncio
async def test_wedding_case_writes_a_stated_event_with_its_confidence():
    registry = RecordingRegistry()

    result = await _run(registry, message=WEDDING, intent_type="item_search")

    assert result["status"] == "success"
    events = [write for write in registry.writes if write["category"] == "event"]
    assert events, "the wedding must be stored as an event memory"
    assert all(write["is_explicit"] is True for write in registry.writes)
    assert all(write["confidence"] == 0.9 for write in registry.writes)
    assert all(write["source"] == "conversation" for write in registry.writes)
    # The undated parse keeps the memory; only a dateable event becomes a structured row.
    assert registry.events == []


@pytest.mark.asyncio
async def test_discount_case_keeps_the_agent_alive_and_grounded():
    """A pricing/purchase message must not make Ava go silent (the reported order_placement bug)."""
    registry = RecordingRegistry()

    result = await _run(registry, message=DISCOUNT, intent_type="pricing_query")

    assert result["status"] == "success"
    assert result["output"]["status"] == "success"
    assert result["output"]["parsed_intent"]["intent_type"] == "pricing_query"
    assert result["output"]["interaction_brief"]


@pytest.mark.asyncio
async def test_a_stated_preference_carries_its_provenance_whatever_the_intent():
    """The provenance the extraction computes must survive the tool call (gap A1/C3)."""
    registry = RecordingRegistry()

    await _run(
        registry,
        message="I really like silk sarees, please.",
        intent_type="customer_preference",
    )

    assert registry.writes, "a stated preference must reach the backend"
    for write in registry.writes:
        assert write["is_explicit"] is True
        assert write["confidence"] == 0.9
        assert write["source"] == "conversation"


@pytest.mark.asyncio
async def test_out_of_scope_message_writes_nothing():
    registry = RecordingRegistry()

    result = await _run(registry, message="What is the capital of France?", intent_type="out_of_scope")

    assert result["status"] in {"success", "out_of_scope"}
    assert registry.writes == []


@pytest.mark.asyncio
async def test_revoked_consent_writes_nothing_and_does_not_search():
    registry = RecordingRegistry()
    registry.consent_status = "revoked"

    result = await _run(registry, message=WEDDING, intent_type="item_search")

    assert result["status"] == "skipped"
    assert result["consent_status"] == "revoked"
    assert registry.writes == []
    assert registry.search_calls == []


@pytest.mark.asyncio
async def test_the_search_carries_the_similarity_floor():
    """Top-k alone returns k rows however unrelated, so the agent asks for a floor (gap B1)."""
    registry = RecordingRegistry()

    await _run(registry, message=WEDDING, intent_type="item_search")

    assert len(registry.search_calls) == 1
    assert registry.search_calls[0]["min_similarity"] > 0.0


@pytest.mark.asyncio
async def test_retrieved_provenance_reaches_the_output():
    """The score and the explicitness the store returns must survive into the output (gap B2)."""
    registry = RecordingRegistry()
    registry.memories = [
        {
            "id": "mem-1",
            "content": "Prefers emerald silk",
            "category": "preference",
            "source": "conversation",
            "isExplicit": True,
            "confidence": 0.9,
            "similarity": 0.91,
        }
    ]

    result = await _run(registry, message=WEDDING, intent_type="item_search")

    note = result["output"]["memories_on_file"][0]
    assert note["content"] == "Prefers emerald silk"
    assert note["is_explicit"] is True
    assert note["confidence"] == 0.9
    assert note["similarity"] == 0.91
    assert note["source"] == "conversation"
