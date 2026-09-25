"""Shared agent output envelope (the stable contract with ASP.NET Core).

Every agent output must conform to this envelope so the backend can rely on a
single, auditable response shape. The universal system prompt references the same
``status`` values and ``metadata`` fields.
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class AgentStatus(StrEnum):
    """Terminal status of an agent workflow."""

    success = "success"
    pending_approval = "pending_approval"
    out_of_scope = "out_of_scope"
    #: The run was deliberately not performed (e.g. the customer revoked consent, or their consent
    #: could not be read). Distinct from ``success`` so run reporting and analytics do not count a
    #: privacy skip as a completed answer (plan §11 Phase 1 item 1.6).
    skipped = "skipped"
    error = "error"


class AgentMetadata(BaseModel):
    """Optional usage/observability metadata attached to an agent output."""

    model_config = ConfigDict(extra="forbid")

    duration_ms: int | None = Field(default=None, description="Workflow duration in milliseconds.")
    model: str | None = Field(default=None, description="LLM model used (or 'rule-based').")
    tokens_used: int | None = Field(default=None, description="Total tokens consumed.")
    input_tokens: int | None = Field(default=None, description="Prompt tokens consumed.")
    output_tokens: int | None = Field(default=None, description="Completion tokens generated.")
    blossoms_consumed: int | None = Field(
        default=None,
        description="Blossom units consumed (determined by the backend, ADR-010).",
    )


class AgentOutput(BaseModel):
    """The structured result of a single agent step or the whole workflow."""

    model_config = ConfigDict(extra="forbid")

    status: AgentStatus = Field(..., description="Terminal status of the output.")
    output: dict[str, Any] = Field(..., description="Structured payload.")
    metadata: AgentMetadata | None = Field(default=None, description="Usage metadata.")


class AgentResponse(BaseModel):
    """Top-level response envelope returned to ASP.NET Core."""

    model_config = ConfigDict(extra="forbid")

    status: AgentStatus = Field(..., description="Terminal status of the workflow.")
    output: dict[str, Any] = Field(..., description="Structured payload.")
    metadata: AgentMetadata | None = Field(default=None, description="Usage metadata.")
