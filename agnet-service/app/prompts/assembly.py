"""Assemble the full system prompt for an agent.

Composes the prompt layers in order:

1. Universal system prompt (loaded from ``agnet-service/app/prompts/SYSTEM_PROMPT.md``).
2. Agent-specific role & tone (from ``agent_prompts.AGENT_PROMPTS``).
3. Dynamic customer/subscription context (from ``context.build_customer_prompt``).
4. Optional dialogue context: the bounded conversation window (ADR-023), rendered by
   ``app.context.render_context_block``. It is kept out of layer 3 on purpose: business context
   and conversation context are different things with different lifetimes.
"""

from typing import Any

from app.prompts import agent_prompts, context
from app.prompts.loader import load_system_prompt


def assemble_system_prompt(
    agent_name: str,
    org_context: dict[str, Any] | None = None,
    dialogue_context: str | None = None,
) -> str:
    """Return the fully assembled system prompt for ``agent_name``.

    Args:
        agent_name: One of ``memory``, ``visual``, ``commerce``.
        org_context: Optional organization context for the dynamic layer.
        dialogue_context: Optional rendered conversation window (ADR-023). An empty or absent
            value leaves the prompt byte-identical to the three-layer form, which is what keeps
            the offline/CI path deterministic.

    Returns:
        The concatenated universal + agent-specific + context prompt, plus the dialogue context
        when there is one.

    Raises:
        KeyError: If ``agent_name`` is not a known agent.
    """
    if agent_name not in agent_prompts.AGENT_PROMPTS:
        raise KeyError(f"Unknown agent: {agent_name!r}")

    universal = load_system_prompt()
    agent_specific = agent_prompts.AGENT_PROMPTS[agent_name]
    customer_context = context.build_customer_prompt(org_context)

    parts = [universal, agent_specific, customer_context]
    if dialogue_context:
        parts.append(dialogue_context)

    return "\n\n".join(parts)
