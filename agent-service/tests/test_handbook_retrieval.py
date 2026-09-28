"""Tests for handbook retrieval and grounding (ADR-025).

Three things decide whether a platform question is answered well, and each is asserted here:

1. **Classification.** ``aveline_help`` has to catch "how do I invite a staff member?" without
   stealing "do you have a blue saree?" or "can you help me with something?". A false positive
   routes a product question to the handbook; a false negative is the old behaviour.
2. **Grounding.** The retrieved excerpts have to reach the prompt, labelled, with the
   no-invention rule stated.
3. **Degradation.** With no model there is nothing to compose an answer from, so the question must
   fall back to the conversational path rather than going unanswered.
"""

import json

import pytest

from app.context import render_handbook_block
from app.gate import classify_by_rules, is_aveline_help, supervise


class _StubLlm:
    """Chat-model double returning a scripted reply and recording the prompts it saw."""

    def __init__(self, content: str) -> None:
        self._content = content
        self.prompts: list = []

    async def ainvoke(self, messages) -> object:
        self.prompts.append(messages)
        return type("_Response", (), {"content": self._content})()


def _plan(**overrides) -> str:
    payload = {
        "intent_type": "aveline_help",
        "agents": [],
        "needs_customer_resolution": False,
        "clarification": None,
        "reply": "Invite them from Team; the code lasts as long as you choose.",
        "requires_approval": False,
    }
    payload.update(overrides)
    return json.dumps(payload)


def _hit(
    content: str = "Open Team and mint an invitation code.",
    title: str = "Team",
    heading: str = "Invitations > Generate a code",
    key: str = "web-docs/team",
    url: str = "/docs/team",
) -> dict:
    return {
        "sourceKey": key,
        "sourceTitle": title,
        "sourceUrl": url,
        "headingPath": heading,
        "content": content,
        "score": 0.03,
    }


# --------------------------------------------------------------------------- classification


@pytest.mark.parametrize(
    "message",
    [
        "How do I invite a staff member?",
        "how to add a piece to the catalog",
        "Where can I see my Blossom balance?",
        "What is Aveline?",
        "How much is the Orchid plan?",
        "what does the approval queue do",
        "what is my plan allowance",
        "I want to top up",
        "How do I set up the WhatsApp webhook?",
    ],
)
def test_platform_questions_classify_as_aveline_help(message):
    assert classify_by_rules(message).intent_type == "aveline_help"


@pytest.mark.parametrize(
    "message",
    [
        "Do you have a blue saree for a wedding?",
        "order the pink one",
        "How much is this dress?",
        "I prefer silk over linen",
        "Can you help me with something?",
        "Find me something for a December wedding",
        "Tell me about the emerald green collection",
    ],
)
def test_product_and_general_messages_are_not_platform_questions(message):
    # Precision matters more than recall: a false positive here would pull a product question out of
    # Elle's lane and into the handbook's.
    assert classify_by_rules(message).intent_type != "aveline_help"


def test_the_out_of_scope_guard_still_wins():
    # "how do I write code" matches a help shape, so the narrow out-of-scope list must be checked
    # first or the gate would route a refusal into the handbook.
    assert classify_by_rules("how do I write code").intent_type == "out_of_scope"


def test_is_aveline_help_is_the_single_predicate_the_workflow_uses():
    assert is_aveline_help("how do I invite a staff member") is True
    assert is_aveline_help("do you have a blue saree") is False


# --------------------------------------------------------------------------- grounding


@pytest.mark.asyncio
async def test_a_platform_question_consults_the_supervisor():
    llm = _StubLlm(_plan())

    plan = await supervise("How do I invite a staff member?", llm=llm)

    assert len(llm.prompts) == 1
    assert plan.intent_type == "aveline_help"
    assert plan.reply


@pytest.mark.asyncio
async def test_the_retrieved_excerpts_reach_the_prompt():
    llm = _StubLlm(_plan())

    await supervise(
        "How do I invite a staff member?",
        llm=llm,
        handbook_hits=[_hit()],
    )

    prompt = llm.prompts[0][-1].content
    assert "HANDBOOK" in prompt
    assert "Open Team and mint an invitation code." in prompt
    # Labelled with its provenance, so the model can attribute what it says.
    assert "Team" in prompt
    assert "Invitations > Generate a code" in prompt


@pytest.mark.asyncio
async def test_the_prompt_forbids_inventing_platform_facts():
    llm = _StubLlm(_plan())

    await supervise("How do I invite a staff member?", llm=llm, handbook_hits=[_hit()])

    prompt = llm.prompts[0][-1].content
    assert "the only source you may answer" in prompt
    assert "Blossom" in prompt  # the no-amounts rule


@pytest.mark.asyncio
async def test_no_excerpts_leaves_the_prompt_unchanged():
    llm = _StubLlm(_plan())

    await supervise("How do I invite a staff member?", llm=llm, handbook_hits=[])

    prompt = llm.prompts[0][-1].content
    # The instruction always names the HANDBOOK layer; what must be absent is any rendered excerpt.
    assert "[1]" not in prompt
    assert "Open Team and mint an invitation code." not in prompt


@pytest.mark.asyncio
async def test_a_platform_question_with_no_model_answer_admits_it():
    # The model was consulted but wrote no reply: silence would leave an explicitly asked platform
    # question unanswered, so a plain admission is used instead.
    llm = _StubLlm(_plan(reply=None))

    plan = await supervise("How do I invite a staff member?", llm=llm)

    assert plan.reply
    assert "handbook" in plan.reply.lower()


# --------------------------------------------------------------------------- degradation


@pytest.mark.asyncio
async def test_without_a_model_a_platform_question_degrades_to_the_conversational_path():
    # No model means no grounded answer, so the question must not be left unanswered.
    plan = await supervise("How do I invite a staff member?", llm=None)

    assert plan.intent_type == "general_inquiry"
    assert plan.reply
    assert plan.suggested_agents == ["memory"]


@pytest.mark.asyncio
async def test_a_provider_failure_also_degrades_a_platform_question():
    class _ExplodingLlm:
        async def ainvoke(self, messages):
            raise RuntimeError("provider unavailable")

    plan = await supervise("What is a Blossom?", llm=_ExplodingLlm())

    assert plan.intent_type == "general_inquiry"
    assert plan.reply


@pytest.mark.asyncio
async def test_a_content_route_still_wins_over_a_reply():
    # A plan that routes Elle must not also carry Aveline's own answer.
    llm = _StubLlm(_plan(agents=["memory", "visual"], reply="Here is what I think."))

    plan = await supervise("How much is this dress?", llm=llm)

    assert plan.reply is None


# --------------------------------------------------------------------------- rendering


def test_render_handbook_block_is_empty_without_hits():
    assert render_handbook_block(None) == ""
    assert render_handbook_block([]) == ""
    assert render_handbook_block([{"content": "   "}]) == ""


def test_render_handbook_block_labels_every_excerpt():
    block = render_handbook_block([_hit(), _hit(content="Another excerpt.", title="Blossoms")])

    assert block.startswith("HANDBOOK")
    assert "[1]" in block
    assert "[2]" in block
    assert "Another excerpt." in block
    assert "Blossoms" in block


def test_render_handbook_block_tolerates_snake_case_keys():
    # The registry returns whatever the API sends; both spellings are accepted so a rename on one
    # side cannot silently empty the block.
    block = render_handbook_block(
        [{"content": "Body.", "source_title": "Team", "heading_path": "Invitations"}]
    )

    assert "Team" in block
    assert "Invitations" in block
