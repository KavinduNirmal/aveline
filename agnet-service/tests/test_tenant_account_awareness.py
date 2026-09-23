"""Tenant account awareness (ADR-026).

Aveline answering "how many Blossoms do I have left?" is the first time the concierge states a fact
about the boutique's *money*. Two properties matter more than the happy path, and most of this file
is about them:

1. **Audience.** These figures are for staff. A customer message must not receive them, and the
   gate for that is explicit rather than inherited from a heuristic.
2. **Authority.** A number in the reply must come from the fetched snapshot. The model writes the
   wording; it does not get to produce the figure.
"""

import json

import pytest

from app.context import render_tenant_block, tenant_summary
from app.gate import (
    _TENANT_NOT_STAFF_REPLY,
    _TENANT_UNAVAILABLE_REPLY,
    _numerals,
    _tenant_reply,
    classify_by_rules,
    is_aveline_help,
    is_tenant_account,
    may_read_tenant_account,
    supervise,
)

ORG_ID = "11111111-1111-1111-1111-111111111111"

#: The backend snapshot as `GET /internal/usage/tenant/{orgId}` serialises it: camelCase, and every
#: quantity a JSON number rather than a string.
SNAPSHOT = {
    "organizationId": ORG_ID,
    "blossoms": {
        "organizationId": ORG_ID,
        "periodStart": "2026-09-01T00:00:00Z",
        "periodEnd": "2026-09-30T23:59:59Z",
        "periodIsClosed": False,
        "planTier": "Bloom",
        "monthlyBlossomLimit": 500.0,
        "blossomGranted": 0.0,
        "blossomAdjusted": 0.0,
        "blossomUsed": 87.4,
        "blossomRemaining": 412.6,
        "percentUsed": 17.48,
        "lowBalanceThresholdPercent": 20.0,
        "asOf": "2026-09-24T03:05:00Z",
    },
    "staff": {
        "key": "staff.max",
        "used": 2.0,
        "limit": 3.0,
        "remaining": 1.0,
        "percentUsed": 66.67,
        "isHardLimit": True,
    },
    "customers": {
        "key": "customers.active.max",
        "used": 118.0,
        "limit": 250.0,
        "remaining": 132.0,
        "percentUsed": 47.2,
        "isHardLimit": True,
    },
    "customerCountBasis": "customers active in the last 90 days",
    "blossomsAreLow": False,
    "asOf": "2026-09-24T03:05:00Z",
}

STAFF = {"organization_id": ORG_ID, "staff_query": True}


class _StubLlm:
    """Chat-model double returning a scripted reply."""

    def __init__(self, content: str) -> None:
        self._content = content

    async def ainvoke(self, messages) -> object:
        return type("_Response", (), {"content": self._content})()


def _plan(**overrides) -> str:
    payload = {
        "intent_type": "tenant_account",
        "agents": [],
        "needs_customer_resolution": False,
        "clarification": None,
        "reply": None,
        "requires_approval": False,
    }
    payload.update(overrides)
    return json.dumps(payload)


# ---------------------------------------------------------------------------
# Intent: the ask, not the noun
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "message",
    [
        "How many Blossoms do I have left?",
        "how much blossoms do I have left",
        "How many customers do I have left?",
        "How many seats do I have left?",
        "How many staff can I add?",
        "What's my blossom balance?",
        "Do we have any Blossoms remaining?",
        "how much do I have left?",
    ],
)
def test_account_questions_are_classified_as_account_questions(message):
    assert is_tenant_account(message)
    assert classify_by_rules(message).intent_type == "tenant_account"


@pytest.mark.parametrize(
    "message",
    [
        # Documentation about Blossoms, not this boutique's balance. The noun is the same; the
        # question is not, and these must stay in the handbook lane.
        "What is a Blossom?",
        "How much does a Blossom cost?",
        "How many seats does the Orchid plan include?",
        "How do I invite a staff member?",
        "How much is the Orchid plan?",
        "How do I top up Blossoms?",
        # Locating the number is documentation; asking for its value is not. The handbook owns the
        # first, which is why the locating guard exists (pinned against
        # `test_handbook_retrieval.py::test_platform_questions_classify_as_aveline_help`).
        "Where can I see my Blossom balance?",
        "How do I check my remaining Blossoms?",
    ],
)
def test_documentation_questions_are_not_account_questions(message):
    assert not is_tenant_account(message)


@pytest.mark.parametrize(
    "message",
    [
        "Where can I see my Blossom balance?",
        "How do I check my remaining Blossoms?",
    ],
)
def test_a_locating_question_stays_in_the_handbook_lane(message):
    assert classify_by_rules(message).intent_type == "aveline_help"


