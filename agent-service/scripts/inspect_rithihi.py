import asyncio
import httpx
import re
from html.parser import HTMLParser


class RithihiCatalogInspector(HTMLParser):
    def __init__(self):
        super().__init__()
        self.articles = []
        self.current_tag = None
        self.current_attrs = {}
        self.in_item = False
        self.item_depth = 0
        self.item_data = {}
        self.all_links = []
        self.all_images = []

    def handle_starttag(self, tag, attrs):
        attr_dict = {k.lower(): v for k, v in attrs}
        if tag == "a" and "href" in attr_dict:
            self.all_links.append((attr_dict["href"], attr_dict.get("title", "")))
        if tag == "img":
            self.all_images.append((attr_dict.get("src", ""), attr_dict.get("alt", "")))
            
        classes = attr_dict.get("class", "").lower()
        if any(marker in classes for marker in ["product", "item", "post", "entry", "card"]) and not self.in_item:
            self.in_item = True
            self.item_depth = 1
            self.item_data = {"tag": tag, "classes": classes, "texts": [], "link": None, "img": None}
        elif self.in_item:
            self.item_depth += 1
            if tag == "a" and "href" in attr_dict and not self.item_data["link"]:
                self.item_data["link"] = attr_dict["href"]
            if tag == "img" and not self.item_data["img"]:
                self.item_data["img"] = attr_dict.get("src") or attr_dict.get("data-src")

    def handle_endtag(self, tag):
        if self.in_item:
            self.item_depth -= 1
            if self.item_depth <= 0:
                self.in_item = False
                if self.item_data.get("texts") or self.item_data.get("link"):
                    self.articles.append(self.item_data)
                self.item_data = {}

    def handle_data(self, data):
        text = data.strip()
        if text and self.in_item:
            self.item_data["texts"].append(text)


async def inspect_rithihi():
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
    }
    async with httpx.AsyncClient(headers=headers, follow_redirects=True, timeout=20.0) as client:
        # Check /?s=white
        res = await client.get("https://rithihi.com/?s=white")
        print(f"GET https://rithihi.com/?s=white -> HTTP {res.status_code}")
        
        parser = RithihiCatalogInspector()
        parser.feed(res.text)
        
        print(f"Total articles/items parsed: {len(parser.articles)}")
        for idx, art in enumerate(parser.articles[:10], 1):
            text_preview = " | ".join(art["texts"][:3])
            print(f"  [{idx}] Class: {art['classes'][:40]} | Link: {art['link']} | Text: {text_preview[:60]}")

        # Check saree links
        saree_links = [l for l, t in parser.all_links if "saree" in l.lower() or "collection" in l.lower() or "product" in l.lower()]
        print(f"\nDiscovered {len(saree_links)} saree links on page:")
        for l in list(dict.fromkeys(saree_links))[:10]:
            print(f"  -> {l}")


if __name__ == "__main__":
    asyncio.run(inspect_rithihi())
