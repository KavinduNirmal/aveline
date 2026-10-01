"""External supplier catalog and web scraping tools for Visual Insight Agent (Elle)."""

import logging
from typing import Any

from app.schemas.atelier_scraper import ScrapedAtelierProduct
from app.services.atelier_scraper import AtelierScraperService
from app.services.fabric_verifier import FabricVerifierService

logger = logging.getLogger(__name__)

DEFAULT_PARTNER_ATELIERS: list[dict[str, Any]] = [
    {
        "id": "sup-rithihi",
        "supplierId": "sup-rithihi",
        "name": "Rithihi Atelier",
        "supplierName": "Rithihi Atelier",
        "websiteUrl": "https://rithihi.com",
    },
    {
        "id": "sup-adithri",
        "supplierId": "sup-adithri",
        "name": "Adithri Couture",
        "supplierName": "Adithri Couture",
        "websiteUrl": "https://shopadithri.com",
    },
]


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
    detected_category: str | None = None,
    scraper_service: AtelierScraperService | None = None,
    max_results_per_atelier: int = 4,
) -> list[dict[str, Any]]:
    """Dynamically scrape partner atelier websites for requested fabric or garment pieces."""
    try:
        # 1. Fetch registered suppliers
        suppliers: list[dict[str, Any]] = []
        if hasattr(registry, "get_suppliers"):
            try:
                res = await registry.get_suppliers(org_id=org_id)
                suppliers = res if isinstance(res, list) else (res.get("items", []) if isinstance(res, dict) else [])
            except Exception as e:
                logger.debug("get_suppliers failed, falling back to default partners: %s", e)

        if not suppliers:
            suppliers = DEFAULT_PARTNER_ATELIERS

        # 2. Extract allowed domains and initialize scraper service if needed
        allowed_domains = []
        for s in suppliers:
            url = s.get("websiteUrl") or s.get("apiEndpoint")
            if url:
                allowed_domains.append(url)

        scraper = scraper_service or AtelierScraperService(allowed_domains=allowed_domains)

        # Build clean search queries: prefer singularized query (e.g. "red saree" vs "red sarees")
        clean_q = query.strip()
        search_queries = [clean_q]
        if clean_q.endswith("s") and not clean_q.endswith("ss"):
            search_queries.append(clean_q[:-1])

        all_scraped: list[ScrapedAtelierProduct] = []
        seen_urls: set[str] = set()

        for s in suppliers:
            base_url = s.get("websiteUrl") or s.get("apiEndpoint")
            if not base_url or not base_url.startswith("http"):
                continue

            sup_id = str(s.get("id") or s.get("supplierId") or "sup-partner")
            sup_name = str(s.get("supplierName") or s.get("name") or "Partner Atelier")

            for sq in search_queries:
                items = await scraper.search_atelier(
                    base_url=base_url,
                    atelier_id=sup_id,
                    atelier_name=sup_name,
                    query=sq,
                    max_results=max_results_per_atelier,
                )
                for item in items:
                    if item.product_url not in seen_urls:
                        seen_urls.add(item.product_url)
                        all_scraped.append(item)
                if len(all_scraped) >= max_results_per_atelier * 2:
                    break

        # 3. Verify and rank matches with FabricVerifierService (strict color & category validation)
        ranked = FabricVerifierService.verify_and_rank(
            all_scraped,
            query=query,
            detected_fabric=detected_fabric,
            detected_color=detected_color,
            detected_category=detected_category,
        )

        return [item.model_dump() for item in ranked]
    except Exception as ex:
        logger.warning("scrape_atelier_catalog encountered exception: %s", ex)
        return []
