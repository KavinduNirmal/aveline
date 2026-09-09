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

    async def identify_customer(self, org_id: str, phone_number: str, full_name: str | None = None) -> dict[str, Any]:
        """Look up a customer by phone number, creating a 'new' profile when absent."""
        body: dict[str, Any] = {"organizationId": org_id, "phoneNumber": phone_number}
        if full_name:
            body["fullName"] = full_name
        return await self._client.request("POST", "/internal/customers/identify", json=body)

    async def search_customer_profile(self, org_id: str, customer_id: str) -> dict[str, Any]:
        """Fetch a customer record plus preferences from the backend."""
        return await self._client.request(
            "GET", f"/internal/customers/{customer_id}/profile?organizationId={org_id}"
        )

    async def get_customer_memories(
        self,
        org_id: str,
        customer_id: str,
        query: str,
        top_k: int = 5,
    ) -> dict[str, Any]:
        """Semantic search over a customer's memories (pgvector via backend)."""
        return await self._client.request(
            "POST",
            "/internal/customers/memories/search",
            json={"organizationId": org_id, "customerId": customer_id, "query": query, "topK": top_k},
        )

    async def save_customer_memory(
        self,
        org_id: str,
        customer_id: str,
        content: str,
        category: str,
    ) -> dict[str, Any]:
        """Persist a new customer memory via the backend."""
        return await self._client.request(
            "POST",
            f"/internal/customers/{customer_id}/memories",
            json={"organizationId": org_id, "content": content, "category": category},
        )

    async def generate_interaction_brief(self, org_id: str, customer_id: str) -> dict[str, Any]:
        """Generate a staff-facing interaction brief for a customer."""
        return await self._client.request(
            "GET", f"/internal/customers/{customer_id}/brief?organizationId={org_id}"
        )

    async def record_customer_interaction(
        self,
        org_id: str,
        customer_id: str,
        channel: str,
        direction: str,
        message_content: str,
    ) -> dict[str, Any]:
        """Record a customer interaction via the backend."""
        return await self._client.request(
            "POST",
            f"/internal/customers/{customer_id}/interactions",
            json={
                "organizationId": org_id,
                "channel": channel,
                "direction": direction,
                "messageContent": message_content,
            },
        )

    async def get_customer_consent(self, org_id: str, customer_id: str) -> dict[str, Any]:
        """Check a customer's consent status via the backend."""
        return await self._client.request(
            "GET", f"/internal/customers/{customer_id}/consent?organizationId={org_id}"
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

    async def check_stock(self, item_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Check stock availability for an inventory item."""
        url = f"/api/internal/inventory/{item_id}/stock"
        if org_id:
            url += f"?organizationId={org_id}"
        return await self._client.request("GET", url)

    async def create_sourcing_request(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Submit a sourcing request for unavailable pieces."""
        return await self._client.request("POST", "/api/internal/sourcing/requests", json=payload)

    async def search_supplier_catalog(self, query: str, org_id: str | None = None) -> dict[str, Any]:
        """Query supplier catalogs for material/garment sourcing."""
        return await self._client.request(
            "POST", "/api/internal/suppliers/search", json={"query": query, "organizationId": org_id}
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
