"""Tests for the top-level concierge orchestrator (app/workflows/concierge_workflow.py).

The orchestrator runs the Intent Gate, delegates to the three agent sub-graphs,
and formulates a final response. Routing is deterministic and testable without an
LLM or a live database.
"""

import pytest

from app.core.config import get_settings
from app.customer_resolution import CustomerCandidate, CustomerResolution
from app.schemas.response import AgentStatus
from app.workflows.concierge_workflow import (
    build_concierge_graph,
    run_concierge,
    run_load_context,
    run_resolve_customer,
)


async def _invoke(message: str, org_context: dict | None = None) -> dict:
    graph = build_concierge_graph()
    initial = {
        "message": message,
        "org_context": org_context or {},
        # Context layers (ADR-023). Empty here: these fixtures carry no conversation id, so the
        # load_context node short-circuits and makes no backend call.
        "history": [],
        "thread_summary": None,
        "pinned_slots": {},
        "intent": None,
        "memory_output": None,
        "visual_output": None,
        "commerce_output": None,
        "response": None,
    }
    return await graph.ainvoke(initial)


async def test_item_search_routes_through_memory_and_visual():
    result = await _invoke("Do you have a blue saree for a wedding?")
    assert result["intent"]["intent_type"] == "item_search"
    assert result["memory_output"] is not None
    assert result["visual_output"] is not None
    assert result["commerce_output"] is None
    assert result["response"]["status"] == AgentStatus.success


async def test_pricing_query_routes_through_memory_and_commerce():
    result = await _invoke("How much is this dress?")
    assert result["intent"]["intent_type"] == "pricing_query"
    assert result["memory_output"] is not None
    assert result["visual_output"] is None
    assert result["commerce_output"] is not None


async def test_visual_agent_emits_structured_output():
    result = await _invoke("Do you have a blue saree for a wedding?")
    visual = result["visual_output"]

    assert visual is not None
    assert visual["ran"] is True
    assert visual["agent"] == "visual"
    assert visual["status"] in ("success", "pending")
    assert isinstance(visual["items"], list)
    assert isinstance(visual["looks"], list)



async def test_commerce_agent_skips_without_organization_context():
    """The Commerce Agent needs an organization to price against.

    Slice 3 replaced the old placeholder node with the real sub-graph, so a pricing
    query with no org context now reports ``skipped`` instead of a stub envelope.
    """
    result = await _invoke("How much is this dress?")
    commerce = result["commerce_output"]

    assert commerce is not None
    assert commerce["ran"] is True
    assert commerce["agent"] == "commerce"
    assert commerce["status"] == "skipped"
    assert commerce["reason"] == "no organization context available"


async def test_out_of_scope_short_circuits():
    result = await _invoke("Write me a python script to sort a list")
    assert result["intent"]["intent_type"] == "out_of_scope"
    assert result["intent"]["is_relevant"] is False
    # No agent should run for out-of-scope input.
    assert result["memory_output"] is None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None
    assert result["response"]["status"] == AgentStatus.out_of_scope


async def test_general_inquiry_runs_memory_only():
    result = await _invoke("Hello, how are you?")
    assert result["intent"]["intent_type"] == "general_inquiry"
    assert result["memory_output"] is not None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None


async def test_memory_agent_parses_message_without_customer_context():
    result = await _invoke("Do you have a blue saree for a wedding?")
    parsed = result["memory_output"]["parsed_intent"]
    assert parsed["intent_type"] == "item_search"
    assert parsed.get("occasion") == "wedding"
    assert parsed.get("color") == "blue"


@pytest.mark.asyncio
async def test_run_concierge_returns_response():
    response = await run_concierge("Do you have a blue saree?")
    assert response.status == AgentStatus.success
    assert response.output is not None


@pytest.mark.asyncio
async def test_run_concierge_metadata_is_rule_based_when_no_llm():
    """Without an LLM every completed run carries rule-based usage metadata (Issue #165)."""
    response = await run_concierge("Do you have a blue saree for a wedding?")
    assert response.metadata is not None
    assert response.metadata.model == "rule-based"
    assert response.metadata.input_tokens == 0
    assert response.metadata.output_tokens == 0
    assert response.metadata.tokens_used == 0


