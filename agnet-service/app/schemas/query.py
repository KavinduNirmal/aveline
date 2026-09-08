"""Pydantic I/O models for the agent query endpoints."""

from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from app.schemas.response import AgentResponse


class AgentQueryRequest(BaseModel):
    """Payload for invoking an agent workflow."""

    model_config = ConfigDict(extra="forbid")

    query: str = Field(..., min_length=1, description="The user query to run.")
    thread_id: str | None = Field(
        default=None,
        description="Conversation/session thread ID used for checkpointing.",
    )
    org_context: dict[str, Any] | None = Field(
        default=None,
        description="Optional organization context (plan tier, brand voice, rules).",
    )


class AgentQueryResponse(BaseModel):
    """Transport wrapper around a completed agent workflow result."""

    model_config = ConfigDict(extra="forbid")

    status: str = "ok"
    result: AgentResponse = Field(..., description="The structured agent response envelope.")
    thread_id: str | None = None
