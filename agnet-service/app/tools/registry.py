"""ToolRegistry — thin, typed wrappers over backend internal endpoints.

Each method is a **thin wrapper**: it validates its inputs and forwards the call
to the ASP.NET Core backend via ``InternalApiClient``. No business logic lives
here. The backend internal controllers are a cross-slice dependency and are
implemented by the slice owners; these stubs define the shared contract.

The registry is shared infrastructure. Slice owners may add agent-specific tools
in ``app/tools/<slice>/`` but should reuse ``InternalApiClient`` for transport.
"""

import logging
from typing import Any

from app.tools.client import InternalApiClient

logger = logging.getLogger("aveline.agent.tools.registry")


class ToolRegistry:
    """Authenticated client for the backend internal API, grouped by domain."""

    def __init__(self, client: InternalApiClient | None = None) -> None:
        self._client = client or InternalApiClient()

    # ============================== MEMORY AGENT ==============================

    async def search_customer_profile(self, customer_id: str) -> dict[str, Any]:
        """Fetch a customer record plus preferences from the backend."""
        return await self._client.request("GET", f"/api/internal/customers/{customer_id}")

    async def get_customer_memories(
        self,
        customer_id: str,
        query: str,
        top_k: int = 5,
    ) -> dict[str, Any]:
        """Semantic search over a customer's memories (pgvector via backend)."""
        return await self._client.request(
            "POST",
            "/api/internal/vector-search",
            json={"customer_id": customer_id, "query": query, "top_k": top_k},
        )

    async def save_customer_memory(
        self,
        customer_id: str,
        content: str,
        category: str,
    ) -> dict[str, Any]:
        """Persist a new customer memory via the backend."""
        return await self._client.request(
            "POST",
            "/api/internal/customer-memory",
            json={"customer_id": customer_id, "content": content, "category": category},
        )

    async def generate_interaction_brief(self, customer_id: str) -> dict[str, Any]:
        """Generate a staff-facing interaction brief for a customer."""
        return await self._client.request(
            "GET", f"/api/internal/customers/{customer_id}/brief"
        )

    # ============================== VISUAL AGENT ==============================

    async def search_inventory(self, criteria: dict[str, Any]) -> dict[str, Any]:
        """Search inventory by structured criteria via the backend."""
        return await self._client.request("POST", "/api/internal/inventory/search", json=criteria)

    async def analyze_product_image(self, image_url: str) -> dict[str, Any]:
        """Analyze a product image and return extracted attributes."""
        return await self._client.request(
            "POST", "/api/internal/analyze-image", json={"image_url": image_url}
        )

    async def match_customers_to_item(self, item_id: str) -> dict[str, Any]:
        """Find customers whose preferences match an item."""
        return await self._client.request(
            "GET", f"/api/internal/inventory/{item_id}/matches"
        )

    # ============================== COMMERCE AGENT ==============================

    async def calculate_margin(self, order_id: str) -> dict[str, Any]:
        """Compute margin for an order via the backend."""
        return await self._client.request(
            "POST", f"/api/internal/orders/{order_id}/calculate-margin"
        )

    async def generate_payment_request(self, order_id: str, amount: float) -> dict[str, Any]:
        """Generate a payment request for an order."""
        return await self._client.request(
            "POST",
            f"/api/internal/orders/{order_id}/payment-request",
            json={"amount": amount},
        )

    async def check_approval_threshold(self, order_id: str) -> dict[str, Any]:
        """Evaluate an order against approval thresholds."""
        return await self._client.request(
            "GET", f"/api/internal/orders/{order_id}/approval-check"
        )