@pytest.mark.asyncio
async def test_run_concierge_out_of_scope():
    response = await run_concierge("Tell me a joke")
    assert response.status == AgentStatus.out_of_scope


@pytest.mark.asyncio
async def test_run_concierge_emits_lifecycle_states():
    """The on_state callback receives states as the workflow progresses."""
    states: list[str] = []

    async def on_state(state):
        states.append(state.value)

    response = await run_concierge(
        "Do you have a blue saree for a wedding?",
        on_state=on_state,
    )

    assert response.status == AgentStatus.success
    # intent_gate -> thinking, memory_agent -> searching, visual_agent -> searching,
    # formulate_response -> processing.
    assert "thinking" in states
    assert "searching" in states
    assert "processing" in states
    # The last emitted state is processing (formulate_response) before the terminal success.
    assert states[-1] == "processing"


@pytest.mark.asyncio
async def test_run_concierge_out_of_scope_emits_thinking_then_processing():
    """Out-of-scope short-circuits: intent gate (thinking) then formulate (processing)."""
    states: list[str] = []

    async def on_state(state):
        states.append(state.value)

    response = await run_concierge("Tell me a joke", on_state=on_state)

    assert response.status == AgentStatus.out_of_scope
    assert states == ["thinking", "processing"]


@pytest.mark.asyncio
async def test_run_concierge_preserves_state_order_with_delay(monkeypatch):
    """A configured state delay must not reorder or drop emitted lifecycle states."""
    monkeypatch.setattr(get_settings(), "agent_state_delay_ms", 1)
    states: list[str] = []

    async def on_state(state):
        states.append(state.value)

    response = await run_concierge(
        "Do you have a blue saree for a wedding?",
        on_state=on_state,
    )

    assert response.status == AgentStatus.success
    assert states == ["thinking", "searching", "searching", "processing"]


# ---------------------------------------------------------------------------
# Workflow Path End-to-End Tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_workflow_product_search_path():
    """Product Search Path: Intent Gate -> Memory Agent -> Visual Agent -> Formulate Response."""
    result = await _invoke("Do you have an emerald silk saree for a reception?")

    # 1. Intent Gate
    assert result["intent"]["intent_type"] == "item_search"
    # 2. Memory Agent
    assert result["memory_output"] is not None
    assert result["memory_output"]["ran"] is True
    # 3. Visual Agent
    assert result["visual_output"] is not None
    assert result["visual_output"]["ran"] is True
    # 4. Commerce Agent skipped
    assert result["commerce_output"] is None
    # 5. Formulate Response
    assert result["response"]["status"] == AgentStatus.success


@pytest.mark.asyncio
async def test_workflow_purchase_request_path():
    """Purchase-Related Request Path: Intent Gate -> Memory Agent -> Visual Agent -> Commerce Agent -> Formulate Response."""
    result = await _invoke("I want to purchase this emerald silk saree")

    # 1. Intent Gate
    assert result["intent"]["intent_type"] == "order_placement"
    # 2. Memory Agent
    assert result["memory_output"] is not None
    # 3. Visual Agent
    assert result["visual_output"] is not None
    # 4. Commerce Agent
    assert result["commerce_output"] is not None
    assert result["commerce_output"]["ran"] is True
    # 5. Formulate Response
    assert result["response"]["status"] == AgentStatus.success


@pytest.mark.asyncio
async def test_workflow_reference_image_path():
    """Reference Image Path: Intent Gate -> Memory Agent -> Visual Agent (Image Analysis -> Inventory Search -> Possible Sourcing) -> Formulate Response."""
    result = await _invoke(
        "Find me something matching this photo",
        org_context={"image_url": "https://images.aveline.luxury/evening-dress.jpg"},
    )

    # 1. Intent Gate
    assert result["intent"]["intent_type"] in ("item_search", "general_inquiry")
    # 2. Memory Agent
    assert result["memory_output"] is not None
    # 3. Visual Agent executed with image attributes & inventory search / looks / sourcing
    assert result["visual_output"] is not None
    assert result["visual_output"]["ran"] is True
    assert "items" in result["visual_output"]
    assert "looks" in result["visual_output"]
    # 4. Formulate Response
    assert result["response"]["status"] == AgentStatus.success


