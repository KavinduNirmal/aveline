"""Inventory search and stock verification tools for Visual Insight Agent (Elle)."""

import logging
from typing import Any

from app.schemas.visual_insight import PieceItem

logger = logging.getLogger("aveline.agent.tools.inventory")


def dict_to_piece_item(item_data: dict[str, Any]) -> PieceItem:
    """Safely map raw item dictionary to PieceItem.

    The internal inventory contract is camelCase and uses ``itemName``/``quantity``/``imageUrl``,
    while older shapes used ``name``/``stock``. Both are accepted, so a real match cannot be
    silently reduced to a defaulted placeholder.
    """
    item_id = str(item_data.get("itemId") or item_data.get("id") or item_data.get("item_id") or "item-001")
    name = str(
        item_data.get("itemName")
        or item_data.get("name")
        or item_data.get("title")
        or "Boutique Piece"
    )
    price = item_data.get("price")
    if price is not None:
        try:
            price = float(price)
        except (ValueError, TypeError):
            price = None

    stock = item_data.get("stock")
    if stock is None:
        stock = item_data.get("quantity")
    if stock is not None:
        try:
            stock = int(stock)
        except (ValueError, TypeError):
            stock = None

    # `sizes` is a JSON array on the wire but a single string on the model; join rather than drop,
    # so a size match can still be reasoned about downstream.
    size = item_data.get("size")
    if size is None:
        sizes = item_data.get("sizes")
        if isinstance(sizes, (list, tuple)) and sizes:
            size = ", ".join(str(s) for s in sizes)

    return PieceItem(
        itemId=item_id,
        name=name,
        price=price,
        size=size,
        stock=stock,
        imageUrl=item_data.get("imageUrl") or item_data.get("image_url"),
        category=item_data.get("category"),
        color=item_data.get("color") or item_data.get("primary_color"),
        description=item_data.get("description"),
    )


#: Field names that identify a dict as a single inventory item rather than an envelope. Without
#: this check a malformed payload (``{}``, ``{"items": "not-a-list"}``) yields one junk item, which
#: would surface to a customer as a piece literally named "Boutique Piece".
_ITEM_FIELDS = (
    "itemId", "id", "item_id", "itemName", "name", "title", "category", "color", "price",
    "quantity", "stock", "sizes", "size",
)


def unwrap_items(res: Any) -> list[dict[str, Any]]:
    """Normalize an inventory response into a list of raw items.

    The internal endpoint returns a bare JSON **array**, while other shapes wrap it in
    ``items``/``results``. Calling ``.get`` on the bare array raised ``AttributeError``, which the
    caller's broad ``except`` converted into "no stock found" - so a real, in-stock piece became an
    out-of-stock sourcing request. Both shapes are handled explicitly for that reason.
    """
    if isinstance(res, list):
        return [item for item in res if isinstance(item, dict)]
    if isinstance(res, dict):
        for key in ("items", "results", "data"):
            candidate = res.get(key)
            if isinstance(candidate, list):
                return [item for item in candidate if isinstance(item, dict)]
        # A lone item object, but only when it actually carries item fields.
        if any(field in res for field in _ITEM_FIELDS):
            return [res]
    return []


async def search_inventory(
    registry: Any,
    criteria: dict[str, Any],
) -> list[PieceItem]:
    """Search boutique inventory by structured criteria (category, color, query, size)."""
    try:
        res = await registry.search_inventory(criteria=criteria)
    except Exception:
        # Surfaced rather than swallowed. An empty result and a failed search mean very different
        # things downstream, because the caller's sourcing path reads "empty" as "not in stock".
        logger.exception("Inventory search request failed for criteria %s", criteria)
        raise

    return [dict_to_piece_item(item) for item in unwrap_items(res)]


async def check_stock(
    registry: Any,
    item_id: str,
    org_id: str | None = None,
) -> dict[str, Any]:
    """Check stock level and available sizes for an inventory item."""
    try:
        if hasattr(registry, "check_stock"):
            return await registry.check_stock(item_id=item_id, org_id=org_id)
        # Fallback to general inventory search
        items = await search_inventory(registry, {"itemId": item_id, "organizationId": org_id})
        if items:
            return {"itemId": items[0].itemId, "stock": items[0].stock or 0, "inStock": (items[0].stock or 0) > 0}
        return {"itemId": item_id, "stock": 0, "inStock": False}
    except Exception:
        return {"itemId": item_id, "stock": 0, "inStock": False}
