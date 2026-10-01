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
from app.gate import has_explicit_search, is_customer_note_or_preference
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
    "red": {"red", "crimson", "deep crimson", "maroon", "deep maroon", "ruby", "ruby red", "burgundy", "royal burgundy", "scarlet", "wine", "cherry", "vermilion", "rust"},
    "blue": {"blue", "navy", "midnight navy", "sapphire", "midnight sapphire", "cobalt", "royal blue", "indigo", "sky blue", "skyblue", "powder blue", "baby blue", "teal", "peacock teal", "aqua", "turquoise", "peacock", "darkblue", "lightblue"},
    "green": {"green", "emerald", "emerald green", "emeraldgreen", "sage", "sage green", "sagegreen", "mint", "mint green", "mintgreen", "olive", "deep olive", "jade", "forest green", "bottle green", "bottlegreen", "forest", "dark green", "darkgreen", "chartreuse", "lime"},
    "pink": {"pink", "blush", "blush pink", "rose", "dusty rose", "dustyrose", "rose pink", "rosepink", "magenta", "fuchsia", "coral", "peach", "apricot", "salmon"},
    "yellow": {"yellow", "gold", "champagne gold", "antique gold", "rose gold", "zari gold", "mustard", "mustard ochre", "mustard yellow", "mustardyellow", "ochre", "buttercup", "butter yellow", "chartreuse", "lemon", "canary", "marigold", "amber", "golden", "lime", "lightyellow"},
    "purple": {"purple", "violet", "lavender", "lilac", "amethyst", "deep amethyst", "plum", "mauve"},
    "white": {"white", "ivory", "heirloom ivory", "off-white", "cream", "pearl"},
    "black": {"black", "midnight black", "charcoal", "slate grey", "grey", "gray", "noir"},
    "brown": {"brown", "terracotta", "burnt terracotta", "rust", "camel", "taupe", "sand", "khaki", "espresso", "beige", "tan"},
}

