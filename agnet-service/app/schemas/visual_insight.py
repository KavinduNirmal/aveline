"""Pydantic I/O models for the Visual Insight Agent (Elle — Slice 2).

These are the typed inputs/outputs of the visual intelligence and sourcing sub-graph.
Models forbid extra fields so schema/contract drift fails loudly.
"""

from typing import Any, Literal
from pydantic import BaseModel, ConfigDict, Field


class ImageAttributes(BaseModel):
    """Structured fashion and aesthetic attributes extracted from an image."""

    model_config = ConfigDict(extra="forbid")

    category: str = Field(..., description="Garment category e.g. Dress, Gown, Blazer, Trousers, Saree")
    silhouette: str | None = Field(default=None, description="Cut or silhouette e.g. A-line, Tailored, Oversized, Wrap")
    primary_color: str = Field(..., description="Dominant color name")
    secondary_colors: list[str] = Field(default_factory=list, description="Secondary or accent colors")
    fabric: str | None = Field(default=None, description="Material or fabric type e.g. Silk, Linen, Velvet, Cotton")
    pattern: str | None = Field(default=None, description="Pattern e.g. Floral, Striped, Solid, Geometric")
    occasion: str | None = Field(default=None, description="Occasion suitability e.g. Wedding, Cocktail, Black Tie, Casual")
    aesthetic_tags: list[str] = Field(default_factory=list, description="Aesthetic keywords e.g. Minimalist, Quiet Luxury, Editorial")


class FoundItem(BaseModel):
    """A matched inventory item returned by visual search."""

    model_config = ConfigDict(extra="forbid")

    item_id: str | None = Field(default=None, description="Item identifier")
    item_name: str | None = Field(default=None, description="Item display name")
    price: float | None = Field(default=None, ge=0.0, description="Item price (non-negative)")
    match_confidence: float | None = Field(default=None, ge=0.0, le=1.0, description="Match confidence score between 0.0 and 1.0")
    category: str | None = None
    color: str | None = None
    stock: int | None = Field(default=None, ge=0)
    image_url: str | None = None
    description: str | None = None


class PieceItem(BaseModel):
    """A single piece/garment from boutique inventory."""

    model_config = ConfigDict(extra="forbid")

    itemId: str
    name: str
    price: float | None = Field(default=None, ge=0.0)
    size: str | None = None
    stock: int | None = Field(default=None, ge=0)
    imageUrl: str | None = None
    category: str | None = None
    color: str | None = None
    description: str | None = None


class LookDto(BaseModel):
    """An editorial look curated by Elle."""

    model_config = ConfigDict(extra="forbid")

    name: str = Field(..., description="Lookbook title e.g. 'Galle Sunset Soiree'")
    text: str = Field(..., description="Stylist commentary or editorial rationale")
    imageUrl: str | None = None
    items: list[PieceItem] = Field(default_factory=list)


class SourcingRequestDto(BaseModel):
    """A sourcing ticket created when a desired garment is out of stock or custom-ordered."""

    model_config = ConfigDict(extra="forbid")

    requestId: str
    customerId: str | None = None
    imageUrl: str | None = None
    notes: str
    status: Literal["pending", "sourcing", "fulfilled", "cancelled"] = "pending"
    supplierRef: str | None = None


class VisualAgentOutput(BaseModel):
    """Structured output produced by the Visual Insight Agent (Elle) sub-graph.

    This directly feeds ``app/events/block_builders.py::build_elle_blocks`` to render
    ``piece``, ``look``, and ``suggestion`` blocks in The Salon.
    """

    model_config = ConfigDict(extra="forbid")

    status: Literal["success", "no_results", "pending", "out_of_scope", "skipped", "stub", "error"]
    agent: str = "visual"
    ran: bool = True
    suggestion: str | None = None
    found_items: list[FoundItem] = Field(default_factory=list)
    items: list[PieceItem] = Field(default_factory=list)
    looks: list[LookDto] = Field(default_factory=list)
    outfit_proposal: dict[str, Any] | None = None
    sourcing_request: SourcingRequestDto | None = None
    sourcing_suggestion: dict[str, Any] | None = None
    image_attributes: ImageAttributes | None = None
    analyzed_image: dict[str, Any] | None = None
    customer_matches: list[dict[str, Any]] | None = None
    action_required: str | None = None
    reason: str | None = None
    note: str | None = None
    message: str | None = None
    text: str | None = None
    summary: str | None = None


def coerce_visual_output(data: dict[str, Any]) -> VisualAgentOutput:
    """Validate and coerce dictionary payload into VisualAgentOutput.

    Ensures forbidden extra fields fail with ValidationError (ADR-002, ADR-016).
    """
    return VisualAgentOutput.model_validate(data)

