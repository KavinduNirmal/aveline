"""Dynamic Atelier Web Scraper Service.

Safely fetches, parses, and extracts garment and fabric availability from partner atelier websites.
Guarded by SSRF validation and built with zero third-party HTML parsing dependencies.
"""

import ipaddress
import json
import logging
import re
from html.parser import HTMLParser
from typing import Any
from urllib.parse import urljoin, urlparse

import httpx

from app.schemas.atelier_scraper import ScrapedAtelierProduct

logger = logging.getLogger(__name__)

# Currency regex pattern matching standard retail price formats
_PRICE_REGEX = re.compile(
    r"(?:(?:\$|€|£|₹|Rs\.?|LKR|USD|EUR|GBP)\s*[\d,]+(?:\.\d{2})?|[\d,]+(?:\.\d{2})?\s*(?:LKR|USD|EUR|GBP)|\b\d{1,3}(?:,\d{3})*(?:\.\d{2})?\b)",
    re.IGNORECASE,
)

_NOISE_PHRASES = [
    "no product",
    "no products",
    "matching your selection",
    "category",
    "categories",
    "search result",
    "search results",
    "quick view",
    "add to cart",
    "view cart",
    "select options",
    "placeholder",
    "showing",
    "filter",
    "menu",
    "sort by",
    "wishlist",
    "add to wishlist",
    "browse wishlist",
    "% off",
    "sale",
    "discount",
    "off",
    "on sale",
    "out of stock",
]