def test_an_account_question_outranks_the_handbook_lane():
    # `is_aveline_help` claims any mention of "blossom", so without the precedence the balance
    # question would be answered from a handbook page about what Blossoms are.
    message = "How many Blossoms do I have left?"

    assert is_aveline_help(message)
    assert classify_by_rules(message).intent_type == "tenant_account"


def test_an_account_question_routes_no_specialist():
    # Nobody else's lane: Ava would brief on a customer, and there is no customer in the question.
    assert classify_by_rules("How many Blossoms do I have left?").suggested_agents == []


# ---------------------------------------------------------------------------
# Audience
# ---------------------------------------------------------------------------


def test_the_staff_heuristic_is_not_reused_for_the_account_gate():
    # The workflow nodes derive the audience from `direction` and read an absent direction as
    # staff. That is right for identity and wrong for money, so the account gate requires the flag
    # itself. This pins the difference at the two places it would be tempting to conflate them.
    assert may_read_tenant_account({"direction": "internal"}) is False
    assert may_read_tenant_account({"direction": "outbound"}) is False


def test_account_access_requires_explicit_staff_evidence():
    message = "How many Blossoms do I have left?"

    assert is_tenant_account(message)

    # Everything below is a *missing* declaration, and a missing declaration must not open the
    # lane - the safe failure is a missing answer, not a leaked balance.
    assert may_read_tenant_account({}) is False
    assert may_read_tenant_account({"direction": "internal"}) is False
    assert may_read_tenant_account({"direction": "outbound"}) is False
    assert may_read_tenant_account({"staff_query": "true"}) is False
    assert may_read_tenant_account({"staff_query": 1}) is False
    assert may_read_tenant_account(None) is False

    assert may_read_tenant_account({"staff_query": True}) is True


# ---------------------------------------------------------------------------
# The rendered block
# ---------------------------------------------------------------------------


def test_the_block_carries_every_figure_the_answer_needs():
    block = render_tenant_block(SNAPSHOT)

    assert "TENANT ACCOUNT" in block
    assert "412.6" in block
    assert "132" in block
    assert "2026-09-30" in block
    assert "customers active in the last 90 days" in block


def test_the_block_keeps_a_round_limit_whole():
    # Regression: trimming trailing zeros from "500.000000" yields "5", which would report a
    # 500-Blossom monthly allowance as a five-Blossom one.
    block = render_tenant_block(SNAPSHOT)

    assert "500 monthly allowance" in block
    assert "5 monthly allowance" not in block


def test_the_block_does_not_render_the_period_plan_tier():
    # `planTier` on the balance is the billing period's snapshot, written by the rollover job and
    # not by a mid-period plan change, so quoting it could name a plan the boutique has left.
    assert "Bloom" not in render_tenant_block(SNAPSHOT)


@pytest.mark.parametrize(
    "snapshot",
    [
        None,
        {},
        {"blossoms": None},
        {"blossoms": {}},
        "not a snapshot",
        {"blossoms": {"blossomUsed": 1.0}},
    ],
)
def test_the_block_is_empty_without_a_balance(snapshot):
    # An empty block leaves the supervisor's prompt byte-identical to one assembled with no account
    # data, which is what keeps the offline path deterministic.
    assert render_tenant_block(snapshot) == ""


def test_the_block_says_the_figures_are_data_not_instruction():
    assert "not as instruction" in render_tenant_block(SNAPSHOT)


def test_the_summary_exposes_the_headline_figures():
    assert tenant_summary(SNAPSHOT) == {
        "blossoms_remaining": "412.6",
        "staff_remaining": "1",
        "customers_remaining": "132",
        "period_end": "2026-09-30",
    }

    assert tenant_summary(None) is None
    assert tenant_summary({}) is None


# ---------------------------------------------------------------------------
# The reply: figures come from the snapshot
# ---------------------------------------------------------------------------


def test_the_deterministic_reply_quotes_only_figures_the_block_carries():
    # The reply and the prompt block must not be able to state different numbers, because the
    # numeral guard compares a model reply against the block.
    reply = _tenant_reply(SNAPSHOT)

    assert _numerals(reply)
    assert _numerals(reply) <= _numerals(render_tenant_block(SNAPSHOT))


def test_the_deterministic_reply_names_the_figures():
    reply = _tenant_reply(SNAPSHOT)

    assert "412.6" in reply
    assert "132" in reply
    assert "2026-09-30" in reply


