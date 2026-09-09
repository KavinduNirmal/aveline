"""Persona-attributed message publishing for the Salon (ADR-016).

The concierge workflow produces an ``AgentResponse``. This module turns it into one or
more ``message.created`` events attributed to the personas that produced real content:

- **Aveline** (the orchestrator) always emits a summary ``Note``.
- **Ava** (memory) emits when the memory agent produced real customer content
  (brief, memories, draft response).
- **Elle** (visual) and **Lina** (commerce) emit when their sub-output carries content;
  their stubs are wired by Issue #151.

The API consumes these events and becomes the system of record; the agent service never
writes message rows directly (ADR-016).
"""

import logging
from typing import Any
from uuid import UUID

from app.events.block_builders import (
    build_ava_blocks,
    build_aveline_blocks,
    build_elle_blocks,
    build_lina_blocks,
)
from app.schemas.response import AgentResponse, AgentStatus

logger = logging.getLogger("aveline.agent.events")

MESSAGE_CREATED = "message.created"

#: persona key -> workflow output field -> block builder.
_SPECIALISTS = (
    ("ava", "memory", build_ava_blocks),
    ("elle", "visual", build_elle_blocks),
    ("lina", "commerce", build_lina_blocks),
)


def _text_block(text: str) -> list[dict[str, Any]]:
    return [{"type": "text", "text": text}]


def build_agent_messages(
    result: AgentResponse,
    thread_id: str | None = None,
    workflow_run_id: UUID | None = None,
) -> list[dict[str, Any]]:
    """Build ``message.created`` payloads attributed to the personas that produced content.

    Args:
        result: The concierge ``AgentResponse``.
        thread_id: The LangGraph checkpoint thread id (the conversation anchor).
        workflow_run_id: Correlation id for the workflow run.

    Returns:
        A list of ``message.created`` payload dicts (snake_case), one per persona.
    """
    output = result.output or {}
    messages: list[dict[str, Any]] = []

    if result.status == AgentStatus.out_of_scope:
        reason = (output.get("reason") or "This request is outside the boutique domain.") if isinstance(output, dict) else "This request is outside the boutique domain."
        messages.append(_message("aveline", "Note", _text_block(str(reason)), thread_id, workflow_run_id))
        return messages

    # Aveline (orchestrator) always summarizes the outcome.
    aveline_blocks = build_aveline_blocks(output)
    if aveline_blocks:
        messages.append(_message("aveline", "Note", aveline_blocks, thread_id, workflow_run_id))

    # Each specialist that produced real content gets its own attributed message.
    for agent_key, field, builder in _SPECIALISTS:
        sub_output = output.get(field) if isinstance(output, dict) else None
        blocks = builder(sub_output)
        if blocks:
            messages.append(_message(agent_key, "Note", blocks, thread_id, workflow_run_id))

    return messages


def _message(
    agent_key: str,
    kind: str,
    blocks: list[dict[str, Any]],
    thread_id: str | None,
    workflow_run_id: UUID | None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "thread_id": thread_id,
        "author": {"agent_key": agent_key},
        "kind": kind,
        "blocks": blocks,
    }
    if workflow_run_id is not None:
        payload["workflow_run_id"] = str(workflow_run_id)
    return payload


async def publish_agent_messages(
    bus: Any,
    org_id: UUID,
    thread_id: str | None,
    result: AgentResponse,
    workflow_run_id: UUID | None = None,
) -> None:
    """Publish one ``message.created`` event per persona-attributed message.

    Args:
        bus: The event bus (duck-typed ``publish``).
        org_id: The organization the conversation belongs to.
        thread_id: The LangGraph checkpoint thread id.
        result: The concierge ``AgentResponse``.
        workflow_run_id: Optional correlation id for the workflow run.
    """
    for payload in build_agent_messages(result, thread_id=thread_id, workflow_run_id=workflow_run_id):
        await bus.publish(MESSAGE_CREATED, org_id, payload, trace_id=workflow_run_id)
        logger.debug("Published message.created for persona %s.", payload["author"]["agent_key"])
