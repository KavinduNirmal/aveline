"""Probe script to inspect shopadithri.com structure and search capability."""

import asyncio
from html.parser import HTMLParser

import httpx


class LinkAndClassParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.classes = set()
        self.links = []
        self.images = []

    def handle_starttag(self, tag, attrs):
        attr_dict = {k.lower(): v for k, v in attrs}
        if "class" in attr_dict and attr_dict["class"]:
            for c in attr_dict["class"].split():
                self.classes.add(c)
        if tag == "a" and "href" in attr_dict:
            self.links.append(attr_dict["href"])
        if tag == "img" and "src" in attr_dict:
            self.images.append((attr_dict.get("src"), attr_dict.get("alt", "")))


async def probe():
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
    }
    async with httpx.AsyncClient(headers=headers, follow_redirects=True, timeout=20.0) as client:
        # Check homepage
        res = await client.get("https://shopadithri.com/")
        print(f"Homepage status: {res.status_code}")

        parser = LinkAndClassParser()
        parser.feed(res.text)

        saree_links = [link_url for link_url in parser.links if "saree" in link_url.lower() or "product" in link_url.lower() or "shop" in link_url.lower() or "collection" in link_url.lower()]
        print(f"Discovered {len(saree_links)} relevant links on homepage:")
        for link_url in list(dict.fromkeys(saree_links))[:15]:
            print("  ", link_url)

        # Let's search for "yellow" or "saree"
        for search_path in ["/search?q=yellow", "/?s=yellow", "/shop/?filter_color=yellow", "/collections/all?q=yellow"]:
            try:
                sres = await client.get(f"https://shopadithri.com{search_path}")
                print(f"Search {search_path} -> Status: {sres.status_code}, Length: {len(sres.text)}")
            except Exception as e:
                print(f"Search {search_path} error: {e}")

        # Check images with yellow or saree alt
        yellow_images = [(src, alt) for src, alt in parser.images if "yellow" in alt.lower() or "saree" in alt.lower()]
        print(f"\nImages with saree / yellow metadata on homepage: {len(yellow_images)}")
        for src, alt in yellow_images[:10]:
            print(f"  Alt: '{alt}' -> Src: {src}")


if __name__ == "__main__":
    asyncio.run(probe())
