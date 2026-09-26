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

    # Must stay in step with `app.gate.IntentType`: the memory agent echoes the intent the gate
    # decided, so a value the gate can produce but this Literal rejects makes the agent's own output
    # fail validation. That is exactly what happened with `order_placement` - every purchase-intent
    # message ("...available for purchase?") made Ava emit an error and say nothing at all.
    intent_type: Literal[
        "order_placement", "item_search", "pricing_query", "customer_preference",
        "event_query", "out_of_scope", "general_inquiry", "aveline_help", "tenant_account",
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


class OnFileMemory(BaseModel):
    """A memory already stored against the customer, as a staff answer shows it.

    Distinct from :class:`ExtractedMemory`, which is what the agent decided to persist *this* turn:
    these are what the boutique already had, and they are the ones a "what do we know about this
    customer?" answer is actually about.
    """

    model_config = ConfigDict(extra="forbid")

    content: str = Field(..., min_length=1, max_length=2000)
    category: str = "memory"


def normalise_memory_content(content: str) -> str:
    """Reduce memory content to what makes two notes the same note.

    Case, surrounding whitespace and trailing punctuation are not distinctions a reader makes, and
    they are exactly how the same fact ends up stored twice. Lives here, beside the memory models,
    because both the agent that retrieves notes and the publisher that renders them must collapse
    the same pairs - two normalisers would eventually disagree about a duplicate.
    """
    return " ".join(content.strip().lower().rstrip(".!?").split())


class CustomerProfileSummary(BaseModel):
    """The customer profile the agent loads before drafting a response."""

    model_config = ConfigDict(extra="forbid")

    customer_id: str
    # The agent can know a customer by id/name without their phone number (e.g. resolved by
    # name), so the phone is optional in the agent's projection even though the backend record
    # always has one.
    phone_number: str | None = None
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
    #: What was already on file, de-duplicated. Carried separately from `extracted_memories` so a
    #: staff answer can show the notes as their own block instead of reciting them inside the brief
    #: sentence - reciting them produced "on file: X; X; X" whenever the store held near-duplicates.
    memories_on_file: list[OnFileMemory] = Field(default_factory=list)
    detected_events: list[DetectedEvent] = Field(default_factory=list)
    interaction_brief: str | None = None
    draft_response: str | None = None
    action_required: str | None = None
    reason: str | None = None
