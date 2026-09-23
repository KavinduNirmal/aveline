"""Tests for the supervisor routing authority (ADR-023, Decision 3).

The supervisor is the one place a routing decision is made, and its failure modes matter more than
its happy path: a bad plan routes work to nobody, and a crash would break every inbound message. So
the fallback behaviour is pinned as carefully as the refinement.
"""

import json

import pytest

from app.gate import classify_by_rules, supervise


class _StubLlm:
    """Chat-model double returning a scripted reply and recording the prompts it saw."""

    def __init__(self, content: str) -> None:
        self._content = content
        self.prompts: list = []

    async def ainvoke(self, messages) -> object:
        self.prompts.append(messages)
        return type("_Response", (), {"content": self._content})()


class _ExplodingLlm:
    async def ainvoke(self, messages) -> object:
        raise RuntimeError("provider unavailable")


def _plan(**overrides) -> str:
    payload = {
        "intent_type": "item_search",
        "agents": ["memory", "visual"],
        "needs_customer_resolution": False,
        "clarification": None,
        "requires_approval": False,
    }
    payload.update(overrides)
    return json.dumps(payload)


# ---------------------------------------------------------------------------
# Rule pre-filter and the offline guarantee
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_returns_the_rule_plan_when_no_llm_is_configured():
    # I3: with no LLM the decision is exactly the deterministic rules, unchanged.
    plan = await supervise("Do you have a blue saree?", llm=None)

    rules = classify_by_rules("Do you have a blue saree?")
    assert plan.intent_type == rules.intent_type
    assert plan.suggested_agents == list(rules.suggested_agents)


@pytest.mark.asyncio
async def test_does_not_consult_the_model_when_the_rules_are_confident():
    # A confident rule match is free and correct; paying for a model call there would be waste.
    llm = _StubLlm(_plan())

    plan = await supervise("How much is this dress?", llm=llm)

    assert llm.prompts == []
    assert plan.intent_type == "pricing_query"


@pytest.mark.asyncio
async def test_consults_the_model_only_for_general_inquiry():
    llm = _StubLlm(_plan(intent_type="item_search", agents=["memory", "visual"]))

    plan = await supervise("Are there any pinkish gowns in your collection?", llm=llm)

    assert len(llm.prompts) == 1
    # The rules cannot recognise "gowns"; the supervisor can, and routing follows it.
    assert plan.intent_type == "item_search"
    assert plan.suggested_agents == ["memory", "visual"]


# ---------------------------------------------------------------------------
# Failing safely
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_survives_a_provider_failure():
    # Routing must always produce something; the rules are a complete fallback, not a partial one.
    plan = await supervise(
        "Are there any pinkish gowns in your collection?", llm=_ExplodingLlm()
    )

    assert plan.intent_type == "general_inquiry"


@pytest.mark.asyncio
async def test_ignores_an_unparseable_reply():
    plan = await supervise("Any pinkish gowns?", llm=_StubLlm("I think you want a dress."))
    assert plan.intent_type == "general_inquiry"


@pytest.mark.asyncio
async def test_ignores_an_unknown_intent_rather_than_routing_to_nothing():
    # An intent outside the enum would route to no agent at all; the rule decision is safer.
    plan = await supervise("Any pinkish gowns?", llm=_StubLlm(_plan(intent_type="something_else")))
    assert plan.intent_type == "general_inquiry"


@pytest.mark.asyncio
async def test_ignores_an_empty_reply():
    plan = await supervise("Any pinkish gowns?", llm=_StubLlm(""))
    assert plan.intent_type == "general_inquiry"


# ---------------------------------------------------------------------------
# Parsing tolerance
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_accepts_a_code_fenced_reply():
    llm = _StubLlm(f"```json\n{_plan(intent_type='item_search')}\n```")
    plan = await supervise("Any pinkish gowns?", llm=llm)
    assert plan.intent_type == "item_search"


@pytest.mark.asyncio
async def test_drops_unknown_agent_names_but_keeps_valid_ones():
    # Lenient on purpose: an invented agent must not discard an otherwise usable decision.
    llm = _StubLlm(_plan(agents=["memory", "shipping", "visual"]))
    plan = await supervise("Any pinkish gowns?", llm=llm)
    assert plan.suggested_agents == ["memory", "visual"]


