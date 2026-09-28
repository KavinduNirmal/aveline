"""ToolRegistry — thin, typed wrappers over backend internal endpoints.

Each method is a **thin wrapper**: it validates its inputs and forwards the call
to the ASP.NET Core backend via ``InternalApiClient``. No business logic lives
here. The backend internal controllers are a cross-slice dependency and are
implemented by the slice owners; these stubs define the shared contract.

The registry is shared infrastructure. Slice owners may add agent-specific tools
in ``app/tools/<slice>/`` but should reuse ``InternalApiClient`` for transport.
"""

import functools
import inspect
import logging
from typing import Any

from app.observability.metrics import get_agent_metrics
from app.telemetry.agent_telemetry import get_current_collector
from app.tools.client import InternalApiClient

logger = logging.getLogger("aveline.agent.tools.registry")

#: The retrieval mode the agent asks for by default. Customer-memory search fuses a dense pgvector
#: leg with a PostgreSQL full-text leg (the pattern ADR-025 established for the handbook); the
#: backend applies the same default, so this is stated here rather than left implicit in the wire
#: payload. Single-leg modes are for the retrieval evaluation, not for the agent's own reads.
MEMORY_SEARCH_MODE = "hybrid"


def _instrument_tool(tool_name: str, tool_fn: Any) -> Any:
    """Wrap one registry tool so a tool call is both a step row and a metric (G-6)."""

    @functools.wraps(tool_fn)
    async def wrapper(self: Any, *args: Any, **kwargs: Any) -> Any:
        collector = get_current_collector()
        if collector is not None:
            collector.start_step(tool_name, step_kind="ToolCall", tool_name=tool_name)
        status = "Succeeded"
        try:
            return await tool_fn(self, *args, **kwargs)
        except Exception:
            status = "Failed"
            raise
        finally:
            if collector is not None:
                collector.complete_current_step(status=status)
            get_agent_metrics().record_tool_call(tool=tool_name, status=status)

    return wrapper


def _instrumented_registry(cls: Any) -> Any:
    """Instrument every public coroutine on the registry (the tools are a bounded set)."""
    for attr_name, attr in list(vars(cls).items()):
        if attr_name.startswith("_") or not inspect.iscoroutinefunction(attr):
            continue
        setattr(cls, attr_name, _instrument_tool(attr_name, attr))
    return cls