_CATEGORY_MAPPINGS = [
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


def _resolve_garment_category(attrs: ImageAttributes) -> str | None:
    """Resolve a specific garment category from multi-item analysis or silhouette attributes."""
    text_corpus: list[str] = []
    if attrs.detected_items:
        for itm in attrs.detected_items:
            if itm.clothing_type:
                text_corpus.append(itm.clothing_type)
            if itm.suggested_item_name:
                text_corpus.append(itm.suggested_item_name)
            if itm.description:
                text_corpus.append(itm.description)
    if attrs.silhouette:
        text_corpus.append(attrs.silhouette)
    if attrs.category and attrs.category.lower() not in ("garment", "ethnic_couture", "unknown", "top"):
        text_corpus.append(attrs.category)

    combined = " ".join(text_corpus).lower()
    for pattern, cat_name in _CATEGORY_MAPPINGS:
        if re.search(pattern, combined):
            return cat_name
    return None


_MAX_COMMENTARY_CHARS = 900


def _describe_what_was_seen(state: VisualAgentState) -> str | None:
    """A short noun phrase for the piece the vision path found in the customer's image."""
    raw = state.get("image_attributes")
    if not isinstance(raw, dict):
        return None

    cat = (state.get("search_criteria") or {}).get("category") or raw.get("category")
    if cat and cat.endswith("s") and not cat.endswith("ss") and len(cat) > 3:
        cat = cat[:-1]

    parts = [
        str(value).strip()
        for value in (raw.get("primary_color"), raw.get("fabric"), cat)
        if value and str(value).strip() and str(value).strip().lower() not in ("neutral", "unknown", "garment", "ethnic_couture")
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
        intent_type = state.get("intent_type")
        has_image = bool(state.get("image_url") or state.get("image_ref_id") or state.get("image_attributes"))

        # Skip visual processing on customer memory notes/preferences or non-visual intents without an image
        if not has_image and (
            intent_type in ("customer_preference", "event_query", "general_inquiry", "tenant_account", "aveline_help")
            or (is_customer_note_or_preference(msg) and not has_explicit_search(msg))
        ):
            logger.info("Visual agent skipping turn: customer memory/context message with no explicit visual search.")
            return {
                "search_criteria": {},
                "status": "skipped",
                "reason": "customer_memory_context",
            }

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
        for pattern, cat_name in _CATEGORY_MAPPINGS:
            if re.search(pattern, msg):
                category = cat_name
                break

        # Extract leftover keywords for free-form query if no category/color was mapped
        stop_words = {
            "do", "we", "have", "any", "in", "stock", "is", "there", "are", "the", "a", "an",
            "for", "with", "please", "show", "me", "can", "you", "find", "tell", "check",
            "available", "look", "looking", "i", "want", "need", "pieces", "items", "u", "some", "of",
            "this", "that", "these", "those", "it", "one", "ones", "picture", "photo", "image", "pic",
            "outfit", "dress", "saree", "piece", "item", "something", "like", "similar", "same",
            "also", "too", "anything", "else", "got", "get", "inquire", "what", "about"
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

        # Resolve specific category from detected garments or silhouette
        resolved_category = _resolve_garment_category(attrs)
        if resolved_category:
            criteria["category"] = resolved_category
        elif attrs.category and not criteria.get("category"):
            criteria["category"] = attrs.category

        if attrs.primary_color:
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
        if state.get("status") == "skipped":
            return {"matched_items": [], "search_failed": False}

        criteria = state.get("search_criteria") or {
            "organizationId": state.get("org_id", ""),
            "query": state.get("message", ""),
        }

        try:
            items: list[PieceItem] = await search_inventory(self._registry, criteria)

            # If initial inventory lookup with primary criteria returned empty, check ensemble & secondary colors
            raw_attrs = state.get("image_attributes")
            if not items and isinstance(raw_attrs, dict):
                # Collect secondary and detected items colors
                candidate_colors: list[str] = []
                for sc in raw_attrs.get("secondary_colors") or []:
                    if sc and str(sc).strip():
                        candidate_colors.append(str(sc).strip())
                for itm in raw_attrs.get("detected_items") or []:
                    if itm.get("primary_color") and str(itm["primary_color"]).strip():
                        candidate_colors.append(str(itm["primary_color"]).strip())
                    for sc in itm.get("secondary_colors") or []:
                        if sc and str(sc).strip():
                            candidate_colors.append(str(sc).strip())

                # Try candidate colors sequentially
                seen_colors = {str(criteria.get("color", "")).lower()}
                for color_cand in candidate_colors:
                    if color_cand.lower() in seen_colors:
                        continue
                    seen_colors.add(color_cand.lower())
                    cand_criteria = dict(criteria)
                    cand_criteria["color"] = color_cand
                    try:
                        c_items = await search_inventory(self._registry, cand_criteria)
                        if c_items:
                            items.extend(c_items)
                            break
                    except Exception:
                        pass

            # Deduplicate items by itemId
            seen_ids = set()
            deduped_items = []
            for item in items:
                item_id = item.itemId or item.id or item.name
                if item_id and item_id not in seen_ids:
                    seen_ids.add(item_id)
                    deduped_items.append(item)
                elif not item_id:
                    deduped_items.append(item)
            items = deduped_items

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

                # Expand category tokens with category taxonomy aliases
                if "gowns" in category_tokens or "gown" in category_tokens:
                    category_tokens.update({"dress", "dresses", "gown", "gowns", "slip", "maxi", "midi", "frock"})
                elif "lehengas" in category_tokens or "lehenga" in category_tokens:
                    category_tokens.update({"lehenga", "lehengas", "choli", "ghagra"})
                elif "sarees" in category_tokens or "saree" in category_tokens:
                    category_tokens.update({"saree", "sarees", "sari", "saris", "kanjeevaram", "banarasi", "chanderi"})
                elif "kurtas" in category_tokens or "kurta" in category_tokens:
                    category_tokens.update({"kurta", "kurtas", "kurti", "kurtis", "tunic", "tunics", "top", "blouse"})

                raw_cat = (state.get("image_attributes") or {}).get("category")
                if raw_cat:
                    category_tokens.update([w for w in re.findall(r"\b\w+\b", str(raw_cat).lower()) if len(w) > 2])

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

            # Defensive color filter: when a specific color is requested or detected, prune mismatched colors
            target_color = criteria.get("color")
            raw_attrs = state.get("image_attributes")
            if (target_color or raw_attrs) and items:
                # Gather all acceptable base shades
                acceptable_shades: set[str] = set()
                if target_color:
                    acceptable_shades.add(str(target_color).lower().strip())
                if isinstance(raw_attrs, dict):
                    if raw_attrs.get("primary_color"):
                        acceptable_shades.add(str(raw_attrs["primary_color"]).lower().strip())
                    for sc in raw_attrs.get("secondary_colors") or []:
                        if sc and str(sc).strip():
                            acceptable_shades.add(str(sc).lower().strip())
                    for itm in raw_attrs.get("detected_items") or []:
                        if itm.get("primary_color") and str(itm["primary_color"]).strip():
                            acceptable_shades.add(str(itm["primary_color"]).lower().strip())
                        for sc in itm.get("secondary_colors") or []:
                            if sc and str(sc).strip():
                                acceptable_shades.add(str(sc).lower().strip())

                # Expand acceptable shades with all shade synonyms and color families
                expanded_acceptable: set[str] = set()
                for base_shade in acceptable_shades:
                    expanded_acceptable.add(base_shade)
                    # Add individual significant words (e.g. 'crimson' from 'crimson red')
                    for w in re.findall(r"\b\w+\b", base_shade):
                        if len(w) > 2:
                            expanded_acceptable.add(w)

                    # Expand against known color families
                    for fam_key, shades in _COLOR_FAMILIES.items():
                        if base_shade in shades or base_shade == fam_key or any(s in base_shade for s in shades):
                            expanded_acceptable.update(shades)
                            expanded_acceptable.add(fam_key)

                def _is_color_match(item: PieceItem) -> bool:
                    item_color = (item.color or "").lower().strip()
                    item_name = (item.name or "").lower().strip()

                    # Direct or token match in color field
                    if item_color:
                        if any(shade in item_color or item_color in shade for shade in expanded_acceptable):
                            return True
                    # Word boundary match in item name
                    if any(re.search(r"\b" + re.escape(shade) + r"\b", item_name) for shade in expanded_acceptable):
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
        if state.get("status") == "skipped":
            return {"composed_looks": [], "status": "skipped"}

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
        if state.get("status") == "skipped":
            return {
                "sourcing_request": None,
                "partner_sourcing_options": None,
                "suggestion": None,
                "summary": None,
                "text": None,
                "status": "skipped",
            }

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

        # 1. Construct refined search terms from extracted visual criteria (color, category, query terms)
        criteria = state.get("search_criteria") or {}
        search_terms = []
        if criteria.get("color"):
            search_terms.append(str(criteria["color"]))
        if criteria.get("category"):
            cat_str = str(criteria["category"])
            if cat_str.endswith("s") and not cat_str.endswith("ss"):
                cat_str = cat_str[:-1]
            search_terms.append(cat_str)
        if criteria.get("query"):
            search_terms.append(str(criteria["query"]))

        query_text = " ".join(search_terms) if search_terms else msg

        detected_fabric = (state.get("image_attributes") or {}).get("fabric")
        detected_color = (state.get("image_attributes") or {}).get("primary_color") or criteria.get("color")
        detected_category = criteria.get("category")

        # 2. First scrape partner atelier websites for matching garments
        scraped_options: list[dict[str, Any]] = []
        if query_text:
            scraped_options = await scrape_atelier_catalog(
                self._registry,
                query=query_text,
                org_id=org_id,
                detected_fabric=detected_fabric,
                detected_color=detected_color,
                detected_category=detected_category,
            )

        # 3. Formulate response based on whether partner atelier matches were found
        partner_names = ", ".join(sorted({opt["atelier_name"] for opt in scraped_options if "atelier_name" in opt})) if scraped_options else ""

        if scraped_options:
            # Present discovered partner pieces as interactive visual proposals (ticket created only upon selection)
            if staff_query:
                summary_text = (
                    f"No in-stock pieces matched in boutique inventory. "
                    f"Found {len(scraped_options)} matching partner atelier option(s) from {partner_names}."
                )
                return {
                    "sourcing_request": None,
                    "partner_sourcing_options": scraped_options,
                    "suggestion": None,
                    "summary": summary_text,
                    "text": summary_text,
                    "status": "pending",
                }
            else:
                seen = _describe_what_was_seen(state)
                item_desc = seen or (query_text.lower() if query_text else "piece")
                suggestion = (
                    f"We don't currently have this {item_desc} in boutique stock, but Elle found {len(scraped_options)} "
                    f"option(s) at {partner_names} ready to custom-source for you."
                )
                return {
                    "sourcing_request": None,
                    "partner_sourcing_options": scraped_options,
                    "suggestion": suggestion,
                    "summary": None,
                    "text": None,
                    "status": "success",
                }
        else:
            # When 0 options exist across boutique and partner ateliers, initiate a pending ticket
            notes = f"Custom sourcing request for unavailable inquiry: '{msg}'"
            req: SourcingRequestDto = await create_sourcing_request(
                self._registry,
                org_id=org_id,
                customer_id=customer_id,
                image_url=image_url,
                notes=notes,
            )

            if staff_query:
                summary_text = "No in-stock or partner atelier pieces matched. Sourcing request created with partner ateliers."
                return {
                    "sourcing_request": req.model_dump() if req else None,
                    "partner_sourcing_options": None,
                    "suggestion": None,
                    "summary": summary_text,
                    "text": summary_text,
                    "status": "pending",
                }
            else:
                seen = _describe_what_was_seen(state)
                item_desc = seen or (query_text.lower() if query_text else "piece")
                suggestion = (
                    f"We do not have that {item_desc} in boutique stock right now. "
                    f"Elle has initiated a custom sourcing request with our partner ateliers to locate one for you."
                )
                return {
                    "sourcing_request": req.model_dump() if req else None,
                    "partner_sourcing_options": None,
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

        if status == "skipped":
            output = coerce_visual_output({
                "status": "skipped",
                "agent": "visual",
                "ran": True,
                "suggestion": None,
                "summary": None,
                "text": None,
                "items": [],
                "looks": [],
                "sourcing_request": None,
                "partner_sourcing_options": None,
                "image_attributes": None,
                "reason": state.get("reason") or "customer_memory_context",
            })
            return {"output": output.model_dump()}

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

