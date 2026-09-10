"""Event envelope contract for the Redis event bus (ADR-014).

The JSON wire format is snake_case and mirrors the C# ``EventEnvelope`` in
``Aveline.Api/Infrastructure/Eventing`` so both services serialize identically:
``event_id``, ``event_type``, ``timestamp``, ``org_id``, ``trace_id``, ``payload``.
"""

from datetime import UTC, datetime
from typing import Any
from uuid import UUID, uuid4

from pydantic import BaseModel, Field


class EventEnvelope(BaseModel):
    """Canonical envelope exchanged over the Redis event bus."""

    event_id: UUID = Field(default_factory=uuid4)
    event_type: str
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))
    org_id: UUID | None = None
    trace_id: UUID | None = None
    payload: dict[str, Any] | None = None
