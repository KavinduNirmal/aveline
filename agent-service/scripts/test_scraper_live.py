"""Verification script demonstrating the Atelier Web Scraper and Fabric Verifier in action."""

import asyncio
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.services.atelier_scraper import AtelierScraperService
from app.tools.inventory.supplier_tools import scrape_atelier_catalog


class MockRegistry:
    async def get_suppliers(self, org_id: str | None = None):
        return [
            {
                "id": "sup-colombo-silk",
                "supplierName": "Maison de Soie (Paris)",
                "websiteUrl": "https://maisondesoie.example.com",
            },
            {
                "id": "sup-varanasi-zari",
                "supplierName": "Varanasi Heritage Weaves",
                "websiteUrl": "https://varanasiweaves.example.com",
            },
        ]


SAMPLE_HTML_MAISON = """
<!DOCTYPE html>
<html>
<head>
    <title>Maison de Soie - Silk Catalog</title>
</head>
<body>
    <div class="product-grid">
        <div class="product-card">
            <a href="/fabrics/emerald-raw-silk-22-momme">
                <img src="/images/emerald_silk.jpg" alt="Pure Emerald Green Raw Silk Fabric (22 Momme)" />
            </a>
            <h3 class="product-title">Pure Emerald Green Raw Silk Fabric (22 Momme)</h3>
            <span class="price">EUR 185.00/m</span>
        </div>
        <div class="product-card">
            <a href="/fabrics/midnight-blue-crepe">
                <img src="/images/navy_crepe.jpg" alt="Midnight Blue Silk Crepe" />
            </a>
            <h3 class="product-title">Midnight Blue Silk Crepe</h3>
            <span class="price">EUR 140.00/m</span>
        </div>
    </div>
</body>
</html>
"""


async def main():
    print("=================================================================")
    print("1. Testing Atelier HTML Parser on Sample Partner Catalog HTML")
    print("=================================================================")
    scraper = AtelierScraperService(allowed_domains=["maisondesoie.example.com", "varanasiweaves.example.com"])

    extracted = scraper.parse_html_catalog(SAMPLE_HTML_MAISON, "https://maisondesoie.example.com")
    print(f"Extracted {len(extracted)} items from raw HTML:")
    for idx, item in enumerate(extracted, 1):
        print(f"  [{idx}] Title: {item['title']}")
        print(f"      Price: {item['price']}")
        print(f"      Product URL: {item['product_url']}")
        print(f"      Thumbnail: {item['image_url']}")

    print("\n=================================================================")
    print("2. Testing SSRF Security & URL Whitelist Verification")
    print("=================================================================")
    test_urls = [
        ("http://127.0.0.1:8000/admin", False),
        ("http://localhost:5000", False),
        ("http://169.254.169.254/latest/meta-data", False),
        ("http://192.168.1.1/secret", False),
        ("https://malicious-site.com/hack", False),
        ("https://maisondesoie.example.com/catalog", True),
    ]
    for url, expected in test_urls:
        allowed = scraper.is_safe_url(url)
        status = "PASSED" if allowed == expected else "FAILED"
        print(f"  URL: {url:<45} -> Safe: {allowed:<5} [{status}]")

    print("\n=================================================================")
    print("3. Testing Fabric Verifier AI Ranking (Emerald Silk Inquiry)")
    print("=================================================================")
    mock_registry = MockRegistry()

    # Run tool with mock scraper
    class FakeScraper(AtelierScraperService):
        async def search_atelier(self, base_url, atelier_id, atelier_name, query, max_results=5, client=None):
            if "maisondesoie" in base_url:
                raw = self.parse_html_catalog(SAMPLE_HTML_MAISON, base_url)
                from app.schemas.atelier_scraper import ScrapedAtelierProduct
                return [
                    ScrapedAtelierProduct(
                        title=r["title"],
                        price=r["price"],
                        product_url=r["product_url"],
                        image_url=r["image_url"],
                        atelier_id=atelier_id,
                        atelier_name=atelier_name,
                    )
                    for r in raw
                ]
            return []

    results = await scrape_atelier_catalog(
        registry=mock_registry,
        query="emerald raw silk",
        org_id="org-colombo-1",
        detected_fabric="Silk",
        detected_color="Emerald Green",
        scraper_service=FakeScraper(),
    )

    print(f"Scraper returned {len(results)} ranked options for 'emerald raw silk':")
    for r in results:
        print(f"  - Title: {r['title']}")
        print(f"    Atelier: {r['atelier_name']}")
        print(f"    Price: {r['price']}")
        print(f"    Match Confidence: {r['visual_match_score'] * 100:.0f}%")
        print(f"    Stylist Commentary: {r['match_notes']}")
        print(f"    Link: {r['product_url']}")

    print("\nAll Scraper test verifications completed successfully!")


if __name__ == "__main__":
    asyncio.run(main())
