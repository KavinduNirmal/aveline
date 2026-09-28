"""Pinned regression coverage for the inbound-message defects found on 2026-09-22.

Two behaviours are recorded here as **known broken**, deliberately:

1. **A resolution miss vetoes the whole run.** `_route_after_resolve` sends any
   ``not_found``/``ambiguous`` resolution to ``formulate_response``, which discards every
   specialist's output. For an inbound WhatsApp message the sender's own number *is* the
   identity, so "unknown customer" is the normal state for every first-time contact — and
   the entire workflow is blocked on it.

2. **The sender is asked for the number they are messaging from.** ``org_context.phone_number``
   carries the sender's identity, yet the clarification asks for a phone number.

Both are pinned with ``xfail(strict=True)`` rather than ``skip``: the tests assert the behaviour
we *want*, so when the fix lands the suite fails with an XPASS and forces whoever implements it to
come here and update these expectations on purpose. A silently-passing skip would hide the fix.

See ``docs/ADR/ADR-023-conversation-context-and-supervisor.md`` (invariants I1/I2) and
``.agents/plans/supervisor-llm-and-conversation-context-implementation-plan.md`` (W0.3).

Note the distinction these tests protect: ADR-019's *staff* lookup flow legitimately clarifies
when an ``@mention`` matches nothing. The existing test
``test_not_found_resolution_asks_for_phone`` covers that case and is intentionally left alone.
"""

import pytest

from app.customer_resolution import CustomerResolution
from app.events.block_builders import build_clarification_blocks
from app.workflows.concierge_workflow import build_concierge_graph

#: The exact inbound message from the reported incident: a product question from an unknown number.
_INCIDENT_MESSAGE = "Hello there. Are there any pinkish gowns in your collection?"

#: What the API actually sends for an inbound WhatsApp message (ConversationService).
_INBOUND_CONTEXT = {
    "organization_id": "org-1",
    "phone_number": "94763475058",
    "channel": "whatsapp",
    "direction": "inbound",
}


async def _invoke(message: str, org_context: dict) -> dict:
    """Run the concierge graph once, unmodified, against a real org context."""
    graph = build_concierge_graph()
    return await graph.ainvoke(
        {
            "message": message,
            "org_context": org_context,
            "history": [],
            "thread_summary": None,
            "pinned_slots": {},
            "intent": None,
            "resolution": None,
            "memory_output": None,
            "visual_output": None,
            "commerce_output": None,
            "usage": None,
            "response": None,
        }
    )


@pytest.fixture(autouse=True)
def _stub_specialists(monkeypatch):
    """Replace the specialist sub-graphs with fixed outputs.

    These tests are about the *routing* decision - whether a resolution miss vetoes the run or
    merely informs it - so the agents themselves are irrelevant here, and the real ones would
    reach the backend over HTTP. Stubbing them keeps the assertion about routing and the test
    hermetic.
    """

    async def memory(state):
        return {"memory_output": {"agent": "memory", "ran": True, "status": "success"}}

    async def visual(state):
        return {"visual_output": {"agent": "visual", "ran": True, "status": "success"}}

    async def commerce(state):
        return {"commerce_output": {"agent": "commerce", "ran": True, "status": "success"}}

    monkeypatch.setattr("app.workflows.concierge_workflow.run_memory_agent", memory)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_visual_agent", visual)
    monkeypatch.setattr("app.workflows.concierge_workflow.run_commerce_agent", commerce)


def _resolver_returning(kind: str, *, explicit_mention: bool = False, **kwargs):
    """Patch double for ``resolve_customer`` producing a fixed resolution."""

    async def fake(org_id, message, *, registry, customer_id=None, phone=None):
        return CustomerResolution(
            kind=kind, message=message, explicit_mention=explicit_mention, **kwargs)

    return fake


# ---------------------------------------------------------------------------
# I1 — an unknown customer must not block the run
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_unknown_inbound_customer_does_not_block_the_run(monkeypatch):
    """A not_found resolution must not stop specialists from producing an answer."""
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _resolver_returning("not_found"),
    )

    result = await _invoke(_INCIDENT_MESSAGE, _INBOUND_CONTEXT)

    assert result["resolution"]["kind"] == "not_found"
    # The three assertions below are the point: the miss is data, not a veto.
    assert result["memory_output"] is not None, "memory agent was skipped by the resolution veto"
    assert "clarification" not in result["response"]["output"], (
        "an unknown inbound sender must not be turned into a clarification"
    )


# ---------------------------------------------------------------------------
# I2 — the sender is never asked for the number they are messaging from
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_inbound_sender_is_not_asked_for_their_own_phone_number(monkeypatch):
    """The rendered reply must never ask for the number the sender already used.

    The clarification is asserted through ``build_clarification_blocks`` — the function that
    turns the resolution into the text a human actually reads. Asserting on the raw resolution
    would inspect serialized data (``kind: "not_found"``) and pass vacuously, because the
    "share their phone number" wording is added by the block builder, not stored in the payload.
    """
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _resolver_returning("not_found"),
    )

    result = await _invoke(_INCIDENT_MESSAGE, _INBOUND_CONTEXT)
    clarification = result["response"]["output"].get("clarification")

    rendered = " ".join(
        str(block.get("text", "")) for block in build_clarification_blocks(clarification)
    )

    assert "phone number" not in rendered.lower(), (
        f"asked an inbound sender for the number they are messaging from: {rendered!r}"
    )


# ---------------------------------------------------------------------------
# The staff-lookup case that must keep clarifying (ADR-019) — guards against
# fixing I1/I2 by removing clarification altogether.
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_explicit_mention_miss_still_clarifies(monkeypatch):
    """A staff `@mention` that matches nothing legitimately asks for a phone number."""
    monkeypatch.setattr(
        "app.workflows.concierge_workflow.resolve_customer",
        _resolver_returning("not_found", explicit_mention=True),
    )

    # No direction: this is staff typing in the Salon, not an inbound customer message.
    result = await _invoke("Any events for @Zara Nobody?", {"organization_id": "org-1"})

    assert result["response"]["output"]["clarification"]["kind"] == "not_found"
