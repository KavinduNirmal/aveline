"""External supplier catalog and web scraping tools for Visual Insight Agent (Elle)."""

import logging
from typing import Any

from app.schemas.atelier_scraper import ScrapedAtelierProduct
from app.services.atelier_scraper import AtelierScraperService
from app.services.fabric_verifier import FabricVerifierService

logger = logging.getLogger(__name__)


async def search_supplier_catalog(
    registry: Any,
    query: str,
    org_id: str | None = None,
) -> list[dict[str, Any]]:
    """Query partner ateliers and supplier catalogs for matching fabrics or garments."""
    try:
        if hasattr(registry, "search_supplier_catalog"):
            res = await registry.search_supplier_catalog(query=query, org_id=org_id)
            return res.get("items") or (res if isinstance(res, list) else [])
        return []
    except Exception:
        return []


async def scrape_atelier_catalog(
    registry: Any,
    query: str,
    org_id: str | None = None,
    detected_fabric: str | None = None,
    detected_color: str | None = None,
    scraper_service: AtelierScraperService | None = None,
    max_results_per_atelier: int = 3,
) -> list[dict[str, Any]]:
    """Dynamically scrape partner atelier websites for requested fabric or garment pieces."""
    try:
        # 1. Fetch registered suppliers
        suppliers: list[dict[str, Any]] = []
        if hasattr(registry, "get_suppliers"):
            res = await registry.get_suppliers(org_id=org_id)
            suppliers = res if isinstance(res, list) else (res.get("items", []) if isinstance(res, dict) else [])

        if not suppliers:
            return []

        # 2. Extract allowed domains and initialize scraper service if needed
        allowed_domains = []
        for s in suppliers:
            url = s.get("websiteUrl") or s.get("apiEndpoint")
            if url:
                allowed_domains.append(url)

        scraper = scraper_service or AtelierScraperService(allowed_domains=allowed_domains)

        all_scraped: list[ScrapedAtelierProduct] = []
        for s in suppliers:
            base_url = s.get("websiteUrl") or s.get("apiEndpoint")
            if not base_url or not base_url.startswith("http"):
                continue

            sup_id = str(s.get("id") or s.get("supplierId") or "sup-partner")
            sup_name = str(s.get("supplierName") or s.get("name") or "Partner Atelier")

            items = await scraper.search_atelier(
                base_url=base_url,
                atelier_id=sup_id,
                atelier_name=sup_name,
                query=query,
                max_results=max_results_per_atelier,
            )
            all_scraped.extend(items)

        # 3. Verify and rank matches with FabricVerifierService
        ranked = FabricVerifierService.verify_and_rank(
            all_scraped,
            query=query,
            detected_fabric=detected_fabric,
            detected_color=detected_color,
        )

        return [item.model_dump() for item in ranked]
    except Exception as ex:
        logger.warning("scrape_atelier_catalog encountered exception: %s", ex)
        return []
