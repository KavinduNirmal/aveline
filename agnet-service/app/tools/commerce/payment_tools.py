"""Payment link generation and validation tools (Slice 3).

**Money statements are never made offline.** A checkout URL and a settled status are claims about a
provider's state, so both tools delegate to the backend and neither invents an answer when the
backend cannot be reached (plan §9.8, P10):

- :func:`generate_payment_request` raises :class:`PaymentGatewayUnavailableError`; it never falls back
  to a fabricated URL, because a payment link that leads nowhere is worse than an error.
- :func:`validate_payment` returns an explicit ``unknown`` shape; it never reports
  ``is_settled: True`` without a server answer.
"""

import logging
from typing import Any

logger = logging.getLogger("aveline.agent.tools.commerce.payment")

#: The explicit "the server did not tell us" status. It is deliberately not a settled one.
UNKNOWN_STATUS = "unknown"

#: Server statuses that mean the money is in. Kept to the vocabulary the Commerce payment DTO uses.
SETTLED_STATUSES = frozenset({"confirmed", "succeeded", "paid"})


class PaymentGatewayUnavailableError(RuntimeError):
    """Raised when no backend answer can mint a payment link.

    There is deliberately no local fallback. The caller must surface this rather than hand the
    customer a URL that no provider recognises.
    """


async def generate_payment_request(
    org_id: str,
    order_id: str | None,
    amount: float,
    method: str = "online",
    registry: Any = None,
) -> dict[str, Any]:
    """Generate a customer checkout link through the backend payment path.

    Args:
        org_id: Organization/Tenant ID.
        order_id: Order UUID.
        amount: Final transaction amount in LKR.
        method: Payment method (online, card, cash).
        registry: ToolRegistry client for backend API.

    Returns:
        Dictionary with amount, status, checkout url (``None`` when the provider settles in place),
        method, gateway transaction id, and expiry.

    Raises:
        PaymentGatewayUnavailableError: The backend is absent, unreachable, or answered without a
            usable body. No URL is fabricated in that case.
    """
    if not order_id:
        raise PaymentGatewayUnavailableError(
            "A payment request needs an order id and none was supplied; no link was created."
        )

    if registry is None or not hasattr(registry, "generate_payment_request"):
        raise PaymentGatewayUnavailableError(
            "No backend is available to create the payment link; refusing to invent one."
        )

    try:
        result = await registry.generate_payment_request(order_id, amount, org_id=org_id or None)
    except Exception as ex:
        logger.warning("Payment link creation failed; not fabricating one: %s", ex)
        raise PaymentGatewayUnavailableError(
            f"The payment provider could not be reached for order {order_id}: {ex}"
        ) from ex

    if not isinstance(result, dict):
        raise PaymentGatewayUnavailableError(
            f"The backend returned no payment record for order {order_id}; no link was created."
        )

    # `None` is an honest answer: a provider that settles in place issues no URL.
    url = result.get("paymentLink") or result.get("url")
    return {
        "amount": float(result.get("amount", amount)),
        "status": result.get("status", "pending"),
        "url": url,
        "method": method,
        "gateway_transaction_id": result.get("gatewayTransactionId"),
        "expires_at": result.get("expiresAt"),
    }


async def validate_payment(
    org_id: str,
    payment_id: str,
    registry: Any = None,
) -> dict[str, Any]:
    """Check payment settlement status with the backend gateway.

    A settlement is only ever reported when the server said so. Anything else — no registry, an
    unreachable backend, or a body without a status — is reported as ``unknown`` with
    ``is_settled: False``.
    """
    unknown = {
        "payment_id": payment_id,
        "status": UNKNOWN_STATUS,
        "is_settled": False,
        "gateway_transaction_id": None,
        "confirmed_at": None,
    }

    if not payment_id:
        return {**unknown, "detail": "No payment id was supplied, so no status could be read."}

    if registry is None or not hasattr(registry, "validate_payment"):
        logger.warning("No backend is available to validate payment %s; reporting unknown.", payment_id)
        return {**unknown, "detail": "No backend is available to confirm settlement."}

    try:
        result = await registry.validate_payment(payment_id, org_id=org_id or None)
    except Exception as ex:
        logger.warning("Payment validation call failed for %s; reporting unknown: %s", payment_id, ex)
        return {**unknown, "detail": f"The backend could not be reached: {ex}"}

    if not isinstance(result, dict):
        return {**unknown, "detail": "The backend returned no payment record."}

    status = str(result.get("status") or "").strip().lower()
    if not status:
        return {**unknown, "detail": "The backend returned a payment record with no status."}

    return {
        "payment_id": payment_id,
        "status": status,
        "is_settled": status in SETTLED_STATUSES,
        "gateway_transaction_id": result.get("gatewayTransactionId"),
        "confirmed_at": result.get("confirmedAt"),
    }
