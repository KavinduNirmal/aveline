"""Image analysis tools for Visual Insight Agent (Elle).

Extracts fashion attributes (category, silhouette, colors, fabric, pattern, occasion)
from garment photographs or inspiration mood boards.
"""

from typing import Any

from app.schemas.visual_insight import ImageAttributes


def parse_image_attributes_dict(data: dict[str, Any]) -> ImageAttributes:
    """Parse raw image analysis dictionary into validated ImageAttributes."""
    category = data.get("category") or "Garment"
    primary_color = data.get("primary_color") or data.get("color") or "Neutral"
    secondary_colors = data.get("secondary_colors") or []
    if isinstance(secondary_colors, str):
        secondary_colors = [s.strip() for s in secondary_colors.split(",") if s.strip()]

    aesthetic_tags = data.get("aesthetic_tags") or data.get("tags") or []
    if isinstance(aesthetic_tags, str):
        aesthetic_tags = [t.strip() for t in aesthetic_tags.split(",") if t.strip()]

    return ImageAttributes(
        category=str(category),
        silhouette=data.get("silhouette"),
        primary_color=str(primary_color),
        secondary_colors=secondary_colors,
        fabric=data.get("fabric") or data.get("material"),
        pattern=data.get("pattern"),
        occasion=data.get("occasion"),
        aesthetic_tags=aesthetic_tags,
    )


async def analyze_product_image(registry: Any, image_url: str) -> ImageAttributes:
    """Analyze a product image via the tool registry / vision model."""
    try:
        res = await registry.analyze_product_image(image_url=image_url)
        data = res.get("attributes") or res
        return parse_image_attributes_dict(data)
    except Exception:
        # Fallback heuristic if endpoint is unavailable
        return ImageAttributes(
            category="Curated Piece",
            primary_color="Bespoke",
            aesthetic_tags=["Quiet Luxury", "Boutique"],
        )
