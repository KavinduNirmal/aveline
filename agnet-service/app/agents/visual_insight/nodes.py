"""Node implementations for the Visual Insight Agent (Elle — Slice 2).

Orchestrates image analysis, inventory querying, customer aesthetic matching,
outfit composition, and supplier sourcing.
"""

import logging
from typing import Any

from app.agents.visual_insight.state import VisualAgentState
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

logger = logging.getLogger("aveline.agent.visual")


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

    async def run(self, state: dict[str, Any]) -> dict[str, Any]:
        """Execute business behavior for the agent based on intent."""
        intent_type = state.get("intent_type")
        if intent_type == "item_search":
            return await self._handle_item_search(state)
        if intent_type == "image_analysis":
            return await self._handle_image_analysis(state)
        if intent_type == "outfit_composition":
            return await self._handle_outfit_composition(state)

        return {"status": "unsupported_intent", "found_items": []}

    async def _handle_outfit_composition(self, state: dict[str, Any]) -> dict[str, Any]:
        """Handle outfit composition intent."""
        org_id = state.get("org_id", "")
        customer_id = state.get("customer_id", "")
        parsed_intent = state.get("parsed_intent", {})
        occasion = parsed_intent.get("occasion", "")
        budget = parsed_intent.get("budget")

        if self.tools and hasattr(self.tools, "compose_outfit"):
            outfit = await self.tools.compose_outfit(org_id, customer_id, occasion)
            if isinstance(outfit, dict) and "items" in outfit:
                # Rule 1: Never Recommend Out-of-Stock Products
                in_stock_items = [
                    item for item in outfit["items"]
                    if item.get("stock") is None or item.get("stock", 0) > 0
                ]

                # Rule 4: Respect Budget
                if budget is not None and budget > 0:
                    budget_items = []
                    current_total = 0
                    for item in in_stock_items:
                        item_price = item.get("price", 0) or 0
                        if current_total + item_price <= budget:
                            budget_items.append(item)
                            current_total += item_price
                    in_stock_items = budget_items
                    outfit["total_price"] = current_total
                else:
                    outfit["total_price"] = sum((item.get("price", 0) or 0) for item in in_stock_items)

                outfit["items"] = in_stock_items

            return {
                "status": "success",
                "outfit_proposal": outfit,
            }

        return {"status": "error", "outfit_proposal": None}

    async def _handle_image_analysis(self, state: dict[str, Any]) -> dict[str, Any]:
        """Handle image analysis intent."""
        org_id = state.get("org_id", "")
        image_url = state.get("image_url")
        item_id = state.get("item_id")
        prompt = state.get("prompt")

        if not image_url:
            return {
                "status": "error",
                "message": "No image URL provided",
            }

        if self.tools and hasattr(self.tools, "analyze_product_image"):
            attrs = await self.tools.analyze_product_image(org_id, image_url, prompt=prompt)

            customer_matches = None
            if item_id and hasattr(self.tools, "generate_customer_matches"):
                customer_matches = await self.tools.generate_customer_matches(item_id, org_id)

            res = {
                "status": "success",
                "analyzed_image": attrs,
            }
            if customer_matches is not None:
                res["customer_matches"] = customer_matches
            return res

        return {"status": "error", "analyzed_image": None}

    async def _handle_item_search(self, state: dict[str, Any]) -> dict[str, Any]:
        """Handle item search intent."""
        org_id = state.get("org_id", "")
        customer_id = state.get("customer_id")
        parsed_intent = state.get("parsed_intent", {})

        if self.tools and hasattr(self.tools, "search_inventory"):
            raw_items = await self.tools.search_inventory(org_id, parsed_intent)

            # Deterministic business rule validation
            valid_items = []
            for item in (raw_items or []):
                # Rule 5: Organization Isolation
                if "org_id" in item and item["org_id"] != org_id:
                    continue
                # Rule 2: Always Include Price
                price = item.get("price")
                if price is None or not isinstance(price, (int, float)) or price < 0:
                    continue
                # Rule 1: Never recommend out-of-stock products
                stock = item.get("stock")
                if stock is not None and stock <= 0:
                    continue
                valid_items.append(item)

            if valid_items:
                return {
                    "status": "success",
                    "found_items": valid_items,
                }

            ref_image = parsed_intent.get("reference_image_url")
            if ref_image and hasattr(self.tools, "create_sourcing_request"):
                sourcing_data = {
                    "customerId": customer_id,
                    "imageUrl": ref_image,
                    "notes": parsed_intent.get("notes", "Auto-initiated sourcing request from customer visual search"),
                }
                sourcing_suggestion = await self.tools.create_sourcing_request(org_id, sourcing_data)
                return {
                    "status": "success",
                    "found_items": [],
                    "sourcing_suggestion": sourcing_suggestion,
                    "action_required": "review_sourcing_request",
                }

            return {
                "status": "no_results",
                "found_items": valid_items,
            }

        return {"status": "error", "found_items": []}

    async def parse_visual_intent(self, state: VisualAgentState) -> dict[str, Any]:
        """Extract visual search hints and occasion parameters from customer input."""
        msg = state.get("message", "").lower()
        org_id = state.get("org_id", "")

        # Extract occasion keywords
        occasion = None
        for occ in ["wedding", "gala", "cocktail", "dinner", "reception", "beach", "party", "office"]:
            if occ in msg:
                occasion = occ.capitalize()
                break

        # Extract color hints
        color = None
        for col in ["peach", "gold", "navy", "black", "white", "ivory", "red", "emerald", "silk", "linen"]:
            if col in msg:
                color = col.capitalize()
                break

        criteria = {
            "organizationId": org_id,
            "query": state.get("message", ""),
            "occasion": occasion,
            "color": color,
        }

        return {"search_criteria": criteria}

    async def analyze_image(self, state: VisualAgentState) -> dict[str, Any]:
        """Call vision analysis on provided image URL or mood board."""
        image_url = state.get("image_url")
        if not image_url:
            return {"image_attributes": None}

        try:
            attrs: ImageAttributes = await analyze_product_image(self._registry, image_url)
            criteria = state.get("search_criteria") or {}
            criteria["category"] = attrs.category
            criteria["color"] = attrs.primary_color
            if attrs.occasion and not criteria.get("occasion"):
                criteria["occasion"] = attrs.occasion

            return {
                "image_attributes": attrs.model_dump(),
                "search_criteria": criteria,
            }
        except Exception as e:
            logger.warning("Image analysis failed: %s", e)
            return {"image_attributes": None}

    async def search_inventory(self, state: VisualAgentState) -> dict[str, Any]:
        """Query boutique inventory using compiled criteria."""
        criteria = state.get("search_criteria") or {
            "organizationId": state.get("org_id", ""),
            "query": state.get("message", ""),
        }

        try:
            items: list[PieceItem] = await search_inventory(self._registry, criteria)
            return {"matched_items": [item.model_dump() for item in items]}
        except Exception as e:
            logger.warning("Inventory search failed: %s", e)
            return {"matched_items": []}

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
                    system_prompt = assemble_system_prompt("visual", {"organization_id": state.get("org_id")})
                    user_msg = (
                        f"Curate a look for occasion '{occasion}' featuring: "
                        + ", ".join(f"{i.name} ({i.category or 'garment'}, {i.color or 'color'})" for i in items)
                    )
                    llm_res = await self.llm.ainvoke([("system", system_prompt), ("human", user_msg)])
                    if hasattr(llm_res, "content") and llm_res.content:
                        look.text = str(llm_res.content).strip()
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
                suggestion = f"Elle curated {len(items)} piece(s) harmonizing with your aesthetic, including the {name}."
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
        """Initiate supplier sourcing when in-stock pieces are unavailable."""
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

        if staff_query:
            summary_text = "No in-stock pieces matched. Sourcing request created with partner ateliers."
            return {
                "sourcing_request": req.model_dump(),
                "suggestion": None,
                "summary": summary_text,
                "text": summary_text,
                "status": "pending",
            }
        else:
            suggestion = (
                "We do not have this exact piece in stock right now, but Elle has initiated a custom "
                "sourcing request with our partner ateliers."
            )
            return {
                "sourcing_request": req.model_dump(),
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
            "image_attributes": image_attrs,
            "reason": state.get("reason"),
        })

        # ADR-010 Usage Reporting (best-effort)
        prompt_tokens = state.get("prompt_tokens") or 0
        completion_tokens = state.get("completion_tokens") or 0
        if (prompt_tokens + completion_tokens) > 0 and state.get("org_id"):
            try:
                await report_usage(
                    organization_id=str(state["org_id"]),
                    request_id=str(state.get("customer_id") or "visual-req"),
                    workflow_id="visual_insight",
                    provider="openai",
                    model="visual-llm",
                    input_tokens=prompt_tokens,
                    output_tokens=completion_tokens,
                )
            except Exception as exc:
                logger.warning("Usage reporting failed (non-fatal): %s", exc)

        return {"output": output.model_dump()}

