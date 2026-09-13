"""Customer loyalty tier and discount eligibility tools (Slice 3)."""

import logging
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from app.tools.registry import ToolRegistry

logger = logging.getLogger("aveline.agent.tools.commerce.loyalty")

# Standard discount caps by tier
TIER_DISCOUNT_CAPS = {
    "VIP": 0.10,       # 10% maximum auto-approved discount
    "Regular": 0.05,   # 5% maximum auto-approved discount
    "New": 0.00,       # 0% standard discount
}


async def get_customer_loyalty_tier(
    org_id: str,
    customer_id: str | None,
    registry: Any = None,
) -> dict[str, Any]:
    """Look up customer profile and resolve their loyalty tier and standard discount cap.

    Args:
        org_id: Organization/Tenant ID.
        customer_id: Customer UUID if known.
        registry: ToolRegistry client for internal API.

    Returns:
        Dictionary with tier name (VIP, Regular, New) and max_allowed_discount.
    """
    if not customer_id:
        return {"tier": "New", "max_allowed_discount": 0.0, "vip": False}

    if registry:
        try:
            profile = await registry.search_customer_profile(org_id, customer_id)
            if profile:
                status = (profile.get("status") or "").upper()
                if "VIP" in status or profile.get("isVip"):
                    return {"tier": "VIP", "max_allowed_discount": 0.10, "vip": True}
                if "REGULAR" in status:
                    return {"tier": "Regular", "max_allowed_discount": 0.05, "vip": False}
        except Exception as ex:
            logger.warning("Failed to fetch customer profile for loyalty tier: %s", ex)

    # Default fallback when unclassified
    return {"tier": "Regular", "max_allowed_discount": 0.05, "vip": False}