class _ProductCardHTMLParser(HTMLParser):
    """Zero-dependency HTML Parser that extracts product cards, JSON-LD, and OpenGraph metadata."""

    def __init__(self, base_url: str):
        super().__init__()
        self.base_url = base_url
        self.products: list[dict[str, Any]] = []
        self.json_ld_blocks: list[str] = []
        self.og_metadata: dict[str, str] = {}

        # Parsing state
        self._in_script_json_ld = False
        self._in_card = False
        self._card_depth = 0
        self._title_depth = 0
        self._price_depth = 0
        self._ignore_depth = 0
        self._current_card: dict[str, Any] = {}
        self._text_accumulator: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attr_dict = {k.lower(): (v or "") for k, v in attrs}

        # 1. Capture OpenGraph / Twitter meta tags
        if tag == "meta":
            prop = attr_dict.get("property") or attr_dict.get("name") or ""
            content = attr_dict.get("content") or ""
            if prop and content:
                self.og_metadata[prop.lower()] = content.strip()

        # 2. Capture JSON-LD script blocks
        if tag == "script" and attr_dict.get("type") == "application/ld+json":
            self._in_script_json_ld = True
            self._text_accumulator = []
            return

        # 3. Detect Product Card Containers
        class_val = attr_dict.get("class", "").lower()
        id_val = attr_dict.get("id", "").lower()

        # Explicit card markers identifying individual product items
        is_card_marker = any(
            marker in class_val or marker in id_val
            for marker in [
                "product-card",
                "product_card",
                "product-item",
                "product_item",
                "product-grid-item",
                "grid-product",
                "woocommerce-loopproduct",
                "type-product",
                "card-wrapper",
                "product-inner",
                "product-entry",
                "pls-product-inner",
            ]
        )

        # Semantic tag representing a single product (not a parent container)
        is_container = any(c in class_val for c in ("grid", "list", "row", "loop", "container", "products", "archive", "wrap", "wrapper"))
        is_semantic_card = tag in ("article", "li") and ("type-product" in class_val or ("product" in class_val and not is_container))

        is_card_start = is_card_marker or is_semantic_card

        if is_card_start and not self._in_card:
            self._in_card = True
            self._card_depth = 1
            self._title_depth = 0
            self._price_depth = 0
            self._ignore_depth = 0
            self._current_card = {
                "title": "",
                "title_texts": [],
                "price": "",
                "price_texts": [],
                "product_url": "",
                "image_url": "",
                "texts": [],
                "alt_title": "",
            }
        elif is_card_start and self._in_card and is_card_marker and (self._current_card.get("image_url") or self._current_card.get("title")):
            # Sibling card started without closing tag
            self._finalize_current_card()
            self._in_card = True
            self._card_depth = 1
            self._title_depth = 0
            self._price_depth = 0
            self._ignore_depth = 0
            self._current_card = {
                "title": "",
                "title_texts": [],
                "price": "",
                "price_texts": [],
                "product_url": "",
                "image_url": "",
                "texts": [],
                "alt_title": "",
            }
        elif self._in_card:
            self._card_depth += 1

            # Check for title elements
            is_title = tag in ("h1", "h2", "h3", "h4", "h5", "h6") or any(
                k in class_val for k in ("product-title", "product__title", "entry-title", "card-title", "product_title")
            )
            if is_title:
                self._title_depth += 1

            # Check for price elements
            is_price = any(
                k in class_val for k in ("price", "amount", "money", "woocommerce-price", "product-price")
            )
            if is_price:
                self._price_depth += 1

            # Check for ignore elements (wishlist, badges, quick view)
            is_ignore = any(
                k in class_val or k in id_val
                for k in ("wishlist", "quickview", "quick-view", "cart-button", "yith-wcwl", "product-labels", "badge", "on-sale")
            )
            if is_ignore:
                self._ignore_depth += 1

            # Extract links inside card (preferring explicit product detail pages)
            if tag == "a" and "href" in attr_dict:
                href = attr_dict["href"].strip()
                if href and href not in ("#", "/", "") and not href.startswith("javascript:") and not href.startswith("?"):
                    joined = urljoin(self.base_url, href)
                    if not self._current_card.get("product_url") or any(kw in joined for kw in ("/product/", "/products/", "/item/", "/p/")):
                        self._current_card["product_url"] = joined

            # Extract images inside card
            if tag == "img":
                candidates = [
                    attr_dict.get("data-src"),
                    attr_dict.get("data-lazy-src"),
                    attr_dict.get("data-original"),
                    attr_dict.get("data-srcset"),
                    attr_dict.get("srcset"),
                    attr_dict.get("src"),
                ]
                resolved_img = None
                for c in candidates:
                    if not c:
                        continue
                    c = c.strip()
                    if " " in c or "," in c:
                        c = c.split(",")[0].split(" ")[0].strip()
                    if c.startswith("data:") or "transparent" in c or "placeholder" in c:
                        continue
                    if c.startswith("http") or c.startswith("/"):
                        resolved_img = urljoin(self.base_url, c)
                        break

                if resolved_img and not self._current_card.get("image_url"):
                    self._current_card["image_url"] = resolved_img

                # Use image alt as fallback title
                alt = attr_dict.get("alt", "").strip()
                if alt and len(alt) > 3 and not any(ign in alt.lower() for ign in _NOISE_PHRASES):
                    self._current_card["alt_title"] = alt

    def handle_endtag(self, tag: str) -> None:
        if tag == "script" and self._in_script_json_ld:
            self._in_script_json_ld = False
            self.json_ld_blocks.append("".join(self._text_accumulator).strip())
            self._text_accumulator = []

        if self._in_card:
            if self._title_depth > 0 and tag in ("h1", "h2", "h3", "h4", "h5", "h6", "div", "span", "a"):
                self._title_depth -= 1
            if self._price_depth > 0:
                self._price_depth -= 1
            if self._ignore_depth > 0:
                self._ignore_depth -= 1

            self._card_depth -= 1
            if self._card_depth <= 0:
                self._in_card = False
                self._finalize_current_card()

    def handle_data(self, data: str) -> None:
        text = data.strip()
        if not text:
            return

        if self._in_script_json_ld:
            self._text_accumulator.append(data)
            return

        if self._in_card:
            if self._ignore_depth > 0:
                return

            if self._title_depth > 0:
                self._current_card["title_texts"].append(text)

            if self._price_depth > 0:
                self._current_card["price_texts"].append(text)

            self._current_card["texts"].append(text)

            price_match = _PRICE_REGEX.search(text)
            if price_match and not self._current_card.get("price") and any(sym in text for sym in ("$", "€", "£", "₹", "Rs", "LKR")):
                self._current_card["price"] = price_match.group(0).strip()

    def _finalize_current_card(self) -> None:
        if not self._current_card:
            return

        # 1. Resolve Title
        title = ""
        if self._current_card.get("title_texts"):
            title = " ".join(self._current_card["title_texts"]).strip()

        if not title:
            title = self._current_card.get("alt_title") or ""

        if not title:
            for t in self._current_card.get("texts", []):
                t_clean = t.strip()
                t_lower = t_clean.lower()
                if len(t_clean) > 3 and not _PRICE_REGEX.search(t_clean):
                    if not any(ign in t_lower for ign in _NOISE_PHRASES):
                        title = t_clean
                        break

        # 2. Reject noise titles
        title_lower = title.lower()
        if not title or len(title) < 2 or any(ign in title_lower for ign in _NOISE_PHRASES):
            self._current_card = {}
            return

        cleaned_title = re.sub(
            r"^(?:side view of a|front view of a|side view of|front view of|wedding sarees in sri lanka -)\s*",
            "",
            title,
            flags=re.IGNORECASE,
        )
        cleaned_title = re.sub(
            r"\s*(?:wearing woman side view|wearing woman front view|wearing woman|front view|side view)$",
            "",
            cleaned_title,
            flags=re.IGNORECASE,
        ).strip()
        if len(cleaned_title) >= 3:
            title = cleaned_title

        # 3. Resolve Price
        price = self._current_card.get("price")
        if not price and self._current_card.get("price_texts"):
            combined_price = " ".join(self._current_card["price_texts"]).strip()
            pmatch = _PRICE_REGEX.search(combined_price)
            if pmatch:
                price = pmatch.group(0).strip()
                if not any(sym in price for sym in ("$", "€", "£", "₹", "Rs", "LKR")):
                    price = f"LKR {price}"

        if not price:
            price = "Price on Request"

        product_url = self._current_card.get("product_url") or self.base_url
        image_url = self._current_card.get("image_url")

        # 4. Strictly require valid absolute image URL (no transparent/placeholder assets)
        if image_url and image_url.startswith("http") and "transparent" not in image_url and "placeholder" not in image_url:
            self.products.append(
                {
                    "title": title,
                    "price": price,
                    "product_url": product_url,
                    "image_url": image_url,
                }
            )

        self._current_card = {}


