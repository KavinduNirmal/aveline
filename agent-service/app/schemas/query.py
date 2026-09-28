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


class AgentResumeRequest(BaseModel):
    """Payload for settling a paused workflow from its checkpoint (ADR-024, Decision 3)."""

    model_config = ConfigDict(extra="forbid")

    thread_id: str = Field(..., min_length=1, description="The paused conversation's thread id.")
    decision: str = Field(
        ...,
        min_length=1,
        description=(
            "The owner's decision in the graph's own vocabulary: "
            "'approved', 'rejected' or 'revised'. The API translates its HTTP verbs before sending."
        ),
    )
    comment: str | None = Field(default=None, description="The owner's note, if any.")
    organization_id: str | None = Field(default=None, description="Tenant scope, for telemetry.")
    order_id: str | None = Field(default=None, description="The order the decision applies to.")
    customer_name: str | None = Field(
        default=None,
        description=(
            "The customer the order is for, so the settlement can address them by name. The API has "
            "this by the time a human decides; the checkpoint's context often does not."
        ),
    )
    revised_discount: float | None = Field(
        default=None, description="The re-negotiated discount, when the decision is 'revised'."
    )
    customer_id: str | None = Field(default=None, description="Tenant customer, for telemetry.")
    conversation_id: str | None = Field(default=None, description="Salon thread, for telemetry.")