@pytest.mark.asyncio
async def test_run_visual_agent_wires_llm_when_configured(monkeypatch):
    """Verify run_visual_agent queries visual_llm_or_none and executes with LLM commentary."""
    from unittest.mock import AsyncMock, MagicMock

    from app.workflows.concierge_workflow import run_visual_agent

    mock_llm_res = MagicMock()
    mock_llm_res.content = "Editorial styling commentary from wired LLM."
    mock_llm_res.usage_metadata = {"input_tokens": 80, "output_tokens": 25}

    mock_llm = MagicMock()
    mock_llm.ainvoke = AsyncMock(return_value=mock_llm_res)

    monkeypatch.setattr(
        "app.workflows.concierge_workflow.visual_llm_or_none",
        lambda settings: mock_llm,
    )

    # Mock tool registry inventory search so items are matched
    monkeypatch.setattr(
        "app.agents.visual_insight.nodes.search_inventory",
        AsyncMock(
            return_value=[
                MagicMock(
                    itemId="item-101",
                    name="Peach Raw-Silk Drape Gown",
                    price=1250.0,
                    stock=2,
                    imageUrl="https://images.aveline.luxury/gown.jpg",
                    category="Gown",
                    color="Peach",
                    occasion="Wedding",
                    aestheticTags=["Silk"],
                    model_dump=lambda: {
                        "itemId": "item-101",
                        "name": "Peach Raw-Silk Drape Gown",
                        "price": 1250.0,
                        "stock": 2,
                        "imageUrl": "https://images.aveline.luxury/gown.jpg",
                    },
                )
            ]
        ),
    )

    state = {
        "message": "I need a gown for a wedding",
        "org_context": {
            "organization_id": "org-test",
            "customer_id": "cust-01",
            "direction": "inbound",
        },
        "intent": {"intent_type": "item_search"},
    }

    result = await run_visual_agent(state)
    visual_output = result["visual_output"]

    assert visual_output["ran"] is True
    assert visual_output["status"] == "success"
    assert len(visual_output["looks"]) == 1
    assert visual_output["looks"][0]["text"] == "Editorial styling commentary from wired LLM."
    assert mock_llm.ainvoke.called


# ---------------------------------------------------------------------------
# Shared customer resolution (Issue #161)
# ---------------------------------------------------------------------------


def _make_resolver(*, kind, **kwargs):
    """Return an async resolver double producing a fixed CustomerResolution."""

    async def fake(org_id, message, *, registry, customer_id=None, phone=None):
        return CustomerResolution(kind=kind, message=message, **kwargs)

    return fake


@pytest.mark.asyncio
async def test_ambiguous_resolution_short_circuits_to_clarification(monkeypatch):
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _make_resolver(
            kind="ambiguous",
            candidates=[
                CustomerCandidate(customer_id="c1", full_name="Samantha Arias", status="vip"),
                CustomerCandidate(customer_id="c2", full_name="Samantha R", status="returning"),
            ],
        ),
    )

    result = await _invoke("Any events for Samantha Arias?", {"organization_id": "org-1"})

    assert result["resolution"]["kind"] == "ambiguous"
    # No specialist should run while we are still deciding which customer.
    assert result["memory_output"] is None
    assert result["visual_output"] is None
    assert result["commerce_output"] is None
    assert result["response"]["output"]["clarification"]["kind"] == "ambiguous"


@pytest.mark.asyncio
async def test_explicit_mention_miss_still_asks_for_a_phone_number(monkeypatch):
    """A staff `@mention` that matches nothing is worth asking about (ADR-019)."""
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _make_resolver(kind="not_found", explicit_mention=True),
    )

    result = await _invoke("Any events for @Zara Nobody?", {"organization_id": "org-1"})

    assert result["resolution"]["kind"] == "not_found"
    # Asking instead of working: no specialist content, and the clarification is rendered.
    assert result["memory_output"] is None
    assert result["response"]["output"]["clarification"]["kind"] == "not_found"


