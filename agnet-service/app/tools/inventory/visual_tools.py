"""Visual tools adapter for ASP.NET Core API."""

import logging
from typing import Any
import httpx

from app.core.config import get_settings

logger = logging.getLogger("aveline.agent.tools.visual")


class VisualTools:
    """HTTP client adapter calling the ASP.NET Core backend for visual and inventory operations."""

    def __init__(
        self,
        api_base_url: str | None = None,
        internal_api_key: str | None = None,
        timeout: float = 30.0,
    ) -> None:
        try:
            settings = get_settings()
            default_base_url = settings.api_base_url
            default_token = settings.internal_api_token
        except Exception:
            default_base_url = "http://localhost:5000"
            default_token = "default-internal-token"

        self.api_base_url = (api_base_url or default_base_url).rstrip("/")
        self.internal_api_key = internal_api_key or default_token
        self.timeout = timeout

    async def get_item(
        self,
        item_id: str,
        org_id: str,
    ) -> dict[str, Any] | None:
        """Retrieve a specific inventory item by ID. Returns None if 404 Not Found."""
        url = f"{self.api_base_url}/api/internal/visual/inventory/{item_id}"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        params = {"orgId": org_id}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.get(url, params=params, headers=headers)
            if response.status_code == 404:
                return None
            response.raise_for_status()
            return response.json()

    async def search_inventory(
        self,
        org_id: str,
        criteria: dict[str, Any],
    ) -> list[dict[str, Any]]:
        """Query internal inventory search endpoint."""
        url = f"{self.api_base_url}/api/internal/visual/search-inventory"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, **criteria}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            data = response.json()
            if isinstance(data, dict):
                return data.get("results") or data.get("items") or []
            if isinstance(data, list):
                return data
            return []

    async def create_inventory_item(
        self,
        org_id: str,
        item_data: dict[str, Any],
    ) -> dict[str, Any]:
        """Create a new inventory item via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/inventory"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, **item_data}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            return response.json()

    async def update_inventory_item(
        self,
        item_id: str,
        org_id: str,
        item_data: dict[str, Any],
    ) -> dict[str, Any]:
        """Update an existing inventory item via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/inventory/{item_id}"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, **item_data}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.put(url, json=payload, headers=headers)
            response.raise_for_status()
            return response.json()

    async def update_inventory_status(
        self,
        item_id: str,
        org_id: str,
        status: str,
    ) -> dict[str, Any]:
        """Update inventory item status via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/inventory/{item_id}/status"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, "status": status}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.patch(url, json=payload, headers=headers)
            response.raise_for_status()
            return response.json()

    async def get_low_stock_items(
        self,
        org_id: str,
        threshold: int = 5,
    ) -> list[dict[str, Any]]:
        """Retrieve low stock items via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/inventory/low-stock"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        params = {"orgId": org_id, "threshold": threshold}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.get(url, params=params, headers=headers)
            response.raise_for_status()
            data = response.json()
            if isinstance(data, list):
                return data
            return []

    async def analyze_product_image(
        self,
        org_id: str,
        image_url: str,
        prompt: str | None = None,
    ) -> dict[str, Any]:
        """Perform visual image feature extraction via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/analyze-image"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, "imageUrl": image_url, "prompt": prompt}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            return response.json()

    async def analyze_image(
        self,
        org_id: str,
        image_url: str,
        prompt: str | None = None,
    ) -> dict[str, Any]:
        """Alias for analyze_product_image."""
        return await self.analyze_product_image(org_id, image_url, prompt=prompt)

    async def get_customer_matches(
        self,
        item_id: str,
        org_id: str,
        min_score: float = 0.7,
    ) -> list[dict[str, Any]]:
        """Retrieve customer matches for an inventory item via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/customer-matches/{item_id}"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        params = {"orgId": org_id, "minScore": min_score}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.get(url, params=params, headers=headers)
            response.raise_for_status()
            data = response.json()
            if isinstance(data, list):
                return data
            return []

    async def generate_customer_matches(
        self,
        item_id: str,
        org_id: str,
        max_matches: int = 10,
    ) -> list[dict[str, Any]]:
        """Trigger customer matching generation for an inventory item via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/customer-matches/{item_id}/generate"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, "maxMatches": max_matches}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            data = response.json()
            if isinstance(data, list):
                return data
            return []

    async def compose_outfit(
        self,
        org_id: str,
        primary_item_id: str,
        occasion: str | None = None,
        customer_id: str | None = None,
        style: str | None = None,
    ) -> dict[str, Any]:
        """Compose coordinated outfit looks via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/outfits/compose"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {
            "orgId": org_id,
            "primaryItemId": primary_item_id,
            "occasion": occasion,
            "customerId": customer_id,
            "style": style,
        }

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            return response.json()

    async def create_sourcing_request(
        self,
        org_id: str,
        sourcing_data: dict[str, Any],
    ) -> dict[str, Any]:
        """Create a sourcing request via internal API."""
        url = f"{self.api_base_url}/api/internal/visual/sourcing-requests"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        payload = {"orgId": org_id, **sourcing_data}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            return response.json()

    async def search_supplier_catalog(
        self,
        supplier_id: str,
        org_id: str,
        query: str | None = None,
        category: str | None = None,
        color: str | None = None,
        max_price: float | None = None,
    ) -> list[dict[str, Any]]:
        """Browse supplier catalog via internal API with optional query/filters."""
        url = f"{self.api_base_url}/api/internal/visual/suppliers/{supplier_id}/catalog"
        headers = {
            "X-Internal-Key": self.internal_api_key or "",
            "X-Internal-Token": self.internal_api_key or "",
        }
        params: dict[str, Any] = {"orgId": org_id}
        if category:
            params["category"] = category
        if color:
            params["color"] = color
        if query:
            params["query"] = query
        if max_price is not None:
            params["maxPrice"] = max_price

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.get(url, params=params, headers=headers)
            response.raise_for_status()
            data = response.json()
            if isinstance(data, list):
                return data
            return []

    async def get_supplier_catalog(
        self,
        supplier_id: str,
        org_id: str,
        category: str | None = None,
        color: str | None = None,
        max_price: float | None = None,
    ) -> list[dict[str, Any]]:
        """Alias for search_supplier_catalog."""
        return await self.search_supplier_catalog(
            supplier_id=supplier_id,
            org_id=org_id,
            category=category,
            color=color,
            max_price=max_price,
        )
