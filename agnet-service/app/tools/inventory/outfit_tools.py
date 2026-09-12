"""Outfit composition and curation tools for Visual Insight Agent (Elle).

Assembles individual pieces into harmonious editorial looks.
"""

from typing import Any

from app.schemas.visual_insight import LookDto, PieceItem


def compose_outfit(
    items: list[PieceItem],
    occasion: str | None = None,
    customer_preferences: dict[str, Any] | None = None,
) -> LookDto:
    """Combine catalog pieces into an editorial lookbook entry."""
    occasion_label = occasion or "Bespoke Evening"
    if not items:
        return LookDto(
            name=f"Curated {occasion_label}",
            text=f"A personalized style palette curated for {occasion_label}.",
            items=[],
        )

    item_names = ", ".join(item.name for item in items[:3])
    commentary = (
        f"Harmonized for {occasion_label}: featuring {item_names} "
        "crafted in signature textures and silhouette."
    )

    first_image = next((item.imageUrl for item in items if item.imageUrl), None)

    return LookDto(
        name=f"Look: {occasion_label}",
        text=commentary,
        imageUrl=first_image,
        items=items,
    )
