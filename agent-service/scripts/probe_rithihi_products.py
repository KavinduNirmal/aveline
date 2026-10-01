"""Probe rithihi product category pages."""

import asyncio
import re

import httpx


async def check_categories():
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
    }
    categories = [
        "https://rithihi.com/product-category/silk-sarees-sri-lanka/",
        "https://rithihi.com/product-category/silk-sarees-sri-lanka/kanchipuram/",
        "https://rithihi.com/product-category/silk-sarees-sri-lanka/benaras-silk/",
        "https://rithihi.com/product-category/cotton-sarees/jamdani-weave/",
    ]

    async with httpx.AsyncClient(headers=headers, follow_redirects=True, timeout=20.0) as client:
        for cat in categories:
            res = await client.get(cat)
            print(f"Cat: {cat} -> HTTP {res.status_code}")

            # Find all product titles and images
            # WooCommerce usually has class="woocommerce-loop-product__title" or <h2 class="woocommerce-loop-product__title">
            matches = re.findall(r'<li[^>]*class="[^"]*product[^"]*"[^>]*>(.*?)</li>', res.text, re.DOTALL)
            print(f"  Found {len(matches)} WooCommerce product items!")
            for idx, m in enumerate(matches[:5], 1):
                t_match = re.search(r'<h[23][^>]*>(.*?)</h[23]>', m)
                title = re.sub(r'<[^>]+>', '', t_match.group(1)).strip() if t_match else "Unknown Title"
                p_match = re.search(r'<span[^>]*class="[^"]*price[^"]*"[^>]*>(.*?)</span>', m, re.DOTALL)
                price = re.sub(r'<[^>]+>', '', p_match.group(1)).strip() if p_match else "Inquire"
                l_match = re.search(r'<a[^>]*href="([^"]+)"', m)
                link = l_match.group(1) if l_match else ""

                print(f"    [{idx}] {title} | Price: {price} | Link: {link}")


if __name__ == "__main__":
    asyncio.run(check_categories())