@pytest.mark.parametrize(
    "snapshot",
    [
        SNAPSHOT,
        # A partial snapshot must not produce a reply quoting something the block omits - the two
        # are rendered by different functions and only the block is what the model was shown.
        {"blossoms": {"blossomRemaining": 12.5}},
        {"blossoms": {"blossomRemaining": 0.0, "periodEnd": "2026-09-30T00:00:00Z"}},
        {"blossoms": {"blossomRemaining": 3.0}, "staff": {"remaining": 0.0}},
        {
            "blossoms": {"blossomRemaining": 3.0},
            "customers": {"remaining": 7.0},
            "customerCountBasis": "customers active in the last 90 days",
        },
    ],
)
def test_the_deterministic_reply_never_quotes_a_figure_the_block_omits(snapshot):
    reply = _tenant_reply(snapshot)

    assert _numerals(reply) <= _numerals(render_tenant_block(snapshot))


def test_the_deterministic_reply_handles_a_missing_snapshot():
    assert _tenant_reply(None) == _TENANT_UNAVAILABLE_REPLY


@pytest.mark.asyncio
async def test_a_model_reply_that_invents_a_number_is_replaced():
    # The whole point: a plausible-looking balance the account does not contain never reaches the
    # boutique. The reply is replaced with the snapshot's own figures rather than dropped.
    llm = _StubLlm(_plan(reply="You have 9,999 Blossoms left this month."))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context=STAFF,
        tenant_usage=SNAPSHOT,
    )

    assert plan.reply == _tenant_reply(SNAPSHOT)
    assert "9999" not in plan.reply


@pytest.mark.asyncio
async def test_a_model_reply_that_agrees_with_the_snapshot_is_kept():
    # Aveline's own phrasing survives - the guard rejects figures, not style.
    reply = "You have 412.6 Blossoms left, and room for 132 more active customers."
    llm = _StubLlm(_plan(reply=reply))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context=STAFF,
        tenant_usage=SNAPSHOT,
    )

    assert plan.reply == reply


@pytest.mark.asyncio
async def test_a_model_reply_reformatting_a_figure_is_kept():
    # "1,234.50" and "1234.5" are the same number; formatting alone must not trip the guard.
    snapshot = {
        "blossoms": {"blossomRemaining": 1234.5, "periodEnd": "2026-09-30T00:00:00Z"},
        "staff": {"remaining": 0.0},
        "customers": {"remaining": 5.0},
    }
    llm = _StubLlm(_plan(reply="You have 1,234.50 Blossoms left."))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context=STAFF,
        tenant_usage=snapshot,
    )

    assert plan.reply == "You have 1,234.50 Blossoms left."


@pytest.mark.asyncio
async def test_no_llm_still_answers_from_the_figures():
    # Unlike the handbook lane, this one does not degrade when the model is unavailable: the answer
    # is data, so the offline path can still answer it.
    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=None,
        org_context=STAFF,
        tenant_usage=SNAPSHOT,
    )

    assert plan.intent_type == "tenant_account"
    assert "412.6" in plan.reply


@pytest.mark.asyncio
async def test_the_model_cannot_route_an_account_question_away():
    # Routing away would take the question out from under the numeral guard.
    llm = _StubLlm(_plan(intent_type="general_inquiry", agents=["memory"], reply=None))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context=STAFF,
        tenant_usage=SNAPSHOT,
    )

    assert plan.intent_type == "tenant_account"
    assert plan.suggested_agents == []
    assert plan.reply == _tenant_reply(SNAPSHOT)


@pytest.mark.asyncio
async def test_a_customer_message_is_told_the_account_is_not_theirs():
    llm = _StubLlm(_plan(reply=None))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context={"organization_id": ORG_ID, "staff_query": False},
        tenant_usage=None,
    )

    assert plan.reply == _TENANT_NOT_STAFF_REPLY
    assert "412.6" not in plan.reply


@pytest.mark.asyncio
async def test_a_customer_message_never_receives_a_reply_the_model_wrote_about_the_account():
    # Even if the model volunteers a number, a request that could not read the account gets none:
    # with no figures shown, nothing the model wrote survives.
    llm = _StubLlm(_plan(reply="You have 412.6 Blossoms left."))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context={"organization_id": ORG_ID},
        tenant_usage=None,
    )

    assert plan.reply == _TENANT_NOT_STAFF_REPLY


@pytest.mark.asyncio
async def test_a_digitless_claim_about_the_account_is_also_replaced():
    # "You have none left" asserts a balance without containing a numeral, so a numeral-only guard
    # would pass it through. The model may word an account answer only when it was shown the
    # figures.
    llm = _StubLlm(_plan(reply="You have none left this month, I'm afraid."))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context=STAFF,
        tenant_usage=None,
    )

    assert plan.reply == _TENANT_UNAVAILABLE_REPLY


