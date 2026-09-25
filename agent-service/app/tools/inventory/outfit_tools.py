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

    return LookDto(
        name=f"Look: {occasion_label}",
        text=commentary,
        # Deliberately no `imageUrl`. A look is a pairing and a rationale, not a photograph: the
        # boutique holds one picture per piece, so there is no picture *of the look*. Handing it the
        # first matched piece's photo made the Salon render that photograph twice — once on the
        # piece, once on the look — which read as a duplicated result. A look with no image renders
        # as its styling note, which is what it has to say.
        imageUrl=None,
        items=items,
    )
