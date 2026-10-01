"""Fabric and Garment Visual Match Verification Service.

Verifies whether a scraped partner product aligns with the client's visual attributes or text query.
"""

import re

from app.schemas.atelier_scraper import ScrapedAtelierProduct

_COLOR_FAMILIES: dict[str, set[str]] = {
    "red": {"red", "crimson", "deep crimson", "maroon", "deep maroon", "ruby", "ruby red", "burgundy", "royal burgundy", "scarlet", "wine", "cherry", "vermilion", "rust"},
    "blue": {"blue", "navy", "midnight navy", "sapphire", "midnight sapphire", "cobalt", "royal blue", "indigo", "sky blue", "powder blue", "baby blue", "teal", "peacock teal", "aqua", "turquoise", "peacock"},
    "green": {"green", "emerald", "emerald green", "sage", "sage green", "mint", "mint green", "olive", "deep olive", "jade", "forest green", "bottle green", "bottle", "forest", "dark green"},
    "pink": {"pink", "blush", "blush pink", "rose", "dusty rose", "rose pink", "magenta", "fuchsia", "coral", "peach", "apricot", "salmon"},
    "yellow": {"yellow", "gold", "champagne gold", "antique gold", "rose gold", "zari gold", "mustard", "mustard ochre", "ochre", "buttercup", "butter yellow"},
    "purple": {"purple", "violet", "lavender", "lilac", "amethyst", "deep amethyst", "plum"},
    "white": {"white", "ivory", "heirloom ivory", "off-white", "cream", "pearl"},
    "black": {"black", "midnight black", "charcoal", "slate grey", "grey", "gray"},
    "brown": {"brown", "terracotta", "burnt terracotta", "rust", "camel", "taupe", "sand", "khaki", "espresso"},
}


class FabricVerifierService:
    """Evaluates semantic and visual alignment between customer requests and scraped pieces."""

    @staticmethod
    def verify_and_rank(
        products: list[ScrapedAtelierProduct],
        query: str,
        detected_fabric: str | None = None,
        detected_color: str | None = None,
        detected_category: str | None = None,
    ) -> list[ScrapedAtelierProduct]:
        """Rank and enrich scraped products with match scores and styling rationale."""
        if not products:
            return []

        ranked: list[ScrapedAtelierProduct] = []
        q_tokens = set(query.lower().replace("-", " ").split())
        target_fab = (detected_fabric or "").lower().strip()
        target_col = (detected_color or "").lower().strip()
        target_cat = (detected_category or "").lower().strip()

        # Determine allowed and conflicting color shades
        allowed_shades: set[str] = set()
        conflicting_shades: set[str] = set()
        if target_col:
            for fam_key, shades in _COLOR_FAMILIES.items():
                if target_col == fam_key or target_col in shades:
                    allowed_shades.update(shades)
                else:
                    conflicting_shades.update(shades)

        for p in products:
            title_lower = p.title.lower()
            score = 0.70  # Baseline catalog match score

            # 1. Strict Color Verification
            if target_col:
                has_matching_color = any(
                    re.search(r"\b" + re.escape(shade) + r"\b", title_lower)
                    for shade in allowed_shades
                )
                has_conflicting_color = any(
                    re.search(r"\b" + re.escape(shade) + r"\b", title_lower)
                    for shade in conflicting_shades
                    if len(shade) > 3
                )

                if has_matching_color:
                    score += 0.20
                elif has_conflicting_color:
                    # Active color mismatch (e.g. asked for red, title is "Blue Soft Organza Saree")
                    score -= 0.35

            # 2. Fabric alignment bonus
            if target_fab and target_fab in title_lower:
                score += 0.15

            # 3. Category alignment bonus / check
            if target_cat:
                cat_singular = target_cat.rstrip("s")
                if cat_singular in title_lower or target_cat in title_lower:
                    score += 0.10
                elif any(incompatible in title_lower for incompatible in ("earring", "necklace", "bangle", "footwear", "clutch", "bag")):
                    score -= 0.30

            # 4. Query token overlap
            matched_tokens = [t for t in q_tokens if len(t) > 2 and t in title_lower]
            token_bonus = min(0.15, len(matched_tokens) * 0.05)
            score = min(0.99, score + token_bonus)

            # 5. Prune items with active conflicts (e.g. conflicting color or incompatible category)
            if score < 0.60:
                continue

            notes = f"Verified by Elle: Partner piece '{p.title}' from {p.atelier_name}."
            if target_col and any(re.search(r"\b" + re.escape(shade) + r"\b", title_lower) for shade in allowed_shades):
                notes += f" Matches requested {detected_color} color palette."
            if target_fab and target_fab in title_lower:
                notes += f" Matches requested {detected_fabric} weave."

            enriched = ScrapedAtelierProduct(
                title=p.title,
                price=p.price,
                product_url=p.product_url,
                image_url=p.image_url,
                fabric_details=p.fabric_details or detected_fabric,
                in_stock=p.in_stock,
                atelier_id=p.atelier_id,
                atelier_name=p.atelier_name,
                visual_match_score=round(score, 2),
                match_notes=notes,
            )
            ranked.append(enriched)

        # Sort descending by match score
        ranked.sort(key=lambda x: x.visual_match_score or 0.0, reverse=True)
        return ranked
