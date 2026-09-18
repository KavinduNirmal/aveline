"""Assemble the full system prompt for an agent.

Composes the three prompt layers in order:

1. Universal system prompt (loaded from ``agnet-service/app/prompts/SYSTEM_PROMPT.md``).
2. Agent-specific role & tone (from ``agent_prompts.AGENT_PROMPTS``).
3. Dynamic customer/subscription context (from ``context.build_customer_prompt``).
"""

from typing import Any

from app.prompts import agent_prompts, context
from app.prompts.loader import load_system_prompt


def assemble_system_prompt(
    agent_name: str,
    org_context: dict[str, Any] | None = None,
) -> str:
    """Return the fully assembled system prompt for ``agent_name``.

    Args:
        agent_name: One of ``memory``, ``visual``, ``commerce``.
        org_context: Optional organization context for the dynamic layer.

    Returns:
        The concatenated universal + agent-specific + context prompt.

    Raises:
        KeyError: If ``agent_name`` is not a known agent.
    """
    if agent_name not in agent_prompts.AGENT_PROMPTS:
        raise KeyError(f"Unknown agent: {agent_name!r}")

    universal = load_system_prompt()
    agent_specific = agent_prompts.AGENT_PROMPTS[agent_name]
    customer_context = context.build_customer_prompt(org_context)

    return f"{universal}\n\n{agent_specific}\n\n{customer_context}"
