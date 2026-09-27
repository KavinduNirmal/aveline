"""Unit tests for scrape_atelier_catalog and FabricVerifierService."""

from unittest.mock import AsyncMock, MagicMock

import pytest

from app.schemas.atelier_scraper import ScrapedAtelierProduct
from app.services.fabric_verifier import FabricVerifierService
from app.tools.inventory.supplier_tools import scrape_atelier_catalog


def test_fabric_verifier_ranks_relevant_pieces_higher():
    products = [
        ScrapedAtelierProduct(
            title="Standard Cotton Kurti",
            price="$50",
            product_url="https://atelier.com/1",
            atelier_id="1",
            atelier_name="Atelier A",
        ),
        ScrapedAtelierProduct(
            title="Emerald Green Mulberry Silk Saree",
            price="$250",
            product_url="https://atelier.com/2",
            atelier_id="2",
            atelier_name="Atelier B",
        ),
    ]

    ranked = FabricVerifierService.verify_and_rank(
        products,
        query="emerald silk saree",
        detected_fabric="Silk",
        detected_color="Emerald Green",
    )

    assert len(ranked) == 2
    assert ranked[0].title == "Emerald Green Mulberry Silk Saree"
    assert ranked[0].visual_match_score > ranked[1].visual_match_score
    assert "Silk weave" in (ranked[0].match_notes or "")


@pytest.mark.asyncio
async def test_scrape_atelier_catalog_with_mock_registry():
    registry = MagicMock()
    registry.get_suppliers = AsyncMock(
        return_value=[
            {
                "id": "sup-101",
                "supplierName": "Maison de Soie",
                "websiteUrl": "https://maisondesoie.example.com",
            }
        ]
    )

    mock_scraper = MagicMock()
    mock_scraper.search_atelier = AsyncMock(
        return_value=[
            ScrapedAtelierProduct(
                title="Pure Mulberry Silk Fabric (22 Momme)",
                price="$180/m",
                product_url="https://maisondesoie.example.com/item1",
                image_url="https://maisondesoie.example.com/img1.jpg",
                atelier_id="sup-101",
                atelier_name="Maison de Soie",
            )
        ]
    )

    results = await scrape_atelier_catalog(
        registry=registry,
        query="mulberry silk",
        org_id="org-1",
        detected_fabric="Silk",
        scraper_service=mock_scraper,
    )

    assert len(results) == 1
    assert results[0]["title"] == "Pure Mulberry Silk Fabric (22 Momme)"
    assert results[0]["atelier_name"] == "Maison de Soie"
    assert results[0]["price"] == "$180/m"
    assert results[0]["visual_match_score"] is not None