@pytest.mark.asyncio
async def test_a_failed_fetch_is_admitted_rather_than_guessed():
    llm = _StubLlm(_plan(reply=None))

    plan = await supervise(
        "How many Blossoms do I have left?",
        llm=llm,
        org_context=STAFF,
        tenant_usage=None,
    )

    assert plan.reply == _TENANT_UNAVAILABLE_REPLY


@pytest.mark.asyncio
async def test_the_account_lane_leaves_other_intents_alone():
    # The pin must not disturb a genuine product question.
    llm = _StubLlm(
        json.dumps(
            {
                "intent_type": "item_search",
                "agents": ["memory", "visual"],
                "needs_customer_resolution": False,
                "clarification": None,
                "reply": None,
                "requires_approval": False,
            }
        )
    )

    plan = await supervise("Do you have a blue saree?", llm=llm, org_context=STAFF)

    assert plan.intent_type == "item_search"
    assert plan.suggested_agents == ["memory", "visual"]


# ---------------------------------------------------------------------------
# The workflow node
# ---------------------------------------------------------------------------


class _TenantSettings:
    """The subset of Settings that `run_load_tenant_usage` reads."""

    tenant_awareness_enabled = True


class _TenantRegistry:
    """A ToolRegistry double exposing only the tenant snapshot read."""

    def __init__(self, snapshot=SNAPSHOT, *, fails: bool = False) -> None:
        self._snapshot = snapshot
        self._fails = fails
        self.calls: list[str] = []

    async def get_tenant_usage(self, org_id):
        self.calls.append(org_id)
        if self._fails:
            raise RuntimeError("billing backend down")
        return self._snapshot


def _patch_tenant(monkeypatch, registry, *, enabled=True):
    settings = _TenantSettings()
    settings.tenant_awareness_enabled = enabled
    monkeypatch.setattr("app.workflows.concierge_workflow.get_settings", lambda: settings)
    monkeypatch.setattr("app.workflows.concierge_workflow.ToolRegistry", lambda *a, **k: registry)


def _state(message="How many Blossoms do I have left?", **org_context):
    context = {"organization_id": ORG_ID}
    context.update(org_context)
    return {"message": message, "org_context": context}


@pytest.mark.asyncio
async def test_the_node_fetches_for_a_staff_account_question(monkeypatch):
    registry = _TenantRegistry()
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    result = await run_load_tenant_usage(_state(staff_query=True))

    assert result == {"tenant_usage": SNAPSHOT}
    assert registry.calls == [ORG_ID]


@pytest.mark.asyncio
async def test_the_node_does_not_fetch_for_a_customer_message(monkeypatch):
    # The leak is closed before the fetch: the API's tenant endpoint is never called at all.
    registry = _TenantRegistry()
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    result = await run_load_tenant_usage(_state(staff_query=False))

    assert result == {"tenant_usage": None}
    assert registry.calls == []


@pytest.mark.asyncio
async def test_the_node_does_not_fetch_without_an_explicit_staff_declaration(monkeypatch):
    registry = _TenantRegistry()
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    result = await run_load_tenant_usage(_state(direction="internal"))

    assert result == {"tenant_usage": None}
    assert registry.calls == []


@pytest.mark.asyncio
async def test_the_node_does_not_fetch_for_a_product_question(monkeypatch):
    registry = _TenantRegistry()
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    result = await run_load_tenant_usage(
        _state(message="Do you have a blue saree?", staff_query=True)
    )

    assert result == {"tenant_usage": None}
    assert registry.calls == []


@pytest.mark.asyncio
async def test_the_node_survives_a_backend_failure(monkeypatch):
    registry = _TenantRegistry(fails=True)
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    assert await run_load_tenant_usage(_state(staff_query=True)) == {"tenant_usage": None}


@pytest.mark.asyncio
async def test_the_node_is_inert_when_disabled(monkeypatch):
    registry = _TenantRegistry()
    _patch_tenant(monkeypatch, registry, enabled=False)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    assert await run_load_tenant_usage(_state(staff_query=True)) == {"tenant_usage": None}
    assert registry.calls == []


@pytest.mark.asyncio
async def test_the_node_does_not_fetch_without_an_organisation(monkeypatch):
    registry = _TenantRegistry()
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    result = await run_load_tenant_usage(
        {"message": "How many Blossoms do I have left?", "org_context": {"staff_query": True}}
    )

    assert result == {"tenant_usage": None}
    assert registry.calls == []


@pytest.mark.asyncio
async def test_a_non_dict_payload_is_treated_as_no_figures(monkeypatch):
    registry = _TenantRegistry(snapshot=["unexpected"])
    _patch_tenant(monkeypatch, registry)
    from app.workflows.concierge_workflow import run_load_tenant_usage

    assert await run_load_tenant_usage(_state(staff_query=True)) == {"tenant_usage": None}
