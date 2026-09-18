"""Customer-to-item matching tools for Visual Insight Agent (Elle).

Finds clients whose recorded aesthetics, size preferences, and purchase histories
harmonize with newly arrived garments.
"""

from typing import Any


async def match_customers_to_item(
    registry: Any,
    item_id: str,
    org_id: str | None = None,
) -> list[dict[str, Any]]:
    """Query customers whose preferences match an inventory piece."""
    try:
        res = await registry.match_customers_to_item(item_id=item_id)
        return res.get("matches") or res.get("customers") or (res if isinstance(res, list) else [])
    except Exception:
        return []