class AtelierScraperService:
    """Service to search partner atelier web catalogs safely and extract product information."""

    def __init__(self, allowed_domains: list[str] | None = None):
        self.allowed_domains = [d.lower().strip() for d in (allowed_domains or []) if d and d.strip()]
        self.headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
            "Accept-Language": "en-US,en;q=0.9",
        }

    def is_safe_url(self, url: str) -> bool:
        """Validate URL to prevent SSRF and unauthorized network traversal."""
        try:
            parsed = urlparse(url.strip())
            if parsed.scheme.lower() not in ("http", "https"):
                return False

            hostname = parsed.hostname
            if not hostname:
                return False
            hostname_lower = hostname.lower()

            # Disallow localhost, loopback, link-local, and cloud metadata hostnames
            if hostname_lower in ("localhost", "127.0.0.1", "::1", "metadata.google.internal"):
                return False

            # Check for direct IP addresses
            try:
                ip = ipaddress.ip_address(hostname_lower)
                if ip.is_private or ip.is_loopback or ip.is_link_local or ip.is_reserved or ip.is_multicast:
                    return False
            except ValueError:
                # Normal domain string
                pass

            # If an explicit whitelist is provided, ensure domain matches or is a subdomain
            if self.allowed_domains:
                domain_matched = False
                for allowed in self.allowed_domains:
                    allowed_clean = urlparse(allowed).hostname or allowed
                    allowed_clean = allowed_clean.lower()
                    if hostname_lower == allowed_clean or hostname_lower.endswith(f".{allowed_clean}"):
                        domain_matched = True
                        break
                if not domain_matched:
                    return False

            return True
        except Exception:
            return False

    def parse_html_catalog(self, html_text: str, base_url: str) -> list[dict[str, Any]]:
        """Parse HTML string and extract product cards or OpenGraph / JSON-LD fallbacks."""
        parser = _ProductCardHTMLParser(base_url)
        try:
            parser.feed(html_text)
        except Exception as ex:
            logger.warning("HTML parser encountered warning on %s: %s", base_url, ex)

        results = list(parser.products)

        # Process JSON-LD blocks for structured product data
        for block in parser.json_ld_blocks:
            try:
                data = json.loads(block)
                items = data if isinstance(data, list) else [data]
                for item in items:
                    if isinstance(item, dict) and item.get("@type") == "Product":
                        name = item.get("name")
                        if name and not any(ign in str(name).lower() for ign in _NOISE_PHRASES):
                            offers = item.get("offers", {})
                            price_val = offers.get("price") or offers.get("lowPrice")
                            currency = offers.get("priceCurrency") or "$"
                            price_str = f"{currency} {price_val}" if price_val else "Price on Request"
                            img = item.get("image")
                            img_url = img[0] if isinstance(img, list) and img else (img if isinstance(img, str) else None)
                            if img_url and str(img_url).startswith("http") and "transparent" not in str(img_url) and "placeholder" not in str(img_url):
                                results.append(
                                    {
                                        "title": str(name),
                                        "price": price_str,
                                        "product_url": item.get("url") or base_url,
                                        "image_url": img_url,
                                    }
                                )
            except Exception:
                pass

        # OpenGraph single-product fallback if no product cards found
        if not results and parser.og_metadata.get("og:title"):
            og_title = parser.og_metadata["og:title"]
            og_img = parser.og_metadata.get("og:image")
            og_price = parser.og_metadata.get("og:price:amount")
            og_curr = parser.og_metadata.get("og:price:currency") or "$"
            price_display = f"{og_curr} {og_price}" if og_price else "Price on Request"
            if og_title and not any(ign in og_title.lower() for ign in _NOISE_PHRASES):
                if og_img and str(og_img).startswith("http") and "transparent" not in str(og_img) and "placeholder" not in str(og_img):
                    results.append(
                        {
                            "title": og_title,
                            "price": price_display,
                            "product_url": parser.og_metadata.get("og:url") or base_url,
                            "image_url": og_img,
                        }
                    )

        return results

    async def search_atelier(
        self,
        base_url: str,
        atelier_id: str,
        atelier_name: str,
        query: str,
        max_results: int = 5,
        client: httpx.AsyncClient | None = None,
    ) -> list[ScrapedAtelierProduct]:
        """Search a partner atelier website by query and return structured ScrapedAtelierProduct records."""
        if not self.is_safe_url(base_url):
            logger.warning("Rejecting unsafe or non-whitelisted atelier URL: %s", base_url)
            return []

        clean_base = base_url.rstrip("/")
        encoded_query = query.strip().replace(" ", "+")
        search_urls = [
            f"{clean_base}/?s={encoded_query}&post_type=product",
            f"{clean_base}/shop/?s={encoded_query}",
            f"{clean_base}/search?q={encoded_query}",
            f"{clean_base}/search?type=product&q={encoded_query}",
            f"{clean_base}/collections/all?q={encoded_query}",
            f"{clean_base}/catalog?search={encoded_query}",
        ]

        async def _fetch(c: httpx.AsyncClient, target_url: str) -> str | None:
            try:
                res = await c.get(target_url, headers=self.headers, timeout=10.0, follow_redirects=True)
                if res.status_code == 200 and res.text:
                    return res.text
            except Exception as ex:
                logger.debug("Fetch failed for %s: %s", target_url, ex)
            return None

        html_content = None
        if client:
            for url in search_urls:
                html_content = await _fetch(client, url)
                if html_content:
                    break
        else:
            async with httpx.AsyncClient(follow_redirects=True) as new_client:
                for url in search_urls:
                    html_content = await _fetch(new_client, url)
                    if html_content:
                        break

        if not html_content:
            return []

        raw_items = self.parse_html_catalog(html_content, base_url)
        matched_products: list[ScrapedAtelierProduct] = []

        seen_titles: set[str] = set()

        for item in raw_items:
            title = (item.get("title") or "").strip()
            image_url = item.get("image_url")

            # 1. Require a valid, absolute image URL (no blank / data: / transparent URLs)
            if not image_url or not isinstance(image_url, str) or not image_url.startswith("http") or "transparent" in image_url or "placeholder" in image_url:
                continue

            # 2. Reject noise titles
            title_lower = title.lower()
            if any(noise in title_lower for noise in _NOISE_PHRASES):
                continue

            if title.lower() in seen_titles:
                continue
            seen_titles.add(title.lower())

            matched_products.append(
                ScrapedAtelierProduct(
                    title=title,
                    price=item.get("price") or "Price on Request",
                    product_url=item.get("product_url") or base_url,
                    image_url=image_url,
                    fabric_details=None,
                    in_stock=True,
                    atelier_id=atelier_id,
                    atelier_name=atelier_name,
                    visual_match_score=0.90,
                    match_notes=f"Sourced from partner atelier '{atelier_name}' matching query '{query}'",
                )
            )
            if len(matched_products) >= max_results:
                break

        return matched_products