@_instrumented_registry
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

    async def update_customer(
        self,
        org_id: str,
        customer_id: str,
        full_name: str | None = None,
        phone_number: str | None = None,
    ) -> dict[str, Any]:
        """Apply an explicit staff instruction to a customer's name or phone (ADR-023 follow-up).

        Only the fields actually supplied are sent, so an update that mentions a name cannot blank
        out the phone. This never creates a customer: naming someone who is not on file is a
        different operation, and silently creating a record from a chat message would be wrong.

        Raises:
            httpx.HTTPStatusError: 409 when another customer already holds the phone number, 404
                when the customer is not in the organization. Both must surface to staff rather
                than be swallowed - an update that silently did nothing is worse than an error.
        """
        body: dict[str, Any] = {}
        if full_name is not None:
            body["fullName"] = full_name
        if phone_number is not None:
            body["phoneNumber"] = phone_number

        return await self._client.request(
            "PATCH",
            f"/internal/customers/{customer_id}",
            json=body,
            params={"organizationId": org_id},
        )

    async def lookup_customers(
        self,
        org_id: str,
        name: str | None = None,
        phone: str | None = None,
        email: str | None = None,
    ) -> dict[str, Any]:
        """Read-only customer lookup by name, phone and/or email (no auto-create).

        Returns the backend ``{matches, isExact, total}`` envelope. Used by the shared
        customer-resolution step so any specialist can resolve a customer from free text.
        """
        body: dict[str, Any] = {"organizationId": org_id}
        if name:
            body["name"] = name
        if phone:
            body["phoneNumber"] = phone
        if email:
            body["email"] = email
        return await self._client.request("POST", "/internal/customers/lookup", json=body)

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
        min_similarity: float = 0.0,
        mode: str = MEMORY_SEARCH_MODE,
    ) -> dict[str, Any]:
        """Search a customer's memories (dense, lexical, or fused by the backend).

        ``min_similarity`` is the cosine floor a hit must clear. Top-k always returns k rows
        however unrelated, so without a floor a customer whose notes say nothing about the current
        message is handed the five least-unrelated ones as if they were context (gap B1). The
        backend applies it as a predicate **on the dense leg**, mirroring the handbook search: the
        lexical leg has no cosine score to floor.

        ``mode`` selects which legs run and defaults to ``hybrid``. The single-leg modes exist so
        each leg's contribution can be measured rather than assumed (see
        ``app/agents/customer_memory/eval.py``); everything that is not evaluating gets the fused
        result.
        """
        return await self._client.request(
            "POST",
            "/internal/customers/memories/search",
            json={
                "organizationId": org_id,
                "customerId": customer_id,
                "query": query,
                "topK": top_k,
                "minSimilarity": min_similarity,
                "mode": mode,
            },
        )

    async def search_handbook(
        self,
        query: str,
        top_k: int = 5,
        audience: str = "staff",
        min_similarity: float = 0.0,
    ) -> list[dict[str, Any]]:
        """Hybrid (dense + lexical) search over the Aveline handbook (ADR-025).

        The backend runs both retrieval legs over the same filter and fuses them with Reciprocal
        Rank Fusion; see ``docs/architecture/handbook.md``. Each hit carries its vector and lexical
        rank alongside the fused score, which is what lets the retrieval eval report per-leg
        quality instead of one opaque number.
        """
        result = await self._client.request(
            "POST",
            "/internal/handbook/search",
            json={
                "query": query,
                "topK": top_k,
                "audience": audience,
                "minSimilarity": min_similarity,
            },
        )
        return result if isinstance(result, list) else []

    async def save_customer_memory(
        self,
        org_id: str,
        customer_id: str,
        content: str,
        category: str,
        source: str | None = None,
        is_explicit: bool | None = None,
        confidence: float | None = None,
        metadata_json: str | None = None,
    ) -> dict[str, Any]:
        """Persist a new customer memory via the backend.

        The provenance fields are sent only when the caller actually knows them. The agent does
        know them - it extracted the statement and knows whether the customer said it - and this
        tool used to accept only content and category, so every agent-written memory landed as
        ``Source="conversation"``, ``IsExplicit=false``, ``Confidence=0.50`` no matter what the
        extraction had computed (gap A1). Omitting a field is now the only way to get the backend's
        own default, which is the honest outcome for a caller that has no opinion.
        """
        body: dict[str, Any] = {
            "organizationId": org_id,
            "content": content,
            "category": category,
        }
        if source is not None:
            body["source"] = source
        if is_explicit is not None:
            body["isExplicit"] = is_explicit
        if confidence is not None:
            body["confidence"] = confidence
        if metadata_json is not None:
            body["metadataJson"] = metadata_json

        return await self._client.request(
            "POST",
            f"/internal/customers/{customer_id}/memories",
            json=body,
        )

    async def save_customer_preference(
        self,
        org_id: str,
        customer_id: str,
        key: str,
        value: str,
        source: str | None = None,
        is_explicit: bool | None = None,
        confidence: float | None = None,
    ) -> dict[str, Any]:
        """Record a stated preference in the customer's preferences table (gap C4).

        Distinct from :meth:`save_customer_memory` on purpose. A memory is a searchable, dated
        statement; a preference is the canonical key/value the interaction brief's summary is
        assembled from. The agent used to write only the memory, so a preference the customer stated
        in conversation could be on file as a note and still be missing from the brief that exists to
        surface it.
        """
        body: dict[str, Any] = {
            "organizationId": org_id,
            "preferenceKey": key,
            "preferenceValue": value,
        }
        if source is not None:
            body["source"] = source
        if is_explicit is not None:
            body["isExplicit"] = is_explicit
        if confidence is not None:
            body["confidence"] = confidence

        return await self._client.request(
            "POST",
            f"/internal/customers/{customer_id}/preferences",
            json=body,
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
        parsed_intent_json: str | None = None,
    ) -> dict[str, Any]:
        """Record a customer interaction via the backend.

        ``parsed_intent_json`` (when provided) stores the structured intent extracted from the
        message so the interaction history carries what the agent understood.
        """
        body: dict[str, Any] = {
            "organizationId": org_id,
            "channel": channel,
            "direction": direction,
            "messageContent": message_content,
        }
        if parsed_intent_json is not None:
            body["parsedIntentJson"] = parsed_intent_json
        return await self._client.request(
            "POST",
            f"/internal/customers/{customer_id}/interactions",
            json=body,
        )

    async def add_customer_event(
        self,
        org_id: str,
        customer_id: str,
        event_type: str,
        event_date: str,
        description: str | None = None,
    ) -> dict[str, Any]:
        """Persist a structured customer event (wedding, birthday, ...) via the backend."""
        body: dict[str, Any] = {
            "organizationId": org_id,
            "eventType": event_type,
            "eventDate": event_date,
        }
        if description:
            body["description"] = description
        return await self._client.request(
            "POST",
            f"/internal/customers/{customer_id}/events",
            json=body,
        )

    async def get_customer_events(self, org_id: str, customer_id: str) -> dict[str, Any]:
        """List a customer's structured events (upcoming first) via the backend."""
        return await self._client.request(
            "GET", f"/internal/customers/{customer_id}/events?organizationId={org_id}"
        )

    async def get_customer_consent(self, org_id: str, customer_id: str) -> dict[str, Any]:
        """Check a customer's consent status via the backend."""
        return await self._client.request(
            "GET", f"/internal/customers/{customer_id}/consent?organizationId={org_id}"
        )

    # ============================== CONVERSATION ==============================

    async def get_conversation_history(
        self,
        org_id: str,
        conversation_id: str,
        limit: int = 20,
    ) -> dict[str, Any]:
        """Read a bounded, oldest-first window of a conversation's transcript (ADR-023).

        The concierge workflow receives only the newest message, so nothing referential
        ("yes, that one", "is it still available?") can be resolved without this. The window is
        deliberately bounded by the caller: the transcript is a context budget, not an archive.

        Args:
            org_id: The owning organization (tenant scope).
            conversation_id: The conversation to read.
            limit: Maximum turns to return, newest-last. Clamped by the backend.

        Returns:
            The backend ``{conversationId, organizationId, items}`` envelope, where each item is
            ``{id, authorKind, agentKey, kind, text, createdAt}``.
        """
        return await self._client.request(
            "GET",
            f"/internal/conversations/{conversation_id}/messages",
            params={"organizationId": org_id, "limit": limit},
        )

    # ============================== TENANT ACCOUNT ==============================

    async def get_tenant_usage(self, org_id: str) -> dict[str, Any]:
        """Read the organisation's own account position (ADR-026).

        The Blossom balance and the seat/customer allowances the boutique's dashboard shows, from
        the same backend projections that dashboard renders. The caller is responsible for the
        audience check: this method performs no permission reasoning of its own, and must only be
        reached for an organisation whose request carried explicit staff evidence.

        Args:
            org_id: The organisation (tenant scope).

        Returns:
            The backend snapshot envelope ``{organizationId, blossoms, staff, customers,
            customerCountBasis, asOf}``, with each allowance carrying
            ``{key, used, limit, remaining, percentUsed, isHardLimit}``.
        """
        return await self._client.request("GET", f"/internal/usage/tenant/{org_id}")

    async def get_customer_book_summary(
        self,
        org_id: str,
        limit: int = 5,
    ) -> dict[str, Any]:
        """Read the boutique's client book at a glance (ADR-026).

        How many clients there are, plus the few most recently active, taken from the same read the
        tenant dashboard's Home rows use - so a client cannot be named here who is not in the book.
        Same audience rule as :meth:`get_tenant_usage`: the caller decides who may see it.

        Args:
            org_id: The organisation (tenant scope).
            limit: How many named clients to return. Clamped by the backend.

        Returns:
            The backend envelope ``{total, activitySince, highlights}``, where each highlight is
            ``{customerId, name, level, activity, lastActivityAtUtc}``.
        """
        return await self._client.request(
            "GET",
            "/internal/customers/book-summary",
            params={"organizationId": org_id, "limit": limit},
        )

    # ============================== VISUAL AGENT ==============================

    async def search_inventory(self, criteria: dict[str, Any]) -> dict[str, Any]:
        """Search inventory by structured criteria via the backend."""
        payload = dict(criteria)
        if "org_id" in payload and "organizationId" not in payload:
            payload["organizationId"] = payload["org_id"]
        return await self._client.request("POST", "/internal/visual/inventory/search", json=payload)

    async def get_inventory_item(self, item_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Fetch inventory item details."""
        url = f"/internal/visual/inventory/{item_id}"
        if org_id:
            url += f"?organizationId={org_id}"
        return await self._client.request("GET", url)

    async def analyze_product_image(
        self,
        image_url: str | None = None,
        org_id: str | None = None,
        image_ref_kind: str | None = None,
        image_ref_id: str | None = None,
    ) -> dict[str, Any]:
        """Analyze a product image and return extracted attributes.

        The reference arm is preferred: when ``image_ref_kind``/``image_ref_id`` are present the
        body carries only them (plus the organisation), and the rotating ``imageUrl`` bridge is not
        sent alongside.

        The organisation is mandatory and its absence fails loudly. The previous all-zeros default
        (``00000000-0000-0000-0000-000000000000``) was the C15 defect: the API rejects
        ``Guid.Empty`` with a ``400`` and a wrong-but-valid organisation resolves nothing, so the
        sentinel could only ever produce a tenant-scoped misfire (strategy §1, §4 C15).
        """
        if not org_id:
            raise ValueError(
                "analyze_product_image requires the real organizationId; refusing to substitute "
                "Guid.Empty (strategy §4 C15)."
            )

        body: dict[str, Any] = {"organizationId": org_id}
        if image_ref_kind and image_ref_id:
            body["imageRefKind"] = image_ref_kind
            body["imageRefId"] = image_ref_id
        elif image_url:
            body["imageUrl"] = image_url
        else:
            raise ValueError(
                "analyze_product_image requires either an image reference or an imageUrl."
            )
        return await self._client.request("POST", "/internal/visual/analyze-image", json=body)

    async def match_customers_to_item(self, item_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Find customers whose preferences match an item."""
        url = f"/internal/visual/customer-matches/{item_id}"
        if org_id:
            url += f"?organizationId={org_id}"
        return await self._client.request("GET", url)

    async def check_stock(self, item_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Check stock availability for an inventory item."""
        url = f"/internal/visual/inventory/{item_id}"
        if org_id:
            url += f"?organizationId={org_id}"
        return await self._client.request("GET", url)

    async def compose_outfit(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Compose a harmonized outfit look."""
        body = dict(payload)
        if "org_id" in body and "organizationId" not in body:
            body["organizationId"] = body["org_id"]
        if "primary_item_id" in body and "primaryItemId" not in body:
            body["primaryItemId"] = body["primary_item_id"]
        return await self._client.request("POST", "/internal/visual/outfits/compose", json=body)

    async def create_sourcing_request(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Submit a sourcing request for unavailable pieces."""
        body = dict(payload)
        if "org_id" in body and "organizationId" not in body:
            body["organizationId"] = body["org_id"]
        if "customer_id" in body and "customerId" not in body:
            body["customerId"] = body["customer_id"]
        return await self._client.request("POST", "/internal/visual/sourcing-requests", json=body)

    async def search_supplier_catalog(
        self,
        query: str | None = None,
        org_id: str | None = None,
        supplier_id: str | None = None,
        category: str | None = None,
        color: str | None = None,
        max_price: float | None = None,
    ) -> dict[str, Any]:
        """Query supplier catalogs for material/garment sourcing."""
        sup_id = supplier_id or "00000000-0000-0000-0000-000000000001"
        url = f"/internal/visual/suppliers/{sup_id}/catalog?organizationId={org_id or ''}"
        if category:
            url += f"&category={category}"
        if color:
            url += f"&color={color}"
        if max_price is not None:
            url += f"&maxPrice={max_price}"
        return await self._client.request("GET", url)

    async def get_suppliers(self, org_id: str | None = None) -> list[dict[str, Any]]:
        """Fetch registered partner ateliers/suppliers for an organization."""
        path = f"/api/v1/orgs/{org_id}/catalog/suppliers" if org_id else "/internal/visual/suppliers"
        res = await self._client.request("GET", path)
        if isinstance(res, list):
            return res
        if isinstance(res, dict) and "items" in res:
            return res["items"]
        return []

    # ============================== COMMERCE AGENT ==============================

    async def calculate_margin(self, order_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Compute margin for an order via the backend."""
        path = f"/api/v1/orgs/{org_id}/orders/{order_id}/recalculate" if org_id else f"/api/internal/orders/{order_id}/calculate-margin"
        return await self._client.request("POST", path)

    async def generate_payment_request(self, order_id: str, amount: float, org_id: str | None = None) -> dict[str, Any]:
        """Generate a payment request for an order."""
        path = f"/api/v1/orgs/{org_id}/payments" if org_id else f"/api/internal/orders/{order_id}/payment-request"
        payload = {"orderId": order_id, "amount": amount} if org_id else {"amount": amount}
        return await self._client.request("POST", path, json=payload)

    async def validate_payment(self, payment_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Read the backend's settlement status for one payment (never invents one)."""
        path = (
            f"/api/v1/orgs/{org_id}/payments/{payment_id}"
            if org_id
            else f"/api/internal/payments/{payment_id}"
        )
        return await self._client.request("GET", path)

    async def check_approval_threshold(self, order_id: str, org_id: str | None = None) -> dict[str, Any]:
        """Evaluate an order against approval thresholds."""
        path = f"/api/v1/orgs/{org_id}/approvals" if org_id else f"/api/internal/orders/{order_id}/approval-check"
        return await self._client.request("GET", path)
