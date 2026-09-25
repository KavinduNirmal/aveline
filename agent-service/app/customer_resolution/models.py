"""Customer resolution models (Issue #161).

A discriminated result describing whether a customer could be resolved from an inbound
message, and the candidate matches when the name is ambiguous.
"""

from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

ResolutionKind = Literal["resolved", "ambiguous", "not_found", "no_signal"]


class CustomerCandidate(BaseModel):
    """A single potential customer returned by the backend lookup."""

    model_config = ConfigDict(extra="forbid")

    customer_id: str
    full_name: str | None = None
    phone_number: str | None = None
    status: str = "new"
    last_visit_at: str | None = None


class CustomerResolution(BaseModel):
    """Outcome of resolving a customer from a message (or provided context)."""

    model_config = ConfigDict(extra="forbid")

    kind: ResolutionKind
    customer_id: str | None = Field(default=None, description="Set when ``kind == resolved``.")
    profile: dict[str, Any] | None = Field(default=None, description="Resolved customer profile (camelCase).")
    candidates: list[CustomerCandidate] = Field(
        default_factory=list, description="Set when ``kind == ambiguous``."
    )
    message: str = Field(default="", description="The inbound message that drove resolution.")
    explicit_mention: bool = Field(
        default=False,
        description=(
            "True when resolution was driven by an explicit mention typed in the message "
            "(@name or #phone, ADR-019) rather than by supplied context. This is what lets a "
            "caller tell a staff lookup apart from an inbound sender: an inbound sender's own "
            "number arrives as context, and asking them for it would be incoherent."
        ),
    )

    @property
    def is_resolved(self) -> bool:
        return self.kind == "resolved"

    @property
    def needs_clarification(self) -> bool:
        return self.kind in ("ambiguous", "not_found")
