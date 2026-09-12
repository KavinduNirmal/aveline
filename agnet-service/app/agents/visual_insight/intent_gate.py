"""Visual Intent Gate for deterministic visual intent routing and classification."""

import logging
from typing import Any

logger = logging.getLogger("aveline.agent.visual.gate")


class VisualIntentGate:
    """Classifies user queries into Visual Intelligence intents."""

    _IMAGE_KEYWORDS = ("analyze", "photo", "image", "picture", "scan", "look at", "photograph")
    _OUTFIT_KEYWORDS = ("build", "outfit", "compose", "ensemble", "look", "coordinate", "pair", "styling", "style")
    _ITEM_KEYWORDS = ("find", "search", "show", "saree", "dress", "gown", "blouse", "item", "stock", "inventory", "have")

    def infer(self, message: str, context: dict[str, Any] | None = None) -> dict[str, Any]:
        """Infer visual intent type from message and context."""
        ctx = context or {}
        msg = (message or "").lower()

        # Contextual triggers: presence of reference image
        if ctx.get("has_reference_image") or ctx.get("image_url") or ctx.get("reference_image_url"):
            return {
                "intent_type": "image_analysis",
                "message": message,
                "context": ctx,
            }

        # Check explicit image analysis keywords
        if any(kw in msg for kw in self._IMAGE_KEYWORDS):
            return {
                "intent_type": "image_analysis",
                "message": message,
                "context": ctx,
            }

        # Check outfit composition keywords
        if any(kw in msg for kw in self._OUTFIT_KEYWORDS):
            return {
                "intent_type": "outfit_composition",
                "message": message,
                "context": ctx,
            }

        # Check item search keywords
        if any(kw in msg for kw in self._ITEM_KEYWORDS):
            return {
                "intent_type": "item_search",
                "message": message,
                "context": ctx,
            }

        # Default fallback
        return {
            "intent_type": "item_search",
            "message": message,
            "context": ctx,
        }
