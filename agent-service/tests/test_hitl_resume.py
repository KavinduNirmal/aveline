"""The HITL approval loop end to end (ADR-024).

These tests exercise the whole conversation-initiated order path with **no backend**: the commerce
agent's rule engine and loyalty lookup fall back to deterministic local implementations, so a
purchase that breaches a threshold pauses and a decision settles it without a single network call.
The payment gateway is the one exception - it may not invent a checkout link (plan §9.8), so the
registry that mints one is replaced by a double that answers. The checkpointer is an ``InMemorySaver``
shared across the two legs of a run, which is what makes "pause, then resume" testable in one process.

The defects these pin, all measured before the fix:

* no items in the conversation payload, so ``evaluate_deal`` returned ``skipped`` before reading a
  rule;
* the resume posted the decision back to ``/agents/query`` as a new question, re-running the whole
  pipeline and routing to the memory agent;
* the API sent ``approve`` where the graph matches ``approved``.
"""

from contextlib import asynccontextmanager

import pytest
from _payment_fakes import AnsweringPaymentRegistry
from fastapi.testclient import TestClient
from langgraph.checkpoint.memory import InMemorySaver

from app.core.config import get_settings
from app.main import app
from app.schemas.approvals import AgentApprovalDecision, normalize_decision
from app.schemas.response import AgentStatus
from app.workflows import concierge_workflow
from app.workflows.concierge_workflow import (
    NoPausedRunError,
    build_concierge_graph,
    resume_concierge,
    run_concierge,
)

TEST_INTERNAL_TOKEN = "local-development-placeholder-token"
ORG_ID = "01a0cb20-96f0-7c72-9823-98f59781c679"
THREAD_ID = "thread-hitl-001"

#: The ADR's own example: LKR 75,000 with a LKR 65,000 cost - a 13.3% margin. It breaches the
#: high-value rule (> LKR 40,000) *and* the margin rule (< 25%), so it must pause twice over.
EMERALD_SAREE = {
    "item_id": "b7f1c1a4-0000-4000-8000-000000000001",
    "item_name": "Emerald Green Georgette Saree",
    "quantity": 1,
    "unit_price": 75000.0,
    "wholesale_cost": 65000.0,
    "total_price": 75000.0,
}

PURCHASE_MESSAGE = "I want to buy the emerald green saree, please send the order."


@pytest.fixture(autouse=True)
def _hermetic_agent(monkeypatch):
    """Keep these tests offline, while leaving the parts under test real.

    The supervisor, the commerce sub-graph, the approval node and the response builder all run for
    real - they are what these tests are about. Only the nodes that would reach the backend over HTTP
    for *context* are replaced, and ``API_BASE_URL`` points at a closed local port so the two tools
    that do call out (the transcript window and the business-rules endpoint) fail instantly and take
    their documented fallbacks. The rule engine still falls back locally. The payment gateway no
    longer does: it must never invent a checkout link (plan §9.8), so the registry that mints one is
    replaced with a double that actually answers.
    """
    monkeypatch.setenv("API_BASE_URL", "http://127.0.0.1:1")
    get_settings.cache_clear()

    monkeypatch.setattr(
        concierge_workflow, "ToolRegistry", lambda *a, **k: AnsweringPaymentRegistry()
    )

    async def load_context(state):
        return {"history": [], "thread_summary": None, "pinned_slots": {}}

    async def resolve_customer(state):
        return {"resolution": None}

    async def memory(state):
        return {"memory_output": {"agent": "memory", "ran": True, "status": "success"}}

    async def visual(state):
        return {"visual_output": {"agent": "visual", "ran": True, "status": "success"}}

    monkeypatch.setattr(concierge_workflow, "run_load_context", load_context)
    monkeypatch.setattr(concierge_workflow, "run_resolve_customer", resolve_customer)
    monkeypatch.setattr(concierge_workflow, "run_memory_agent", memory)
    monkeypatch.setattr(concierge_workflow, "run_visual_agent", visual)

    yield
    get_settings.cache_clear()


@pytest.fixture
def shared_saver(monkeypatch) -> InMemorySaver:
    """One in-memory checkpointer for the whole test, shared by every leg of a run.

    ``create_checkpointer`` is normally called once per invocation, which for a test would mean the
    pause and the resume never see the same state. Returning one saver makes the two legs a single
    run, which is exactly what the real Postgres saver gives them.
    """
    saver = InMemorySaver()

    @asynccontextmanager
    async def fake_checkpointer(*args, **kwargs):
        yield saver

    monkeypatch.setattr(concierge_workflow, "create_checkpointer", fake_checkpointer)
    return saver


def _org_context() -> dict:
    return {
        "organization_id": ORG_ID,
        "conversation_id": "conv-hitl-001",
        "customer_id": None,
        "phone_number": "+94763475058",
        "channel": "whatsapp",
        "direction": "inbound",
        "items": [EMERALD_SAREE],
    }


