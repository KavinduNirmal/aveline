"""External supplier catalog tools for Visual Insight Agent (Elle)."""

from typing import Any


async def search_supplier_catalog(
    registry: Any,
    query: str,
    org_id: str | None = None,
) -> list[dict[str, Any]]:
    """Query partner ateliers and supplier catalogs for matching fabrics or garments."""
    try:
        if hasattr(registry, "search_supplier_catalog"):
            res = await registry.search_supplier_catalog(query=query, org_id=org_id)
            return res.get("items") or (res if isinstance(res, list) else [])
        return []
    except Exception:
        return []
