"""The Intent Gate — entry point for all inbound agent requests.

Classifies what the user wants, checks relevance/safety, and routes to the
correct agent(s). It is **hybrid**: deterministic keyword rules run first (fast,
cheap, testable); an optional LLM classifier refines ambiguous input.

The gate is shared infrastructure. It only decides routing — it never executes
slice business logic.
"""

import logging
from collections.abc import Awaitable, Callable
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

logger = logging.getLogger("aveline.agent.gate")

IntentType = Literal[
    "item_search",
    "pricing_query",
    "customer_preference",
    "event_query",
    "out_of_scope",
    "general_inquiry",
]

AgentName = Literal["memory", "visual", "commerce"]

# Optional async LLM classifier: returns a refined output or None to keep rules.
LLMClassifier = Callable[[str], Awaitable["IntentGateOutput | None"]]

# Keyword -> intent type. Order matters: first match wins.
_RULE_KEYWORDS: list[tuple[IntentType, tuple[str, ...]]] = [
    # Pricing words are strong signals and often co-occur with item words
    # (e.g. "how much is this dress?"), so they are checked first.
    ("pricing_query", ("price", "cost", "discount", "budget", "margin", "how much", "charge")),
    ("item_search", ("dress", "saree", "blouse", "outfit", "party", "bluish", "size", "stock", "inventory", "item")),
    ("customer_preference", ("remember", "prefer", "prefers", "likes", "favorite", "hates", "dislikes")),
    ("event_query", ("event", "wedding", "birthday", "anniversary", "occasion")),
]

# Phrases that clearly fall outside the boutique domain.
_OUT_OF_SCOPE_KEYWORDS = (
    "write code",
    "python script",
    "javascript",
    "tell me a joke",
    "recipe",
    "weather",
    "news",
    "math problem",
)

# Which agents handle each intent.
_AGENT_ROUTING: dict[IntentType, list[AgentName]] = {
    "item_search": ["memory", "visual"],
    "pricing_query": ["memory", "commerce"],
    "customer_preference": ["memory"],
    "event_query": ["memory"],
    "general_inquiry": ["memory"],
    "out_of_scope": [],
}


class IntentGateOutput(BaseModel):
    """Result of classifying and routing an inbound message."""

    model_config = ConfigDict(extra="forbid")

    intent_type: IntentType
    suggested_agents: list[AgentName] = Field(default_factory=list)
    requires_approval: bool = False
    is_relevant: bool = True
    safety_flags: list[str] = Field(default_factory=list)


def classify_by_rules(message: str) -> IntentGateOutput:
    """Classify ``message`` using deterministic keyword rules.

    Args:
        message: The raw inbound message text.

    Returns:
        An ``IntentGateOutput`` with the rule-based classification.
    """
    lowered = message.lower()

    if any(keyword in lowered for keyword in _OUT_OF_SCOPE_KEYWORDS):
        return IntentGateOutput(
            intent_type="out_of_scope",
            suggested_agents=[],
            is_relevant=False,
            safety_flags=["out_of_scope"],
        )

    for intent_type, keywords in _RULE_KEYWORDS:
        if any(keyword in lowered for keyword in keywords):
            return IntentGateOutput(
                intent_type=intent_type,
                suggested_agents=_AGENT_ROUTING[intent_type],
            )

    return IntentGateOutput(
        intent_type="general_inquiry",
        suggested_agents=_AGENT_ROUTING["general_inquiry"],
    )


async def infer_intent(
    message: str,
    customer_context: dict | None = None,
    llm_classifier: LLMClassifier | None = None,
) -> IntentGateOutput:
    """Infer the intent of ``message``, routing to the appropriate agents.

    Runs deterministic rules first. When the rules yield an ambiguous result
    (``general_inquiry``) and an ``llm_classifier`` is provided, the LLM is
    consulted to refine the classification. If the LLM returns ``None`` or an
    invalid result, the rule-based output is kept.

    Args:
        message: The raw inbound message text.
        customer_context: Optional customer context (reserved for future use).
        llm_classifier: Optional async callable that returns a refined
            ``IntentGateOutput`` or ``None``.

    Returns:
        The final ``IntentGateOutput``.
    """
    del customer_context  # reserved for future context injection
    rule_result = classify_by_rules(message)

    if llm_classifier is not None and rule_result.intent_type == "general_inquiry":
        try:
            refined = await llm_classifier(message)
        except Exception:  # noqa: BLE001 - a classifier failure must not break the gate
            logger.exception("LLM intent classifier failed; falling back to rules.")
            refined = None
        if refined is not None:
            logger.debug("Intent refined by LLM: %s", refined.intent_type)
            return refined

    return rule_result
