"""Courier dispatch and delivery route booking tools (Slice 3)."""

import logging
from typing import Any

logger = logging.getLogger("aveline.agent.tools.commerce.delivery")


async def book_courier(
    org_id: str,
    order_id: str | None,
    delivery_address: str | None,
    carrier: str = "PickMe",
    registry: Any = None,
) -> dict[str, Any]:
    """Calculate delivery fee and record delivery dispatch plan.

    Args:
        org_id: Organization/Tenant ID.
        order_id: Order UUID if known.
        delivery_address: Customer physical delivery address.
        carrier: Preferred courier service (PickMe, Uber, In-house).
        registry: ToolRegistry client.

    Returns:
        Dictionary with carrier, status, estimated_fee, and delivery_address.
    """
    if not delivery_address:
        return {
            "carrier": None,
            "status": "skipped",
            "delivery_address": None,
            "estimated_fee": 0.0,
            "tracking_number": None,
        }

    # Standard delivery rate card: Colombo local is LKR 500-650
    estimated_fee = 650.0 if "colombo" in delivery_address.lower() else 850.0
    ref_id = (order_id or "pkg-sample")[-6:]

    return {
        "carrier": carrier,
        "status": "planned",
        "delivery_address": delivery_address,
        "estimated_fee": estimated_fee,
        "tracking_number": f"TRK-{carrier.upper()[:2]}-{ref_id}",
    }
