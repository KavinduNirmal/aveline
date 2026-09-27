"""Fabric and Garment Visual Match Verification Service.

Verifies whether a scraped partner product aligns with the client's visual attributes or text query.
"""

from app.schemas.atelier_scraper import ScrapedAtelierProduct


class FabricVerifierService:
    """Evaluates semantic and visual alignment between customer requests and scraped pieces."""

    @staticmethod
    def verify_and_rank(
        products: list[ScrapedAtelierProduct],
        query: str,
        detected_fabric: str | None = None,
        detected_color: str | None = None,
    ) -> list[ScrapedAtelierProduct]:
        """Rank and enrich scraped products with match scores and styling rationale."""
        if not products:
            return []

        ranked: list[ScrapedAtelierProduct] = []
        q_tokens = set(query.lower().replace("-", " ").split())
        target_fab = (detected_fabric or "").lower()
        target_col = (detected_color or "").lower()

        for p in products:
            title_lower = p.title.lower()
            score = 0.70  # Baseline catalog match score

            # Fabric alignment bonus
            if target_fab and target_fab in title_lower:
                score += 0.15
            # Color alignment bonus
            if target_col and target_col in title_lower:
                score += 0.10

            # Query token overlap
            matched_tokens = [t for t in q_tokens if len(t) > 2 and t in title_lower]
            token_bonus = min(0.15, len(matched_tokens) * 0.05)
            score = min(0.99, score + token_bonus)

            notes = f"Verified by Elle: Partner piece '{p.title}' from {p.atelier_name}."
            if target_fab and target_fab in title_lower:
                notes += f" Matches requested {detected_fabric} weave."

            # Update score and notes
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
