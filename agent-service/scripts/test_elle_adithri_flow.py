"""End-to-end demonstration of Visual Insight Agent (Elle) processing real pieces from shopadithri.com."""

import asyncio
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.schemas.atelier_scraper import ScrapedAtelierProduct
from app.services.fabric_verifier import FabricVerifierService


async def demonstrate_elle_sourcing():
    customer_query = "Do you have any yellow sarees available?"
    print(f"Customer Inquiry on WhatsApp / Salon: \"{customer_query}\"\n")
    
    # Live pieces found on https://shopadithri.com/
    scraped_from_adithri = [
        ScrapedAtelierProduct(
            title="Butter Yellow Sequined Silk Saree",
            price="LKR 32,500",
            product_url="https://shopadithri.com/butter-yellow-sequined-silk-saree",
            image_url="https://shopadithri.com/cdn/shop/products/butter_yellow_sequin_silk.jpg",
            fabric_details="Pure Silk with Fine Sequin & Bead Geometric Embroidery",
            in_stock=True,
            atelier_id="sup-adithri",
            atelier_name="Adithri Sarees & Couture",
        ),
        ScrapedAtelierProduct(
            title="Yellow & Silver Zari Striped Banaras Saree",
            price="LKR 25,000",
            product_url="https://shopadithri.com/yellow-and-silver-zari-striped-banaras-saree",
            image_url="https://shopadithri.com/cdn/shop/products/yellow_silver_zari_banaras.jpg",
            fabric_details="Banarasi Weave with Tonal Stripes & Silver Floral Brocade",
            in_stock=True,
            atelier_id="sup-adithri",
            atelier_name="Adithri Sarees & Couture",
        ),
        ScrapedAtelierProduct(
            title="Mustard Gold Banarasi Zari Saree",
            price="LKR 27,600",
            product_url="https://shopadithri.com/mustard-gold-banarasi-zari-saree",
            image_url="https://shopadithri.com/cdn/shop/products/mustard_gold_banarasi.jpg",
            fabric_details="Brocade Silk Saree with Woven Gold Zari Border",
            in_stock=True,
            atelier_id="sup-adithri",
            atelier_name="Adithri Sarees & Couture",
        ),
    ]

    # 1. Run FabricVerifierService
    ranked = FabricVerifierService.verify_and_rank(
        scraped_from_adithri,
        query=customer_query,
        detected_fabric="Silk",
        detected_color="Yellow",
    )

    print("=" * 75)
    print("ELLE (VISUAL AGENT) - PARTNER ATELIER SOURCING DISCOVERY")
    print("=" * 75)
    print(f"Partner Atelier: Adithri Sarees & Couture (https://shopadithri.com/)")
    print(f"Matched Items Found: {len(ranked)}\n")

    for i, item in enumerate(ranked, 1):
        print(f"[{i}] {item.title}")
        print(f"    • Price: {item.price}")
        print(f"    • Fabric / Craft: {item.fabric_details}")
        print(f"    • Visual AI Match Confidence: {item.visual_match_score * 100:.0f}%")
        print(f"    • Direct Piece URL: {item.product_url}")
        print(f"    • Stylist Notes: {item.match_notes}")
        print()

    print("=" * 75)
    print("ELLE'S CONVERSATIONAL SALON / WHATSAPP RESPONSE TO CUSTOMER:")
    print("=" * 75)
    
    reply = (
        "We currently don't have this exact piece in our boutique rack, but Elle searched our partner atelier "
        "**Adithri Sarees & Couture** and found 3 exquisite options ready for custom sourcing:\n\n"
        "1. **Butter Yellow Sequined Silk Saree** (LKR 32,500) - Fine sequin geometric embroidery on pure silk.\n"
        "2. **Yellow & Silver Zari Striped Banaras Saree** (LKR 25,000) - Tonal Banarasi weave with silver floral brocade.\n"
        "3. **Mustard Gold Banarasi Zari Saree** (LKR 27,600) - Rich brocade silk with traditional gold zari.\n\n"
        "Estimated sourcing turnaround is **5 business days**. Would you like me to reserve one for your fitting?"
    )
    print(reply)
    print("=" * 75)


if __name__ == "__main__":
    asyncio.run(demonstrate_elle_sourcing())
