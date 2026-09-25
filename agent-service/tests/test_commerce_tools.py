"""Unit tests for the Commerce Agent toolset (app/tools/commerce/).

Tests margin calculation, discount applications, loyalty tier resolutions,
business rules evaluation, payment generation, and courier bookings.
"""

import unittest

from _payment_fakes import AnsweringPaymentRegistry

from app.tools.commerce.delivery_tools import book_courier
from app.tools.commerce.loyalty_tools import get_customer_loyalty_tier
from app.tools.commerce.payment_tools import generate_payment_request, validate_payment
from app.tools.commerce.pricing_tools import apply_discount, calculate_margin
from app.tools.commerce.rules_tools import check_approval_threshold, validate_business_rules


class TestCommerceTools(unittest.IsolatedAsyncioTestCase):
    def test_calculate_margin_positive(self):
        result = calculate_margin(subtotal=10000.0, total_cost=6000.0)
        self.assertEqual(result["gross_profit"], 4000.0)
        self.assertEqual(result["margin"], 0.4000)

    def test_calculate_margin_zero_subtotal(self):
        result = calculate_margin(subtotal=0.0, total_cost=5000.0)
        self.assertEqual(result["gross_profit"], 0.0)
        self.assertEqual(result["margin"], 0.0)

    def test_apply_discount(self):
        result = apply_discount(subtotal=20000.0, discount_percent=0.10)
        self.assertEqual(result["discount_amount"], 2000.0)
        self.assertEqual(result["total"], 18000.0)

    def test_apply_discount_clamps_bounds(self):
        result_negative = apply_discount(subtotal=1000.0, discount_percent=-0.5)
        self.assertEqual(result_negative["discount_amount"], 0.0)
        self.assertEqual(result_negative["total"], 1000.0)

        result_excess = apply_discount(subtotal=1000.0, discount_percent=1.5)
        self.assertEqual(result_excess["discount_amount"], 1000.0)
        self.assertEqual(result_excess["total"], 0.0)

    async def test_get_customer_loyalty_tier_no_customer(self):
        result = await get_customer_loyalty_tier("org-1", customer_id=None)
        self.assertEqual(result["tier"], "New")
        self.assertEqual(result["max_allowed_discount"], 0.0)
        self.assertFalse(result["vip"])

    async def test_validate_business_rules_auto_approved(self):
        result = await validate_business_rules(
            org_id="org-1",
            order_total=25000.0,
            margin=0.35,
            requested_discount=0.05,
            customer_tier="Regular",
        )
        self.assertTrue(result["is_auto_approved"])
        self.assertFalse(result["requires_approval"])
        self.assertEqual(result["triggered_rules"], [])

    async def test_validate_business_rules_high_value_breached(self):
        result = await validate_business_rules(
            org_id="org-1",
            order_total=55000.0,  # > 40,000 threshold
            margin=0.35,
            requested_discount=0.05,
            customer_tier="Regular",
        )
        self.assertFalse(result["is_auto_approved"])
        self.assertTrue(result["requires_approval"])
        self.assertIn("HIGH_VALUE_THRESHOLD_EXCEEDED", result["triggered_rules"])

    async def test_validate_business_rules_low_margin_breached(self):
        result = await validate_business_rules(
            org_id="org-1",
            order_total=20000.0,
            margin=0.18,  # < 25% minimum margin
            requested_discount=0.0,
            customer_tier="Regular",
        )
        self.assertFalse(result["is_auto_approved"])
        self.assertTrue(result["requires_approval"])
        self.assertIn("LOW_MARGIN_THRESHOLD", result["triggered_rules"])

    async def test_validate_business_rules_discount_cap_breached(self):
        result = await validate_business_rules(
            org_id="org-1",
            order_total=20000.0,
            margin=0.30,
            requested_discount=0.20,  # > 10% VIP cap
            customer_tier="VIP",
        )
        self.assertFalse(result["is_auto_approved"])
        self.assertTrue(result["requires_approval"])
        self.assertIn("DISCOUNT_LIMIT_EXCEEDED", result["triggered_rules"])

    def test_check_approval_threshold_sync(self):
        self.assertFalse(check_approval_threshold(order_total=20000.0, margin=0.30, requested_discount=0.05))
        self.assertTrue(check_approval_threshold(order_total=45000.0, margin=0.30))
        self.assertTrue(check_approval_threshold(order_total=20000.0, margin=0.15))

    async def test_generate_payment_request(self):
        """The link is the backend's, never a locally invented one (plan §9.8)."""
        registry = AnsweringPaymentRegistry(checkout_url="https://checkout.provider.example/pi_777")
        result = await generate_payment_request(
            org_id="org-1", order_id="ord-123456", amount=35000.0, registry=registry
        )
        self.assertEqual(result["amount"], 35000.0)
        self.assertEqual(result["status"], "pending")
        self.assertEqual(result["url"], "https://checkout.provider.example/pi_777")
        self.assertNotIn("pay.aveline.boutique", result["url"])
        self.assertEqual(registry.create_calls, [("org-1", "ord-123456", 35000.0)])

    async def test_validate_payment(self):
        """A settlement is reported only when the server reports one (plan §9.8)."""
        registry = AnsweringPaymentRegistry(status="confirmed")
        result = await validate_payment(org_id="org-1", payment_id="pay-777", registry=registry)
        self.assertEqual(result["status"], "confirmed")
        self.assertTrue(result["is_settled"])
        self.assertEqual(registry.validate_calls, [("org-1", "pay-777")])

    async def test_book_courier(self):
        result_with_addr = await book_courier(org_id="org-1", order_id="ord-123456", delivery_address="12 Flower Road, Colombo 07")
        self.assertEqual(result_with_addr["carrier"], "PickMe")
        self.assertEqual(result_with_addr["status"], "planned")
        self.assertEqual(result_with_addr["estimated_fee"], 650.0)

        result_no_addr = await book_courier(org_id="org-1", order_id="ord-123456", delivery_address=None)
        self.assertEqual(result_no_addr["status"], "skipped")
        self.assertIsNone(result_no_addr["delivery_address"])


if __name__ == "__main__":
    unittest.main()
