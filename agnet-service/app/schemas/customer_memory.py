"""Pydantic I/O models for the Customer Memory Agent (Slice 1).

These are the typed inputs/outputs of the memory sub-graph. They mirror the backend
``CustomerConcierge`` DTO contract (ASP.NET Core). Models forbid extra fields so a
schema/contract drift fails loudly instead of silently.
"""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

MemoryCategory = Literal["preference", "event", "complaint", "fact", "sentiment"]
MemorySource = Literal["conversation", "staff_note", "purchase", "inferred"]
EventType = Literal["wedding", "birthday", "party", "office", "other"]
ChannelType = Literal["whatsapp", "instagram", "in_person", "phone"]
DirectionType = Literal["inbound", "outbound"]


class ParsedIntent(BaseModel):
    """Structured intent extracted from a raw customer message."""

    model_config = ConfigDict(extra="forbid")

    intent_type: Literal[
        "item_search", "pricing_query", "customer_preference",
        "event_query", "order_status", "general_inquiry",
    ]
    occasion: str | None = None
    color: str | None = None
    size: str | None = None
    budget: float | None = None
    urgency: str | None = None


class ExtractedMemory(BaseModel):
    """A memory the agent decided to persist about a customer."""

    model_config = ConfigDict(extra="forbid")

    content: str = Field(..., min_length=1, max_length=2000)
    category: MemoryCategory = "fact"
    source: MemorySource = "conversation"
    is_explicit: bool = False
    confidence: float = Field(default=0.50, ge=0.0, le=1.0)


class DetectedEvent(BaseModel):
    """An event (wedding, birthday, ...) detected in a message."""

    model_config = ConfigDict(extra="forbid")

    event_type: EventType = "other"
    event_date: str = Field(..., description="ISO-8601 date (yyyy-MM-dd).")
    description: str | None = None


class CustomerProfileSummary(BaseModel):
    """The customer profile the agent loads before drafting a response."""

    model_config = ConfigDict(extra="forbid")

    customer_id: str
    phone_number: str
    full_name: str | None = None
    status: str
    consent_status: str = "pending"


class MemoryAgentOutput(BaseModel):
    """Structured output produced by the Customer Memory Agent sub-graph.

    Designed to be embedded into the shared concierge ``AgentResponse`` envelope
    (the ``output`` payload). See ``app/schemas/response.py``.
    """

    model_config = ConfigDict(extra="forbid")

    status: Literal["success", "pending_approval", "out_of_scope", "skipped", "error"]
    parsed_intent: ParsedIntent | None = None
    customer: CustomerProfileSummary | None = None
    extracted_memories: list[ExtractedMemory] = Field(default_factory=list)
    detected_events: list[DetectedEvent] = Field(default_factory=list)
    interaction_brief: str | None = None
    draft_response: str | None = None
    action_required: str | None = None
    reason: str | None = None
