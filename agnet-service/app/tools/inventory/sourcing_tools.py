"""Sourcing request tools for Visual Insight Agent (Elle).

Initiates sourcing tickets when clients inquire about unavailable pieces or bespoke silhouettes.
"""

from typing import Any
import uuid

from app.schemas.visual_insight import SourcingRequestDto


async def create_sourcing_request(
    registry: Any,
    org_id: str,
    customer_id: str | None,
    image_url: str | None,
    notes: str,
) -> SourcingRequestDto:
    """Create a backorder or supplier sourcing ticket."""
    req_id = f"src_{uuid.uuid4().hex[:8]}"
    payload = {
        "organizationId": org_id,
        "customerId": customer_id,
        "imageUrl": image_url,
        "notes": notes,
        "requestId": req_id,
    }
    try:
        if hasattr(registry, "create_sourcing_request"):
            res = await registry.create_sourcing_request(payload)
            req_id = res.get("requestId") or res.get("id") or req_id
    except Exception:
        pass

    return SourcingRequestDto(
        requestId=req_id,
        customerId=customer_id,
        imageUrl=image_url,
        notes=notes,
        status="pending",
    )