async def _pause(saver: InMemorySaver, thread_id: str = THREAD_ID):
    return await run_concierge(
        PURCHASE_MESSAGE, org_context=_org_context(), thread_id=thread_id
    )


# ---------------------------------------------------------------------------
# The pause
# ---------------------------------------------------------------------------


async def test_a_purchase_over_the_threshold_pauses_for_approval(shared_saver):
    """The bug ADR-024 was written for: this message produced no pause and no order."""
    response = await _pause(shared_saver)

    assert response.status == AgentStatus.pending_approval
    commerce = response.output["commerce"]
    assert commerce["status"] == "pending_approval"
    assert commerce["needs_approval"] is True
    assert commerce["payment"] is None, "a paused deal must not produce a payment link"


async def test_the_pause_carries_every_rule_it_breached(shared_saver):
    response = await _pause(shared_saver)
    approval = response.output["approval"]

    assert approval["kind"] == "approval_required"
    assert approval["approval_type"] == "high_value_order"
    assert "HIGH_VALUE_THRESHOLD_EXCEEDED" in approval["triggered_rules"]
    # The margin breach matters too: approving this deal is a margin decision, not just a big-order
    # one, and the owner deciding it needs to see both.
    assert "LOW_MARGIN_THRESHOLD" in approval["triggered_rules"]
    assert approval["total"] == pytest.approx(75000.0)


async def test_the_graph_is_actually_waiting_on_the_checkpoint(shared_saver):
    """A pause is a checkpoint that says `next`, not a response that merely claims one."""
    await _pause(shared_saver)

    graph = build_concierge_graph(checkpointer=shared_saver)
    snapshot = await graph.aget_state({"configurable": {"thread_id": THREAD_ID}})

    assert snapshot.next == ("commerce_approval",)


async def test_without_a_thread_the_run_reports_the_pause_without_stopping(shared_saver):
    """An un-checkpointed caller keeps the pre-ADR-024 shape: a status, no interrupt."""
    response = await run_concierge(PURCHASE_MESSAGE, org_context=_org_context())

    assert response.status == AgentStatus.pending_approval
    assert response.output["commerce"]["status"] == "pending_approval"


async def test_a_message_with_no_items_never_pauses(shared_saver):
    """`skipped` is not `is_auto_approved`: nothing to evaluate must not look like a pause."""
    org_context = _org_context()
    org_context["items"] = []

    response = await run_concierge(
        "I want to buy something nice", org_context=org_context, thread_id=THREAD_ID
    )

    assert response.status == AgentStatus.success
    assert response.output["commerce"]["status"] == "skipped"


# ---------------------------------------------------------------------------
# The resume
# ---------------------------------------------------------------------------


async def test_an_approval_settles_the_deal(shared_saver):
    await _pause(shared_saver)

    response = await resume_concierge(
        THREAD_ID,
        {"decision": "approved", "comment": "Go ahead.", "order_id": "ord-hitl-001"},
    )

    assert response.status == AgentStatus.success
    commerce = response.output["commerce"]
    assert commerce["status"] == "success"
    assert commerce["payment"]["url"], "an approved deal must produce a checkout link"
    assert commerce["needs_approval"] is False


async def test_a_rejection_cancels_the_deal(shared_saver):
    await _pause(shared_saver)

    response = await resume_concierge(THREAD_ID, {"decision": "rejected", "comment": "Too thin."})

    assert response.status == AgentStatus.success
    commerce = response.output["commerce"]
    assert commerce["status"] == "rejected"
    assert commerce["payment"] is None, "a rejected deal must not produce a payment link"


async def test_a_resume_never_re_consults_the_supervisor(shared_saver, monkeypatch):
    """Invariant A3.

    Re-routing a decision is precisely what made the old resume skip commerce: the decision text
    carried no commerce signal, so the supervisor sent it to the memory agent. A resume enters at the
    paused node and the routing plan made for the original message stands.
    """
    calls = {"count": 0}
    real_supervise = concierge_workflow.supervise

    async def counting_supervise(*args, **kwargs):
        calls["count"] += 1
        return await real_supervise(*args, **kwargs)

    monkeypatch.setattr(concierge_workflow, "supervise", counting_supervise)

    await _pause(shared_saver)
    assert calls["count"] == 1, "the original message is routed exactly once"

    await resume_concierge(THREAD_ID, {"decision": "approved"})
    assert calls["count"] == 1, "a resume must not be routed again"


async def test_the_old_imperative_verb_settles_nothing(shared_saver):
    """The concrete seam bug: the API used to send `approve`, the graph matches `approved`.

    If the vocabularies ever drift again the failure is a visible `error` output, not a silent
    non-payment.
    """
    await _pause(shared_saver)

    response = await resume_concierge(THREAD_ID, {"decision": "approve"})

    assert response.output["commerce"]["status"] == "error"
    assert response.output["commerce"]["reason"] == "unrecognised approval decision"


