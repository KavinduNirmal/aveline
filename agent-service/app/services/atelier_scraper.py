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
    r"(?:(?:\$|€|£|₹|Rs\.?|LKR|USD|EUR|GBP)\s*[\d,]+(?:\.\d{2})?|[\d,]+(?:\.\d{2})?\s*(?:LKR|USD|EUR|GBP))",
    re.IGNORECASE,
)


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
        is_card_start = any(
            marker in class_val or marker in id_val
            for marker in [
                "product-card",
                "product-item",
                "product-grid-item",
                "grid-product",
                "product_card",
                "woocommerce-loopproduct",
                "product-wrap",
            ]
        ) or (tag == "article" and "product" in class_val)

        if is_card_start and not self._in_card:
            self._in_card = True
            self._card_depth = 1
            self._current_card = {
                "title": "",
                "price": "",
                "product_url": "",
                "image_url": "",
                "texts": [],
            }
        elif self._in_card:
            self._card_depth += 1

            # Extract links inside card
            if tag == "a" and "href" in attr_dict:
                href = attr_dict["href"].strip()
                if href and not self._current_card.get("product_url"):
                    self._current_card["product_url"] = urljoin(self.base_url, href)

            # Extract images inside card
            if tag == "img":
                src = (
                    attr_dict.get("src")
                    or attr_dict.get("data-src")
                    or attr_dict.get("data-srcset")
                    or attr_dict.get("data-original")
                    or ""
                ).strip()
                if src and not self._current_card.get("image_url"):
                    # Strip query parameters or srcset if needed
                    src = src.split(",")[0].split(" ")[0].strip()
                    self._current_card["image_url"] = urljoin(self.base_url, src)

                # Use image alt as fallback title
                alt = attr_dict.get("alt", "").strip()
                if alt and not self._current_card.get("title") and len(alt) > 3:
                    self._current_card["title"] = alt

    def handle_endtag(self, tag: str) -> None:
        if tag == "script" and self._in_script_json_ld:
            self._in_script_json_ld = False
            self.json_ld_blocks.append("".join(self._text_accumulator).strip())
            self._text_accumulator = []

        if self._in_card:
            self._card_depth -= 1
            if self._card_depth <= 0:
                self._in_card = False
                # Finalize current card
                self._finalize_current_card()

    def handle_data(self, data: str) -> None:
        text = data.strip()
        if not text:
            return

        if self._in_script_json_ld:
            self._text_accumulator.append(data)
            return

        if self._in_card:
            self._current_card["texts"].append(text)
            # Check if this text looks like a price
            price_match = _PRICE_REGEX.search(text)
            if price_match and not self._current_card.get("price"):
                self._current_card["price"] = price_match.group(0).strip()
            elif not self._current_card.get("title") and len(text) > 3 and not price_match:
                # Potential title
                if not any(ign in text.lower() for ign in ["quick view", "sale", "add to cart", "sold out", "new"]):
                    self._current_card["title"] = text

    def _finalize_current_card(self) -> None:
        if not self._current_card:
            return
        title = self._current_card.get("title") or ""
        price = self._current_card.get("price") or "Price on Request"
        product_url = self._current_card.get("product_url") or self.base_url
        image_url = self._current_card.get("image_url")

        # If title wasn't set, try picking from first non-price text
        if not title:
            for t in self._current_card.get("texts", []):
                if len(t) > 3 and not _PRICE_REGEX.search(t):
                    title = t
                    break

        if title and len(title) >= 2:
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
            "User-Agent": "Aveline-Atelier-Assistant/1.0 (+https://aveline.fashion/bot)",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
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
                    # Clean allowed domain
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
                        if name:
                            offers = item.get("offers", {})
                            price_val = offers.get("price") or offers.get("lowPrice")
                            currency = offers.get("priceCurrency") or "$"
                            price_str = f"{currency} {price_val}" if price_val else "Inquire"
                            img = item.get("image")
                            img_url = img[0] if isinstance(img, list) and img else (img if isinstance(img, str) else None)
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
            f"{clean_base}/search?q={encoded_query}",
            f"{clean_base}/catalog?search={encoded_query}",
            f"{clean_base}/shop?s={encoded_query}",
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
            async with httpx.AsyncClient() as new_client:
                for url in search_urls:
                    html_content = await _fetch(new_client, url)
                    if html_content:
                        break

        if not html_content:
            return []

        raw_items = self.parse_html_catalog(html_content, base_url)
        matched_products: list[ScrapedAtelierProduct] = []

        # Deduplicate and convert to ScrapedAtelierProduct
        seen_titles: set[str] = set()
        for item in raw_items:
            title = item.get("title", "").strip()
            if not title or title.lower() in seen_titles:
                continue
            seen_titles.add(title.lower())

            matched_products.append(
                ScrapedAtelierProduct(
                    title=title,
                    price=item.get("price") or "Price on Request",
                    product_url=item.get("product_url") or base_url,
                    image_url=item.get("image_url"),
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
