"""The payment tools must not invent a settlement or a checkout link (plan §9.8, P10).

Before this change ``payment_tools`` had two fabrications:

- ``generate_payment_request`` fell back to
  ``https://pay.aveline.boutique/checkout/{ref}`` whenever the backend was absent or unreachable, so
  the agent handed a customer a link that led nowhere;
- ``validate_payment`` returned ``{"status": "confirmed", "is_settled": True}`` without calling
  anything at all.

Both are money statements about the real world, so neither may be produced offline. These tests
therefore assert the **absence** of a simulation, not merely the presence of a new call: for every way
the backend can fail, no URL and no settled status may come back. That is what makes the fix
regression-proof rather than a rename of the fallback.
"""

import pytest

from app.tools.commerce import payment_tools
from app.tools.commerce.payment_tools import generate_payment_request, validate_payment

#: The host the old fallback minted links against. It must never appear in an answer again.
FABRICATED_CHECKOUT_HOST = "pay.aveline.boutique"


class _AnsweringRegistry:
    """A hand-written double exposing only the two payment methods the tools may call."""

    def __init__(
        self,
        *,
        create: dict | None = None,
        create_error: Exception | None = None,
        status: dict | None = None,
        status_error: Exception | None = None,
    ) -> None:
        self.create = create
        self.create_error = create_error
        self.status = status
        self.status_error = status_error
        self.create_calls: list[tuple] = []
        self.status_calls: list[tuple] = []

    async def generate_payment_request(self, order_id, amount, org_id=None):
        self.create_calls.append((org_id, order_id, amount))
        if self.create_error is not None:
            raise self.create_error
        return self.create

    async def validate_payment(self, payment_id, org_id=None):
        self.status_calls.append((org_id, payment_id))
        if self.status_error is not None:
            raise self.status_error
        return self.status


# ---------------------------------------------------------------------------
# generate_payment_request: no fabricated URL
# ---------------------------------------------------------------------------


async def test_generate_payment_request_without_a_backend_raises_rather_than_inventing_a_url():
    """The old fallback's exact trigger: no registry at all."""
    with pytest.raises(payment_tools.PaymentGatewayUnavailableError):
        await generate_payment_request(org_id="org-1", order_id="ord-123456", amount=35000.0)


async def test_generate_payment_request_when_the_backend_errors_raises_rather_than_inventing_a_url():
    """A reachable client whose HTTP call fails is still no authority on the link."""
    registry = _AnsweringRegistry(create_error=RuntimeError("connection refused"))

    with pytest.raises(payment_tools.PaymentGatewayUnavailableError):
        await generate_payment_request(
            org_id="org-1", order_id="ord-123456", amount=35000.0, registry=registry
        )

    assert registry.create_calls == [("org-1", "ord-123456", 35000.0)]


@pytest.mark.parametrize(
    "registry",
    [
        None,
        _AnsweringRegistry(create_error=RuntimeError("connection refused")),
        _AnsweringRegistry(create={}),
        _AnsweringRegistry(create={"amount": 35000.0, "status": "pending"}),
    ],
    ids=["no-registry", "http-error", "empty-body", "body-without-a-link"],
)
async def test_no_failure_mode_yields_a_fabricated_checkout_url(registry):
    """The absence sweep: every way the backend can decline must surface as an error."""
    try:
        result = await generate_payment_request(
            org_id="org-1", order_id="ord-123456", amount=35000.0, registry=registry
        )
    except payment_tools.PaymentGatewayUnavailableError:
        return

    assert FABRICATED_CHECKOUT_HOST not in (result.get("url") or ""), (
        "a checkout URL was produced without a server answer"
    )


async def test_generate_payment_request_returns_the_backend_link_and_forwards_the_org():
    registry = _AnsweringRegistry(
        create={
            "amount": 35000.0,
            "status": "pending",
            "paymentLink": "https://checkout.provider.example/pi_123",
            "gatewayTransactionId": "pi_123",
            "expiresAt": "2026-10-01T00:00:00Z",
        }
    )

    result = await generate_payment_request(
        org_id="org-1", order_id="ord-123456", amount=35000.0, registry=registry
    )

    assert registry.create_calls == [("org-1", "ord-123456", 35000.0)]
    assert result["url"] == "https://checkout.provider.example/pi_123"
    assert result["status"] == "pending"
    assert result["gateway_transaction_id"] == "pi_123"


async def test_a_backend_answer_with_no_link_is_reported_as_no_link_not_an_invented_one():
    """A provider that settles in place has no URL; ``None`` is the honest answer."""
    registry = _AnsweringRegistry(create={"amount": 35000.0, "status": "requires_action"})

    result = await generate_payment_request(
        org_id="org-1", order_id="ord-123456", amount=35000.0, registry=registry
    )

    assert result["url"] is None
    assert FABRICATED_CHECKOUT_HOST not in repr(result)


# ---------------------------------------------------------------------------
# validate_payment: no fabricated settlement
# ---------------------------------------------------------------------------


async def test_validate_payment_without_a_backend_is_unknown_and_not_settled():
    result = await validate_payment(org_id="org-1", payment_id="pay-777")

    assert result["is_settled"] is False
    assert result["status"] == "unknown"


async def test_validate_payment_when_the_backend_errors_is_unknown_and_not_settled():
    registry = _AnsweringRegistry(status_error=RuntimeError("backend down"))

    result = await validate_payment(org_id="org-1", payment_id="pay-777", registry=registry)

    assert registry.status_calls == [("org-1", "pay-777")]
    assert result["is_settled"] is False
    assert result["status"] == "unknown"


async def test_validate_payment_returns_the_status_the_server_reported():
    registry = _AnsweringRegistry(status={"id": "pay-777", "status": "confirmed"})

    result = await validate_payment(org_id="org-1", payment_id="pay-777", registry=registry)

    assert result["is_settled"] is True
    assert result["status"] == "confirmed"


async def test_validate_payment_does_not_settle_a_pending_payment():
    registry = _AnsweringRegistry(status={"id": "pay-777", "status": "pending"})

    result = await validate_payment(org_id="org-1", payment_id="pay-777", registry=registry)

    assert result["is_settled"] is False
    assert result["status"] == "pending"


@pytest.mark.parametrize(
    "registry",
    [
        None,
        _AnsweringRegistry(status_error=RuntimeError("backend down")),
        _AnsweringRegistry(status={}),
        _AnsweringRegistry(status={"id": "pay-777"}),
    ],
    ids=["no-registry", "http-error", "empty-body", "body-without-a-status"],
)
async def test_no_failure_mode_reports_a_settlement(registry):
    result = await validate_payment(org_id="org-1", payment_id="pay-777", registry=registry)

    assert result["is_settled"] is False, "a settlement was reported without a server answer"