@pytest.mark.asyncio
async def test_resolution_miss_outside_a_mention_does_not_ask_and_does_not_stop(monkeypatch):
    """The I1/I2 fix: an unresolved customer must not veto the run (ADR-023, Decision 4).

    A plain staff note, or an inbound sender whose number is simply not on file, is not a lookup
    request. The run continues and specialists produce work rather than the whole exchange being
    replaced by a request for a phone number.
    """
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _make_resolver(kind="not_found"),
    )

    result = await _invoke("Any events for Zara Nobody?", {"organization_id": "org-1"})

    assert result["resolution"]["kind"] == "not_found"
    assert "clarification" not in result["response"]["output"]
    assert result["memory_output"] is not None


@pytest.mark.asyncio
async def test_resolve_node_records_explicit_customer(monkeypatch):
    calls: list[tuple[str, str | None, str | None]] = []

    async def fake(org_id, message, *, registry, customer_id=None, phone=None):
        calls.append((org_id, customer_id, phone))
        return CustomerResolution(kind="resolved", customer_id="c1", profile={"fullName": "Samantha"})

    monkeypatch.setattr("app.workflows.concierge_workflow.resolve_customer", fake)

    out = await run_resolve_customer(
        {"message": "Any events for Samantha?", "org_context": {"organization_id": "org-1", "customer_id": "c1"}}
    )

    assert out["resolution"]["kind"] == "resolved"
    assert calls == [("org-1", "c1", None)]


# ---------------------------------------------------------------------------
# The org_context -> visual state bridge (U3.1)
# ---------------------------------------------------------------------------

REAL_ORG = "11111111-2222-3333-4444-555555555555"
REAL_ATTACHMENT = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"


class _CapturingGraph:
    """A stand-in visual sub-graph that records the state it is invoked with."""

    def __init__(self, sink: dict) -> None:
        self._sink = sink

    async def ainvoke(self, state: dict) -> dict:
        self._sink.update(state)
        return {"output": {"status": "success", "agent": "visual", "ran": True, "items": [], "looks": []}}


def _patch_visual_subgraph(monkeypatch, sink: dict) -> None:
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.build_visual_graph",
        lambda registry, llm=None: _CapturingGraph(sink),
    )
    monkeypatch.setattr("app.workflows.concierge_workflow.ToolRegistry", lambda: None)
    monkeypatch.setattr("app.workflows.concierge_workflow.visual_llm_or_none", lambda settings: None)


@pytest.mark.asyncio
async def test_run_visual_agent_maps_the_attachment_reference_into_the_subgraph(monkeypatch):
    """The contract's `attachments[].reference` becomes the state's reference fields."""
    from app.workflows.concierge_workflow import run_visual_agent

    captured: dict = {}
    _patch_visual_subgraph(monkeypatch, captured)

    await run_visual_agent({
        "message": "match this photo",
        "org_context": {
            "organization_id": REAL_ORG,
            "image_url": "https://bridge.example/api/v1/media/rotating-token",
            "attachments": [
                {
                    "attachmentId": REAL_ATTACHMENT,
                    "publicId": f"aveline/{REAL_ORG}/conversations/{REAL_ATTACHMENT}",
                    "reference": {"kind": "attachment", "id": REAL_ATTACHMENT},
                }
            ],
        },
        "intent": {"intent_type": "item_search"},
    })

    assert captured["org_id"] == REAL_ORG
    assert captured["image_ref_kind"] == "attachment"
    assert captured["image_ref_id"] == REAL_ATTACHMENT
    assert captured["image_url"] == "https://bridge.example/api/v1/media/rotating-token"


