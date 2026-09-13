"""Inventory search and stock verification tools for Visual Insight Agent (Elle)."""

from typing import Any

from app.schemas.visual_insight import PieceItem


def dict_to_piece_item(item_data: dict[str, Any]) -> PieceItem:
    """Safely map raw item dictionary to PieceItem."""
    item_id = str(item_data.get("itemId") or item_data.get("id") or item_data.get("item_id") or "item-001")
    name = str(item_data.get("name") or item_data.get("title") or "Boutique Piece")
    price = item_data.get("price")
    if price is not None:
        try:
            price = float(price)
        except (ValueError, TypeError):
            price = None

    stock = item_data.get("stock") or item_data.get("quantity")
    if stock is not None:
        try:
            stock = int(stock)
        except (ValueError, TypeError):
            stock = None

    return PieceItem(
        itemId=item_id,
        name=name,
        price=price,
        size=item_data.get("size"),
        stock=stock,
        imageUrl=item_data.get("imageUrl") or item_data.get("image_url"),
        category=item_data.get("category"),
        color=item_data.get("color") or item_data.get("primary_color"),
        description=item_data.get("description"),
    )


async def search_inventory(
    registry: Any,
    criteria: dict[str, Any],
) -> list[PieceItem]:
    """Search boutique inventory by structured criteria (category, color, query, size)."""
    try:
        res = await registry.search_inventory(criteria=criteria)
        raw_items = res.get("items") or res.get("results") or (res if isinstance(res, list) else [])
        return [dict_to_piece_item(item) for item in raw_items]
    except Exception:
        return []


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