async def test_resuming_a_thread_with_no_pause_is_an_error(shared_saver):
    """Not a silent fresh run: that is the behaviour this endpoint replaces."""
    with pytest.raises(NoPausedRunError):
        await resume_concierge("thread-that-never-paused", {"decision": "approved"})


async def test_the_settlement_quotes_the_order_and_customer_the_api_supplied(shared_saver):
    """The API has written the order by the time a human decides; the settlement must quote it.

    The checkpoint's conversation context predates the order and usually carries neither the order id
    nor the customer's name, so the decision payload wins over it.
    """
    await _pause(shared_saver)
    order_id = "9f3c1d2e-0000-4000-8000-0000000000ab"

    response = await resume_concierge(
        THREAD_ID,
        {"decision": "approved", "order_id": order_id, "customer_name": "Kasha Vivian Perera"},
    )

    commerce = response.output["commerce"]
    assert "Kasha Vivian Perera" in commerce["summary"]
    # The payment link is minted against the order, so it must name the order and not the fallback.
    assert commerce["payment"]["url"].endswith(order_id[-6:])


async def test_a_revised_decision_re_prices_the_deal(shared_saver):
    await _pause(shared_saver)

    response = await resume_concierge(
        THREAD_ID, {"decision": "revised", "revised_discount": 0.1, "order_id": "ord-revised-1"}
    )

    assert response.status == AgentStatus.success
    commerce = response.output["commerce"]
    assert commerce["status"] == "success"
    assert commerce["deal"]["applied_discount_percent"] == pytest.approx(0.1)
    assert commerce["deal"]["total"] == pytest.approx(67500.0)


async def test_an_out_of_range_discount_is_not_treated_as_a_rate(shared_saver):
    """The API speaks in money and the tools speak in rates; a stray amount must not become 100% off.

    The guard exists because an amount read as a rate clamps to a full discount - the single most
    expensive way this seam could fail.
    """
    await _pause(shared_saver)

    response = await resume_concierge(THREAD_ID, {"decision": "revised", "revised_discount": 5000})

    assert response.output["commerce"]["deal"]["applied_discount_percent"] == pytest.approx(0.0)


# ---------------------------------------------------------------------------
# The published vocabulary
# ---------------------------------------------------------------------------


def test_the_decision_vocabulary_is_the_graphs_own():
    """Pinned against `Aveline.Api/.../ApprovalDecisions.cs`.

    The C# side carries the same three literals and a test asserting them. This is the only place the
    two languages meet, so drift has to fail in both directions.
    """
    assert {decision.value for decision in AgentApprovalDecision} == {
        "approved",
        "rejected",
        "revised",
    }


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("approved", "approved"),
        (" APPROVED ", "approved"),
        ("rejected", "rejected"),
        ("approve", None),
        ("", None),
        (None, None),
        ({"decision": "approved"}, None),
    ],
)
def test_only_the_published_decisions_are_accepted(value, expected):
    assert normalize_decision(value) == expected


# ---------------------------------------------------------------------------
# The endpoint
# ---------------------------------------------------------------------------

RESUME_URL = "/agents/resume"
HEADERS = {"X-Internal-Token": TEST_INTERNAL_TOKEN}


@pytest.fixture(autouse=True)
def _internal_token(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_TOKEN", TEST_INTERNAL_TOKEN)
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


def test_resume_requires_the_internal_token():
    response = TestClient(app).post(RESUME_URL, json={"thread_id": THREAD_ID, "decision": "approved"})
    assert response.status_code == 401


def test_resume_rejects_a_decision_outside_the_vocabulary(shared_saver):
    response = TestClient(app).post(
        RESUME_URL,
        headers=HEADERS,
        json={"thread_id": THREAD_ID, "decision": "approve"},
    )

    assert response.status_code == 400
    assert "approve" in response.json()["detail"]


def test_resume_reports_a_thread_with_no_pause_as_not_found(shared_saver):
    response = TestClient(app).post(
        RESUME_URL,
        headers=HEADERS,
        json={"thread_id": "thread-never-paused", "decision": "approved"},
    )

    assert response.status_code == 404


def test_resume_settles_a_paused_run_over_http(shared_saver):
    """The whole loop through the API surface: pause, then decide."""
    client = TestClient(app)
    pause = client.post(
        "/agents/query",
        headers=HEADERS,
        json={
            "query": PURCHASE_MESSAGE,
            "thread_id": THREAD_ID,
            "org_context": _org_context(),
        },
    )
    assert pause.status_code == 200
    body = pause.json()
    assert body["result"]["status"] == "pending_approval"
    assert body["result"]["output"]["approval"]["kind"] == "approval_required"

    settled = client.post(
        RESUME_URL,
        headers=HEADERS,
        json={
            "thread_id": THREAD_ID,
            "decision": "approved",
            "organization_id": ORG_ID,
            "order_id": "ord-http-001",
            "comment": "Approved by the owner.",
        },
    )

    assert settled.status_code == 200
    result = settled.json()["result"]
    assert result["status"] == "success"
    assert result["output"]["commerce"]["status"] == "success"
