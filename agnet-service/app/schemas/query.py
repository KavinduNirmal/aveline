"""Pydantic I/O models for the agent query endpoints."""

from pydantic import BaseModel, ConfigDict, Field


class AgentQueryRequest(BaseModel):
    """Payload for invoking an agent workflow."""

    model_config = ConfigDict(extra="forbid")

    query: str = Field(..., min_length=1, description="The user query to run.")
    thread_id: str | None = Field(
        default=None,
        description="Conversation/session thread ID used for checkpointing.",
    )


class AgentQueryResponse(BaseModel):
    """Result of a completed agent workflow."""

    model_config = ConfigDict(extra="forbid")

    status: str = "ok"
    result: str
    thread_id: str | None = None
