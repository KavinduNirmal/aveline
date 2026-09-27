"""Pydantic schemas for the Dynamic Atelier Web Scraper Tool."""

from pydantic import BaseModel, ConfigDict, Field


class ScrapedAtelierProduct(BaseModel):
    """A product or fabric item scraped from a partner atelier's online catalog."""

    model_config = ConfigDict(extra="forbid")

    title: str = Field(..., description="Product or fabric title")
    price: str = Field(..., description="Formatted price string e.g. '$180/m' or 'LKR 45,000'")
    product_url: str = Field(..., description="Direct URL to the piece on partner website")
    image_url: str | None = Field(default=None, description="Direct URL to product thumbnail image")
    fabric_details: str | None = Field(default=None, description="Fabric composition or material notes")
    in_stock: bool = Field(default=True, description="Availability status on partner website")
    atelier_id: str = Field(..., description="Supplier / Atelier identifier")
    atelier_name: str = Field(..., description="Partner atelier display name")
    visual_match_score: float | None = Field(default=None, ge=0.0, le=1.0, description="Visual match confidence score")
    match_notes: str | None = Field(default=None, description="AI stylist rationale on match suitability")
