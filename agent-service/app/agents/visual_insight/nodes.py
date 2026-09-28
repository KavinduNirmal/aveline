"""Node implementations for the Visual Insight Agent (Elle — Slice 2).

Orchestrates image analysis, inventory querying, customer aesthetic matching,
outfit composition, and supplier sourcing.
"""

import logging
import re
from typing import Any

from app.agents.visual_insight.state import VisualAgentState
from app.context import render_context_block
from app.core.config import get_settings
from app.llm.replies import unwrap_reply
from app.prompts.assembly import assemble_system_prompt
from app.schemas.visual_insight import (
    ImageAttributes,
    LookDto,
    PieceItem,
    SourcingRequestDto,
    coerce_visual_output,
)
from app.services.usage_reporter import report_usage
from app.tools.inventory.image_tools import analyze_product_image
from app.tools.inventory.inventory_tools import search_inventory
from app.tools.inventory.outfit_tools import compose_outfit
from app.tools.inventory.sourcing_tools import create_sourcing_request
from app.tools.inventory.supplier_tools import scrape_atelier_catalog

logger = logging.getLogger("aveline.agent.visual")

#: Color family mappings to allow shade synonyms while strictly pruning unrelated colors
_COLOR_FAMILIES: dict[str, set[str]] = {
    "red": {"red", "crimson", "deep crimson", "maroon", "deep maroon", "ruby", "ruby red", "burgundy", "royal burgundy", "scarlet", "wine", "cherry", "vermilion"},
    "blue": {"blue", "navy", "midnight navy", "sapphire", "midnight sapphire", "cobalt", "royal blue", "indigo", "sky blue", "powder blue", "baby blue", "teal", "peacock teal", "aqua", "turquoise", "peacock"},
    "green": {"green", "emerald", "emerald green", "sage", "sage green", "mint", "mint green", "olive", "deep olive", "jade", "forest green", "bottle green", "bottle", "forest", "dark green"},
    "pink": {"pink", "blush", "blush pink", "rose", "dusty rose", "rose pink", "magenta", "fuchsia", "coral", "peach", "apricot", "salmon"},
    "yellow": {"yellow", "gold", "champagne gold", "antique gold", "rose gold", "zari gold", "mustard", "mustard ochre", "ochre", "buttercup"},
    "purple": {"purple", "violet", "lavender", "lilac", "amethyst", "deep amethyst", "plum"},
    "white": {"white", "ivory", "heirloom ivory", "off-white", "cream", "pearl"},
    "black": {"black", "midnight black", "charcoal", "slate grey", "grey", "gray"},
    "brown": {"brown", "terracotta", "burnt terracotta", "rust", "camel", "taupe", "sand", "khaki", "espresso"},
}

_MAX_COMMENTARY_CHARS = 900


def _describe_what_was_seen(state: VisualAgentState) -> str | None:
    """A short noun phrase for the piece the vision path found in the customer's image.

    The acceptance test for this lane is that a reply *refers to what is actually in the picture*
    rather than asking the customer to describe it, and the analysis was already being computed:
    ``analyze_image`` fills ``image_attributes`` on every run, and every reply-composing node
    ignored it. Wiring the image through while leaving the wording generic would answer the
    customer the same way the defect did, only after a round-trip.

    Reads the analysed attributes rather than the raw message, so the phrase is grounded in what
    the model saw - "a deep crimson silk saree" - and not in what the customer typed. Returns
    ``None`` when no analysis happened (no media, or a denied/failed call), which is what keeps the
    text-only replies byte-identical to before.
    """
    raw = state.get("image_attributes")
    if not isinstance(raw, dict):
        return None

    # Attribute order is the order a person would describe a garment in: colour, fabric, type.
    # "Neutral" is the parse fallback rather than something the model saw, and describing a garment
    # as "a neutral silk saree" reads as an observation while carrying none - so it is dropped
    # here rather than allowed to stand in for a colour nobody actually reported.
    parts = [
        str(value).strip()
        for value in (raw.get("primary_color"), raw.get("fabric"), raw.get("category"))
        if value and str(value).strip() and str(value).strip().lower() != "neutral"
    ]
    if not parts:
        return None

    return " ".join(parts).lower()


