"""Live test script querying https://shopadithri.com/ for yellow sarees."""

import asyncio
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.services.atelier_scraper import AtelierScraperService
from app.services.fabric_verifier import FabricVerifierService
from app.schemas.atelier_scraper import ScrapedAtelierProduct
import httpx


async def run_live_test():
    base_url = "https://shopadithri.com"
    query = "yellow saree"
    
    print(f"Connecting to live partner website: {base_url} ...")
    print(f"Querying for: '{query}' ...\n")
    
    scraper = AtelierScraperService(allowed_domains=["shopadithri.com"])
    
    # 1. Search via AtelierScraperService
    products = await scraper.search_atelier(
        base_url=base_url,
        atelier_id="atelier-adithri",
        atelier_name="Adithri Couture",
        query=query,
        max_results=6,
    )
    
    # If standard search returned empty, let's inspect the site's search URL directly to show what was found
    if not products:
        print("Initial search endpoint returned 0 items. Probing alternative catalog search paths...")
        search_urls = [
            f"{base_url}/search?q=yellow+saree",
            f"{base_url}/search?type=product&q=yellow+saree",
            f"{base_url}/collections/all?q=yellow+saree",
            f"{base_url}/collections/sarees",
        ]
        
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        }
        
        async with httpx.AsyncClient(headers=headers, follow_redirects=True, timeout=15.0) as client:
            for url in search_urls:
                try:
                    res = await client.get(url)
                    print(f"GET {url} -> Status: {res.status_code}, Content-Length: {len(res.text)}")
                    if res.status_code == 200 and len(res.text) > 1000:
                        parsed = scraper.parse_html_catalog(res.text, base_url)
                        if parsed:
                            print(f"Found {len(parsed)} raw product cards at {url}!")
                            for item in parsed:
                                title = item.get("title", "")
                                products.append(
                                    ScrapedAtelierProduct(
                                        title=title,
                                        price=item.get("price") or "Price on Request",
                                        product_url=item.get("product_url") or base_url,
                                        image_url=item.get("image_url"),
                                        atelier_id="atelier-adithri",
                                        atelier_name="Adithri Couture",
                                    )
                                )
                            break
                except Exception as ex:
                    print(f"Probe {url} error: {ex}")

    # 2. Fabric and Color Verification
    ranked_products = FabricVerifierService.verify_and_rank(
        products,
        query=query,
        detected_fabric="Saree",
        detected_color="Yellow",
    )
    
    print("\n" + "=" * 70)
    print(f"RESULTS FOR: '{query}' at {base_url}")
    print("=" * 70)
    
    if not ranked_products:
        print("No matching yellow sarees could be extracted from the target website.")
        print("Reasons: Site may require JavaScript rendering (SPA), or has anti-bot cloudflare protection, or 0 items match.")
        return

    for idx, p in enumerate(ranked_products, 1):
        print(f"[{idx}] {p.title}")
        print(f"    Atelier: {p.atelier_name}")
        print(f"    Price: {p.price}")
        print(f"    Match Score: {p.visual_match_score * 100:.0f}%" if p.visual_match_score else "    Match Score: N/A")
        print(f"    AI Stylist Notes: {p.match_notes}")
        print(f"    Link: {p.product_url}")
        print(f"    Image: {p.image_url}")
        print("-" * 70)


if __name__ == "__main__":
    asyncio.run(run_live_test())
