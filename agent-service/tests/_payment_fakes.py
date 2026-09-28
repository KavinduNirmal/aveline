"""A hand-written payment-registry double for the commerce tests (plan §9.8, P10).

The payment tools no longer fall back to a local checkout link, so a test that settles a deal must
supply a backend that actually answers. This double answers only the two payment methods the tools
are allowed to call; it is deliberately not a mock of the whole ``ToolRegistry`` (the other commerce
tools keep their own documented fallbacks).
"""

from typing import Any


class AnsweringPaymentRegistry:
    """A ``ToolRegistry`` double that mints a provider link and reports a real status."""

    def __init__(
        self,
        *,
        status: str = "pending",
        checkout_url: str | None = None,
        create_error: Exception | None = None,
    ) -> None:
        self.status = status
        self.checkout_url = checkout_url
        self.create_error = create_error
        self.create_calls: list[tuple[Any, ...]] = []
        self.validate_calls: list[tuple[Any, ...]] = []

    async def generate_payment_request(
        self, order_id: str | None, amount: float, org_id: str | None = None
    ) -> dict[str, Any]:
        self.create_calls.append((org_id, order_id, amount))
        if self.create_error is not None:
            raise self.create_error

        short_ref = (order_id or "sample")[-6:]
        return {
            "amount": amount,
            "status": "pending",
            "paymentLink": self.checkout_url or f"https://checkout.provider.example/{short_ref}",
            "gatewayTransactionId": f"PAY-{short_ref}",
            "expiresAt": None,
        }

    async def validate_payment(self, payment_id: str, org_id: str | None = None) -> dict[str, Any]:
        self.validate_calls.append((org_id, payment_id))
        return {"id": payment_id, "status": self.status}