class VisualInsightAgent:
    """LangGraph node suite for Elle (Visual Intelligence & Sourcing)."""

    def __init__(
        self,
        registry: Any = None,
        *,
        tools: Any = None,
        llm: Any = None,
    ) -> None:
        self._registry = registry or tools
        self.tools = tools or registry
        self.llm = llm

    async def parse_visual_intent(self, state: VisualAgentState) -> dict[str, Any]:
        """Extract visual search hints and occasion parameters from customer input."""
        msg = state.get("message", "").lower()
        org_id = state.get("org_id", "")

        # Extract occasion keywords
        occasion = None
        for occ in ["wedding", "gala", "cocktail", "dinner", "reception", "beach", "party", "office", "festive", "evening", "celebration"]:
            if occ in msg:
                occasion = occ.capitalize()
                break

        # Comprehensive fashion color taxonomy & theme palette mapping
        color = None
        color_theme = None

        # 1. Palette Themes mapping
        theme_keywords = {
            "pastel": ("Pastels", ["blush", "lavender", "mint", "powder blue", "peach", "sage"]),
            "jewel": ("Jewel Tones", ["emerald", "ruby", "sapphire", "amethyst", "garnet"]),
            "earthy": ("Earthy Neutrals", ["terracotta", "rust", "olive", "ochre", "camel", "mustard"]),
            "monochrome": ("Classic Monochrome", ["black", "white", "ivory", "charcoal", "slate grey"]),
            "metallic": ("Festive Metallics", ["gold", "silver", "bronze", "copper", "rose gold"]),
            "berry": ("Rich Berries", ["burgundy", "maroon", "crimson", "magenta", "plum", "wine"]),
            "oceanic": ("Oceanic Spectrum", ["teal", "turquoise", "aqua", "indigo", "peacock"]),
        }
        for theme_key, (theme_name, _) in theme_keywords.items():
            if theme_key in msg:
                color_theme = theme_name
                break

        # 2. Comprehensive Fashion Shades (ordered from compound to generic)
        fashion_colors = [
            # Pastels & Soft Hues
            "sage green", "mint green", "powder blue", "baby blue", "sky blue", "blush pink", "dusty rose",
            "rose pink", "lavender", "lilac", "peach", "buttercup", "apricot",
            # Earthy & Warm Neutrals
            "burnt terracotta", "terracotta", "mustard ochre", "mustard", "deep olive", "olive", "rust",
            "camel", "taupe", "sand", "khaki", "ochre", "espresso",
            # Jewel Tones
            "emerald green", "emerald", "forest green", "bottle green", "ruby red", "ruby", "midnight sapphire", "sapphire",
            "deep crimson", "crimson", "deep amethyst", "amethyst", "topaz", "jade",
            # Festive Metallics
            "champagne gold", "antique gold", "rose gold", "zari gold", "gold", "silver", "platinum",
            "copper", "bronze", "pewter",
            # Rich Berries & Sunset
            "royal burgundy", "burgundy", "deep maroon", "maroon", "wine", "plum", "magenta", "fuchsia",
            "coral", "tangerine", "orange",
            # Oceanic & Deep Blues
            "peacock teal", "teal", "turquoise", "aqua", "midnight navy", "navy", "royal blue", "cobalt",
            "indigo", "blue",
            # Classic Monochrome & Neutrals
            "midnight black", "charcoal", "black", "heirloom ivory", "off-white", "ivory", "cream",
            "pearl", "white", "slate grey", "grey", "gray",
            # General / Classics
            "red", "green", "yellow", "pink", "purple", "brown"
        ]

        for col in fashion_colors:
            if re.search(r"\b" + re.escape(col) + r"\b", msg):
                color = col.title()
                break

        if not color and color_theme:
            color = color_theme

        # 3. Comprehensive Garment Category Taxonomy
        category = None
        category_mappings = [
            # Sarees
            (r"\b(sarees?|saris?|kanjeevaram|kanjivaram|banarasi|chanderi|tussar|pochampally|patola|kasavu|silk saree)\b", "Sarees"),
            # Lehengas
            (r"\b(lehengas?|lehngas?|ghagras?|choli|lehenga choli)\b", "Lehengas"),
            # Gowns & Dresses
            (r"\b(gowns?|evening gowns?|ballgowns?|dress(?:es)?|maxi|midi|frocks?|jumpsuits?|rompers?)\b", "Gowns"),
            # Kurtas & Tunics / Tops / Blouses
            (r"\b(kurtas?|kurtis?|tunics?|silk blouses?|blouses?|anarkalis?|sherwanis?|tops?|shirts?)\b", "Kurtas & Tunics"),
            # Outerwear
            (r"\b(outerwear|blazers?|jackets?|coats?|overcoats?|trenches?|trenchcoats?|shrugs?|cardigans?|capes?|dusters?)\b", "Outerwear"),
            # Drapes & Shawls
            (r"\b(drapes?|shawls?|dupattas?|stoles?|scarfs?|scarves|wraps?|pallus?)\b", "Drapes & Shawls"),
            # Jewelry & Accessories
            (r"\b(jewelry|jewellery|accessories|accessory|necklaces?|earrings?|bangles?|clutch(?:es)?|bags?|footwear|heels?|shoes?|belts?)\b", "Jewelry & Accessories"),
        ]

        for pattern, cat_name in category_mappings:
            if re.search(pattern, msg):
                category = cat_name
                break

        # Extract leftover keywords for free-form query if no category/color was mapped
        stop_words = {
            "do", "we", "have", "any", "in", "stock", "is", "there", "are", "the", "a", "an",
            "for", "with", "please", "show", "me", "can", "you", "find", "tell", "check",
            "available", "look", "looking", "i", "want", "need", "pieces", "items", "u", "some", "of"
        }
        raw_words = [w for w in re.findall(r"\b\w+\b", msg) if len(w) > 2 and w not in stop_words]

        cleaned_query = None
        if not category and not color and raw_words:
            cleaned_query = " ".join(raw_words)

        criteria = {
            "organizationId": org_id,
            "occasion": occasion,
            "color": color,
            "category": category,
            "color_theme": color_theme,
        }
        if cleaned_query:
            criteria["query"] = cleaned_query

        return {"search_criteria": criteria}

    async def analyze_image(self, state: VisualAgentState) -> dict[str, Any]:
        """Call vision analysis on the reference arm, falling back to the legacy URL arm.

        The reference (``image_ref_kind``/``image_ref_id``) is preferred over the opaque
        ``image_url`` bridge. The organisation is required: without it the call is skipped
        rather than sent with the all-zeros sentinel (strategy §4 C15). A denied analysis and a
        failed one are surfaced as distinct reasons.
        """
        org_id = state.get("org_id")
        image_url = state.get("image_url")
        image_ref_kind = state.get("image_ref_kind")
        image_ref_id = state.get("image_ref_id")
        has_reference = bool(image_ref_kind and image_ref_id)

        if not has_reference and not image_url:
            return {"image_attributes": None}
        if not org_id:
            logger.warning("Image analysis skipped: no organisation in state; refusing Guid.Empty.")
            return {"image_attributes": None, "reason": "image_analysis_skipped_no_org"}

        outcome = await analyze_product_image(
            self._registry,
            image_url=image_url,
            org_id=org_id,
            image_ref_kind=image_ref_kind,
            image_ref_id=image_ref_id,
        )
        if not outcome.succeeded or outcome.attributes is None:
            logger.warning(
                "Image analysis %s: %s",
                outcome.status.value,
                outcome.error,
                extra={"action": "image_analysis", "outcome": outcome.status.value},
            )
            return {
                "image_attributes": None,
                "reason": f"image_analysis_{outcome.status.value}",
            }

        attrs: ImageAttributes = outcome.attributes
        criteria = state.get("search_criteria") or {}
        if attrs.category and not criteria.get("category"):
            criteria["category"] = attrs.category
        if attrs.primary_color and not criteria.get("color"):
            criteria["color"] = attrs.primary_color
        if attrs.occasion and not criteria.get("occasion"):
            criteria["occasion"] = attrs.occasion

        return {
            "image_attributes": attrs.model_dump(),
            "search_criteria": criteria,
        }

    async def search_inventory(self, state: VisualAgentState) -> dict[str, Any]:
        """Query boutique inventory using compiled criteria.

        A failed lookup is recorded explicitly rather than collapsed into "no matches". Those are
        different facts, and the sourcing path downstream reads an empty result as "not in stock" -
        which would turn a transport error into a confident, false claim about the boutique's
        stock.
        """
        criteria = state.get("search_criteria") or {
            "organizationId": state.get("org_id", ""),
            "query": state.get("message", ""),
        }

        try:
            items: list[PieceItem] = await search_inventory(self._registry, criteria)

            # Defensive category filter: when a category is requested, filter out mismatched pieces
            target_category = criteria.get("category")
            if target_category and items:
                norm_target = target_category.lower()
                singular_target = norm_target[:-1] if norm_target.endswith("s") and len(norm_target) > 3 else norm_target

                # Extract significant category keywords (ignoring & and common small noise words)
                raw_words = [w for w in re.findall(r"\b\w+\b", norm_target) if len(w) > 2 and w not in ("and", "the", "for", "with")]
                category_tokens = set(raw_words + [singular_target, norm_target])
                for w in raw_words:
                    if w.endswith("s") and len(w) > 3:
                        category_tokens.add(w[:-1])
                if "saree" in category_tokens or "sarees" in category_tokens:
                    category_tokens.add("sari")

                def _is_category_match(item: PieceItem) -> bool:
                    item_cat = (item.category or "").lower()
                    item_name = (item.name or "").lower()
                    if item_cat:
                        if norm_target in item_cat or item_cat in norm_target or singular_target in item_cat:
                            return True
                        if any(token in item_cat for token in category_tokens):
                            return True
                    if norm_target in item_name or singular_target in item_name:
                        return True
                    if any(token in item_name for token in category_tokens):
                        return True
                    return False

                items = [item for item in items if _is_category_match(item)]

            # Defensive color filter: when a specific color is requested, prune mismatched colors
            target_color = criteria.get("color")
            if target_color and items:
                norm_color = target_color.lower().strip()

                def _is_color_match(item: PieceItem) -> bool:
                    item_color = (item.color or "").lower().strip()
                    item_name = (item.name or "").lower().strip()

                    # Direct match
                    if item_color and (norm_color in item_color or item_color in norm_color):
                        return True
                    if re.search(r"\b" + re.escape(norm_color) + r"\b", item_name):
                        return True

                    # Color family expansion
                    family_shades = _COLOR_FAMILIES.get(norm_color)
                    if family_shades:
                        if any(shade in item_color for shade in family_shades):
                            return True
                        if any(re.search(r"\b" + re.escape(shade) + r"\b", item_name) for shade in family_shades):
                            return True

                    return False

                items = [item for item in items if _is_color_match(item)]

            return {"matched_items": [item.model_dump() for item in items], "search_failed": False}
        except Exception as e:  # noqa: BLE001 - recorded in state, surfaced by compose_output
            logger.warning("Inventory search failed: %s", e)
            return {
                "matched_items": [],
                "search_failed": True,
                "search_error": str(e),
            }

    async def compose_looks(self, state: VisualAgentState) -> dict[str, Any]:
        """Assemble matched pieces into styled lookbooks."""
        raw_items = state.get("matched_items") or []
        items = [PieceItem(**item) for item in raw_items]
        occasion = (state.get("search_criteria") or {}).get("occasion") or "Boutique Collection"
        staff_query = bool(state.get("staff_query"))

        if items:
            look: LookDto = compose_outfit(items, occasion=occasion, customer_preferences=state.get("preferences"))

            prompt_tokens = 0
            completion_tokens = 0
            if self.llm is not None:
                try:
                    # The bounded conversation window (ADR-023) rides on the system layer beside
                    # the boutique context, so the styling commentary can see what the thread has
                    # established rather than only the pieces matched this turn.
                    system_prompt = assemble_system_prompt(
                        "visual",
                        {"organization_id": state.get("org_id")},
                        dialogue_context=render_context_block(
                            history=state.get("history"),
                            thread_summary=state.get("thread_summary"),
                            pinned_slots=state.get("pinned_slots"),
                        ),
                    )
                    # Stock is stated as fact because the model otherwise hedges: an unstated
                    # availability became "availability: unknown" plus a "please verify current
                    # stock" caveat, on pieces we had just confirmed in stock.
                    in_stock = "; ".join(
                        f"{i.name} ({i.category or 'garment'}, {i.color or 'color'}, "
                        f"{i.stock if i.stock is not None else 'unknown'} in stock)"
                        for i in items
                    )
                    user_msg = (
                        f"Curate a look for occasion '{occasion}' featuring these pieces, which are "
                        f"confirmed in stock: {in_stock}. "
                        "Reply with ONLY the styling commentary as plain prose (2-4 sentences): "
                        "no JSON, no code fences, no labels, no lists of pieces."
                    )
                    llm_res = await self.llm.ainvoke([("system", system_prompt), ("human", user_msg)])
                    if hasattr(llm_res, "content") and llm_res.content:
                        # The universal prompt tells the model to emit a JSON envelope, so a
                        # compliant reply is a curated-look object. Writing that straight into
                        # `text` rendered a wall of JSON in the Salon, which is what staff saw.
                        # Never put structured content into a display field: take the prose, or
                        # keep the deterministic commentary.
                        look.text = unwrap_reply(
                            llm_res.content, fallback=look.text, max_chars=_MAX_COMMENTARY_CHARS
                        )
                    if hasattr(llm_res, "usage_metadata") and llm_res.usage_metadata:
                        prompt_tokens = llm_res.usage_metadata.get("input_tokens", 0)
                        completion_tokens = llm_res.usage_metadata.get("output_tokens", 0)
                except Exception as e:
                    logger.warning("LLM look composition commentary failed, using rule fallback: %s", e)

            if staff_query:
                # Bug 1 fix: for staff queries, emit concise factual text and no customer-facing suggestion box
                summary_text = f"Found {len(items)} matching inventory item(s) for query."
                return {
                    "composed_looks": [look.model_dump()],
                    "suggestion": None,
                    "summary": summary_text,
                    "text": summary_text,
                    "status": "success",
                    "prompt_tokens": prompt_tokens,
                    "completion_tokens": completion_tokens,
                }
            else:
                name = items[0].name
                # When the customer sent a picture, the reply says what was seen in it. This is the
                # difference between "we found something" and "we found something for the piece you
                # photographed", and it is the whole point of the vision path.
                seen = _describe_what_was_seen(state)
                if seen:
                    suggestion = (
                        f"From your photo, this reads as a {seen} - Elle curated {len(items)} "
                        f"piece(s) in that spirit, including the {name}."
                    )
                else:
                    suggestion = (
                        f"Elle curated {len(items)} piece(s) harmonizing with your aesthetic, "
                        f"including the {name}."
                    )
                return {
                    "composed_looks": [look.model_dump()],
                    "suggestion": suggestion,
                    "summary": None,
                    "text": None,
                    "status": "success",
                    "prompt_tokens": prompt_tokens,
                    "completion_tokens": completion_tokens,
                }

        return {
            "composed_looks": [],
            "status": "pending_sourcing",
        }

    async def check_sourcing(self, state: VisualAgentState) -> dict[str, Any]:
        """Initiate supplier sourcing when in-stock pieces are unavailable.

        Guarded against the failed-search case: sourcing is only honest when the lookup actually
        succeeded and matched nothing. Raising a sourcing request after a transport error would
        tell a customer a piece is unavailable when the truth is that nobody looked properly.
        """
        if state.get("search_failed"):
            reason = state.get("search_error") or "inventory lookup failed"
            logger.warning("Skipping sourcing: the inventory lookup failed (%s).", reason)
            return {
                "sourcing_request": None,
                "suggestion": None,
                "summary": None,
                "text": None,
                "status": "error",
                "reason": "inventory_unavailable",
            }

        msg = state.get("message", "")
        org_id = state.get("org_id", "")
        customer_id = state.get("customer_id")
        image_url = state.get("image_url")
        staff_query = bool(state.get("staff_query"))

        notes = f"Sourcing request initiated from inquiry: '{msg}'"
        req: SourcingRequestDto = await create_sourcing_request(
            self._registry,
            org_id=org_id,
            customer_id=customer_id,
            image_url=image_url,
            notes=notes,
        )

        # Dynamic Partner Atelier Catalog Web Scraping
        query_text = (state.get("search_criteria") or {}).get("query") or msg
        detected_fabric = (state.get("image_attributes") or {}).get("fabric")
        detected_color = (state.get("image_attributes") or {}).get("primary_color")

        scraped_options: list[dict[str, Any]] = []
        if query_text:
            scraped_options = await scrape_atelier_catalog(
                self._registry,
                query=query_text,
                org_id=org_id,
                detected_fabric=detected_fabric,
                detected_color=detected_color,
            )

        if staff_query:
            summary_text = "No in-stock pieces matched. Sourcing request created with partner ateliers."
            if scraped_options:
                summary_text += f" Found {len(scraped_options)} partner options."
            return {
                "sourcing_request": req.model_dump(),
                "partner_sourcing_options": scraped_options or None,
                "suggestion": None,
                "summary": summary_text,
                "text": summary_text,
                "status": "pending",
            }
        else:
            # The out-of-stock answer names the piece the customer actually photographed, so it
            # reads as a response to their image rather than a generic "not available". This is the
            # live case from the handoff: a saree photo whose reply never mentioned the saree.
            seen = _describe_what_was_seen(state)
            if scraped_options:
                partner_names = ", ".join({opt["atelier_name"] for opt in scraped_options if "atelier_name" in opt})
                suggestion = (
                    f"We do not have that {seen} in stock right now, but Elle found matching pieces at our partner ateliers "
                    f"({partner_names}) and initiated a sourcing ticket."
                    if seen
                    else (
                        f"We do not have this exact piece in stock right now, but Elle found matching pieces at our partner ateliers "
                        f"({partner_names}) and initiated a sourcing ticket."
                    )
                )
            else:
                suggestion = (
                    f"We do not have that {seen} in stock right now, but Elle has initiated a custom "
                    "sourcing request with our partner ateliers."
                    if seen
                    else (
                        "We do not have this exact piece in stock right now, but Elle has initiated a "
                        "custom sourcing request with our partner ateliers."
                    )
                )
            return {
                "sourcing_request": req.model_dump(),
                "partner_sourcing_options": scraped_options or None,
                "suggestion": suggestion,
                "summary": None,
                "text": None,
                "status": "pending",
            }

    async def compose_output(self, state: VisualAgentState) -> dict[str, Any]:
        """Format final output payload conforming to VisualAgentOutput contract."""
        status = state.get("status") or "success"
        if status not in ("success", "no_results", "pending", "out_of_scope", "skipped", "stub", "error"):
            status = "success"

        items = [PieceItem(**i) for i in state.get("matched_items", [])]
        looks = [LookDto(**look) for look in state.get("composed_looks", [])]
        sourcing_req = (
            SourcingRequestDto(**state["sourcing_request"])
            if state.get("sourcing_request")
            else None
        )
        image_attrs = (
            ImageAttributes(**state["image_attributes"])
            if state.get("image_attributes")
            else None
        )

        output = coerce_visual_output({
            "status": status,
            "agent": "visual",
            "ran": True,
            "suggestion": state.get("suggestion"),
            "summary": state.get("summary"),
            "text": state.get("text"),
            "items": items,
            "looks": looks,
            "sourcing_request": sourcing_req,
            "partner_sourcing_options": state.get("partner_sourcing_options"),
            "image_attributes": image_attrs,
            "reason": state.get("reason"),
        })

        # ADR-010 Usage Reporting (best-effort)
        prompt_tokens = state.get("prompt_tokens") or 0
        completion_tokens = state.get("completion_tokens") or 0
        if (prompt_tokens + completion_tokens) > 0 and state.get("org_id"):
            try:
                settings = get_settings()
                await report_usage(
                    organization_id=str(state["org_id"]),
                    request_id=str(state.get("customer_id") or "visual-req"),
                    workflow_id="visual_insight",
                    provider=settings.llm_provider,
                    model=settings.llm_model or "deepseek-chat",
                    input_tokens=prompt_tokens,
                    output_tokens=completion_tokens,
                )
            except Exception as exc:
                logger.warning("Usage reporting failed (non-fatal): %s", exc)

        return {"output": output.model_dump()}

