"""Customer loyalty tier and discount eligibility tools (Slice 3)."""

import logging
from typing import Any

logger = logging.getLogger("aveline.agent.tools.commerce.loyalty")

# Standard discount caps by tier
TIER_DISCOUNT_CAPS = {
    "VIP": 0.10,       # 10% standard auto-approved discount
    "Level 3": 0.07,   # 7% standard auto-approved discount
    "Level 2": 0.05,   # 5% standard auto-approved discount
    "Level 1": 0.03,   # 3% standard auto-approved discount
    "Regular": 0.05,   # 5% standard auto-approved discount (legacy alias)
    "New": 0.00,       # 0% standard discount
}


async def get_customer_loyalty_tier(
    org_id: str,
    customer_id: str | None,
    customer_name: str | None = None,
    registry: Any = None,
) -> dict[str, Any]:
    """Look up customer profile and resolve their loyalty tier and standard discount cap.

    Args:
        org_id: Organization/Tenant ID.
        customer_id: Customer UUID if known.
        customer_name: Customer name if known (fallback lookup).
        registry: ToolRegistry client for internal API.

    Returns:
        Dictionary with tier name (VIP, Level 3, Level 2, Level 1, New) and max_allowed_discount.
    """
    if not customer_id and not customer_name:
        return {"tier": "New", "max_allowed_discount": 0.0, "vip": False}

    if registry:
        try:
            profile = None
            if customer_id:
                profile = await registry.search_customer_profile(org_id, customer_id)
            elif customer_name and hasattr(registry, "lookup_customers"):
                matches = await registry.lookup_customers(org_id, name=customer_name)
                if matches and isinstance(matches, list) and len(matches) > 0:
                    profile = matches[0]

            if profile:
                level = (profile.get("level") or "").strip().lower()
                status = (profile.get("status") or "").upper()
                tags = [str(t).upper() for t in (profile.get("tags") or [])]
                if level == "vip" or "VIP" in status or profile.get("isVip") or "VIP" in tags:
                    return {"tier": "VIP", "max_allowed_discount": 0.10, "vip": True}
                if level in ("level3", "level 3") or "LEVEL 3" in status or "LEVEL 3" in tags:
                    return {"tier": "Level 3", "max_allowed_discount": 0.07, "vip": False}
                if level in ("level2", "level 2") or "LEVEL 2" in status or "LEVEL 2" in tags:
                    return {"tier": "Level 2", "max_allowed_discount": 0.05, "vip": False}
                if level in ("level1", "level 1") or "LEVEL 1" in status or "LEVEL 1" in tags:
                    return {"tier": "Level 1", "max_allowed_discount": 0.03, "vip": False}
                if "REGULAR" in status or "RETURNING" in status:
                    return {"tier": "Level 2", "max_allowed_discount": 0.05, "vip": False}
        except Exception as ex:
            logger.warning("Failed to fetch customer profile for loyalty tier: %s", ex)

    # Default fallback when unclassified
    return {"tier": "Level 2", "max_allowed_discount": 0.05, "vip": False}