@pytest.mark.asyncio
async def test_falls_back_to_the_routing_table_when_no_agents_are_named():
    llm = _StubLlm(_plan(intent_type="item_search", agents=[]))
    plan = await supervise("Any pinkish gowns?", llm=llm)
    # A plan that names no agents would run nothing; the table supplies the intent's set.
    assert plan.suggested_agents == ["memory", "visual"]


@pytest.mark.asyncio
async def test_treats_a_blank_clarification_as_no_question():
    llm = _StubLlm(_plan(clarification="   "))
    plan = await supervise("Any pinkish gowns?", llm=llm)
    assert plan.clarification is None


@pytest.mark.asyncio
async def test_preserves_a_clarification_the_supervisor_asks():
    llm = _StubLlm(_plan(clarification="Which one did you mean - the silk or the linen?"))
    plan = await supervise("that one", llm=llm)
    assert plan.clarification == "Which one did you mean - the silk or the linen?"


@pytest.mark.asyncio
async def test_reads_the_routing_flags():
    llm = _StubLlm(_plan(needs_customer_resolution=True, requires_approval=True))
    plan = await supervise("Any pinkish gowns?", llm=llm)
    assert plan.needs_customer_resolution is True
    assert plan.requires_approval is True


# ---------------------------------------------------------------------------
# What the supervisor is shown
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_the_prompt_carries_the_conversation_context():
    # The whole point of the supervisor over the bare rules: it can resolve a reference. That is
    # only possible if the window, summary and pinned slots actually reach the prompt.
    llm = _StubLlm(_plan())

    await supervise(
        "the pink one",
        llm=llm,
        history=[
            {"authorKind": "System", "text": "Any pinkish gowns?"},
            {"authorKind": "Agent", "text": "We have three."},
        ],
        thread_summary="She is shopping for a December wedding.",
        pinned_slots={"budget": "50k"},
    )

    human = llm.prompts[0][-1].content
    assert "Any pinkish gowns?" in human
    assert "We have three." in human
    assert "December wedding" in human
    assert "50k" in human
    assert "the pink one" in human


@pytest.mark.asyncio
async def test_the_prompt_uses_the_supervisor_prompt_layer():
    # The supervisor must compose through the shared prompt assembly, not a private string.
    llm = _StubLlm(_plan())

    await supervise("the pink one", llm=llm)

    system = llm.prompts[0][0].content
    assert "Supervisor" in system
    # The routing-only boundary is stated in the prompt, not merely implied by the schema (I7).
    assert "Routing only" in system


@pytest.mark.asyncio
async def test_the_prompt_includes_the_json_contract():
    llm = _StubLlm(_plan())
    await supervise("the pink one", llm=llm)
    assert "intent_type" in llm.prompts[0][-1].content


# ---------------------------------------------------------------------------
# The supervisor's clarification is a first-class outcome (ADR-023, Decision 4)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_an_asked_clarification_is_rendered_and_replaces_specialist_work(monkeypatch):
    """A question the supervisor asks is rendered verbatim and no specialist runs.

    This is the outcome the old design could not express: it could only ask as a side effect of
    the customer-resolution veto. Asking is now a decision the supervisor makes deliberately.
    """
    from app.events.block_builders import build_clarification_blocks
    from app.workflows.concierge_workflow import build_concierge_graph

    async def planner(message, **kwargs):
        from app.gate import SupervisorPlan

        return SupervisorPlan(
            intent_type="general_inquiry",
            suggested_agents=["memory"],
            clarification="Which one did you mean - the silk or the linen?",
        )

    monkeypatch.setattr("app.workflows.concierge_workflow.supervise", planner)

    result = await build_concierge_graph().ainvoke(
        {
            "message": "that one",
            "org_context": {"organization_id": "org-1"},
            "history": [],
            "thread_summary": None,
            "pinned_slots": {},
            "intent": None,
            "resolution": None,
            "memory_output": None,
            "visual_output": None,
            "commerce_output": None,
            "response": None,
        }
    )

    clarification = result["response"]["output"]["clarification"]
    assert clarification["kind"] == "asked"
    assert result["memory_output"] is None, "specialists must not run when a question is asked"

    blocks = build_clarification_blocks(clarification)
    assert blocks[0]["text"] == "Which one did you mean - the silk or the linen?"