@pytest.mark.asyncio
async def test_run_visual_agent_keeps_the_legacy_arm_when_no_reference_is_present(monkeypatch):
    """No attachments -> no reference, and the legacy absolute URL still reaches the sub-graph."""
    from app.workflows.concierge_workflow import run_visual_agent

    captured: dict = {}
    _patch_visual_subgraph(monkeypatch, captured)

    await run_visual_agent({
        "message": "match this photo",
        "org_context": {
            "organization_id": REAL_ORG,
            "image_url": "https://example.com/legacy.jpg",
        },
        "intent": {"intent_type": "item_search"},
    })

    assert captured["org_id"] == REAL_ORG
    assert captured["image_ref_kind"] is None
    assert captured["image_ref_id"] is None
    assert captured["image_url"] == "https://example.com/legacy.jpg"


# ---------------------------------------------------------------------------
# Context loading (ADR-023, W1.4-W1.7)
# ---------------------------------------------------------------------------


class _StubRegistry:
    """Registry double whose history read is scripted per test."""

    def __init__(self, *, payload=None, raises: Exception | None = None) -> None:
        self._payload = payload
        self._raises = raises
        self.calls: list[tuple] = []

    async def get_conversation_history(self, org_id, conversation_id, limit=20):
        self.calls.append((org_id, conversation_id, limit))
        if self._raises is not None:
            raise self._raises
        return self._payload


def _patch_registry(monkeypatch, registry):
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.ToolRegistry", lambda *a, **k: registry
    )
    return registry


def _context_state(org_context: dict) -> dict:
    return {
        "message": "hello",
        "org_context": org_context,
        "history": [],
        "thread_summary": None,
        "pinned_slots": {},
    }


@pytest.mark.asyncio
async def test_load_context_populates_the_window_from_the_backend(monkeypatch):
    registry = _patch_registry(
        monkeypatch,
        _StubRegistry(
            payload={
                "items": [
                    {"id": "m1", "authorKind": "System", "text": "Any pinkish gowns?"},
                    {"id": "m2", "authorKind": "Agent", "text": "We have three."},
                ]
            }
        ),
    )

    result = await run_load_context(
        _context_state({"organization_id": "org-1", "conversation_id": "conv-1"})
    )

    assert [t["id"] for t in result["history"]] == ["m1", "m2"]
    assert registry.calls == [("org-1", "conv-1", get_settings().context_window_turns)]


@pytest.mark.asyncio
async def test_load_context_skips_the_backend_when_no_conversation_is_bound(monkeypatch):
    # A staff query with no bound conversation: no id, so no call and no window.
    registry = _patch_registry(monkeypatch, _StubRegistry(payload={"items": []}))

    result = await run_load_context(_context_state({"organization_id": "org-1"}))

    assert result == {"history": [], "thread_summary": None, "pinned_slots": {}}
    assert registry.calls == []


@pytest.mark.asyncio
async def test_load_context_survives_a_backend_failure(monkeypatch):
    # Losing history degrades an answer; it must never prevent one.
    _patch_registry(
        monkeypatch, _StubRegistry(raises=RuntimeError("backend down"))
    )

    result = await run_load_context(
        _context_state({"organization_id": "org-1", "conversation_id": "conv-1"})
    )

    assert result == {"history": [], "thread_summary": None, "pinned_slots": {}}


@pytest.mark.asyncio
async def test_load_context_tolerates_a_malformed_payload(monkeypatch):
    _patch_registry(monkeypatch, _StubRegistry(payload={"items": "not-a-list"}))

    result = await run_load_context(
        _context_state({"organization_id": "org-1", "conversation_id": "conv-1"})
    )

    assert result["history"] == []


@pytest.mark.asyncio
async def test_load_context_bounds_a_long_transcript(monkeypatch):
    # The window is a budget: a long conversation must not put every turn into state.
    items = [
        {"id": f"m{i}", "authorKind": "System", "text": "x" * 400} for i in range(60)
    ]
    _patch_registry(monkeypatch, _StubRegistry(payload={"items": items}))

    result = await run_load_context(
        _context_state({"organization_id": "org-1", "conversation_id": "conv-1"})
    )

    assert 0 < len(result["history"]) < len(items)
    # The newest turn is always kept: a window without the message just received is useless.
    assert result["history"][-1]["id"] == "m59"
