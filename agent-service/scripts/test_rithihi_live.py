"""Live scraper and Visual Agent test for Rithihi (rithihi.com) querying white sarees."""

import asyncio
import os
import sys
from urllib.parse import urljoin

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.services.atelier_scraper import AtelierScraperService
from app.services.fabric_verifier import FabricVerifierService
from app.schemas.atelier_scraper import ScrapedAtelierProduct
import httpx


async def test_rithihi_white_sarees():
    base_url = "https://rithihi.com"
    query = "white saree"
    
    print("===========================================================================")
    print(f"CONNECTING TO PARTNER ATELIER: Rithihi ({base_url})")
    print(f"CUSTOMER QUERY: '{query}'")
    print("===========================================================================\n")

    scraper = AtelierScraperService(allowed_domains=["rithihi.com", "www.rithihi.com"])

    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    }

    # Probing Rithihi search and category endpoints
    search_paths = [
        f"{base_url}/?s=white+saree",
        f"{base_url}/?s=white",
        f"{base_url}/search?q=white+saree",
        f"{base_url}/product-category/sarees/",
        f"{base_url}/shop/",
    ]

    all_scraped: list[ScrapedAtelierProduct] = []
    seen_titles: set[str] = set()

    async with httpx.AsyncClient(headers=headers, follow_redirects=True, timeout=20.0) as client:
        for path in search_paths:
            try:
                print(f"Probing {path} ...")
                res = await client.get(path)
                print(f"  -> HTTP {res.status_code} (Length: {len(res.text)} bytes)")
                
                if res.status_code == 200:
                    raw_items = scraper.parse_html_catalog(res.text, base_url)
                    print(f"  -> Extracted {len(raw_items)} product cards from page")
                    
                    for item in raw_items:
                        title = item.get("title", "").strip()
                        if not title or title.lower() in seen_titles:
                            continue
                        seen_titles.add(title.lower())
                        
                        all_scraped.append(
                            ScrapedAtelierProduct(
                                title=title,
                                price=item.get("price") or "Price on Request",
                                product_url=item.get("product_url") or base_url,
                                image_url=item.get("image_url"),
                                atelier_id="sup-rithihi",
                                atelier_name="Rithihi Couture & Handloom",
                            )
                        )
                    if all_scraped:
                        # Found items on search
                        break
            except Exception as ex:
                print(f"  -> Error connecting to {path}: {ex}")

    # Fabric Verifier Ranking
    ranked = FabricVerifierService.verify_and_rank(
        all_scraped,
        query=query,
        detected_fabric="Saree",
        detected_color="White",
    )

    print("\n" + "=" * 75)
    print("ELLE (VISUAL AGENT) - PARTNER DISCOVERY RESULTS")
    print("=" * 75)
    print(f"Total Unique Pieces Extracted: {len(ranked)}\n")

    for idx, p in enumerate(ranked[:6], 1):
        print(f"[{idx}] {p.title}")
        print(f"    • Atelier: {p.atelier_name}")
        print(f"    • Price: {p.price}")
        print(f"    • Match Score: {p.visual_match_score * 100:.0f}%" if p.visual_match_score else "    • Match Score: N/A")
        print(f"    • Stylist Notes: {p.match_notes}")
        print(f"    • Link: {p.product_url}")
        print(f"    • Thumbnail: {p.image_url}")
        print("-" * 75)

    print("\n" + "=" * 75)
    print("ELLE'S CONVERSATIONAL RESPONSE TO THE CUSTOMER:")
    print("=" * 75)
    if ranked:
        top_pieces = ranked[:3]
        pieces_bullet = "\n".join(
            [f"{i+1}. **{item.title}** ({item.price}) — [View on Rithihi]({item.product_url})" for i, item in enumerate(top_pieces)]
        )
        msg = (
            f"We do not have this white saree in stock in our boutique showroom, but Elle searched our registered partner "
            f"**Rithihi** and found matching pieces available for sourcing:\n\n"
            f"{pieces_bullet}\n\n"
            f"Estimated delivery turnaround is **5 days** (Min Order: LKR 25,000). Would you like me to request a private fitting hold?"
        )
        print(msg)
    else:
        print("No white sarees found on Rithihi search. Sourcing request ticket logged with partner.")
    print("=" * 75)


if __name__ == "__main__":
    asyncio.run(test_rithihi_white_sarees())
