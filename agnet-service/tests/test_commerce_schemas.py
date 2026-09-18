"""Tests for the Commerce Agent Pydantic schemas (app/schemas/commerce.py).

Verifies strict input/output shape validation, default values, extra field forbidding,
and serializability. Compatible with both pytest and standard unittest.
"""

import unittest
from pydantic import ValidationError

from app.schemas.commerce import (
    CommerceAgentInput,
    CommerceAgentOutput,
    CourierDetails,
    DealEvaluation,
    DealItem,
    PaymentDetails,
)


class TestCommerceSchemas(unittest.TestCase):
    def test_deal_item_valid(self):
        item = DealItem(
            item_id="prod-101",
            item_name="Silk Blouse",
            quantity=2,
            unit_price=12000.0,
            wholesale_cost=7000.0,
            total_price=24000.0,
        )
        self.assertEqual(item.item_id, "prod-101")
        self.assertEqual(item.item_name, "Silk Blouse")
        self.assertEqual(item.quantity, 2)
        self.assertEqual(item.unit_price, 12000.0)
        self.assertEqual(item.wholesale_cost, 7000.0)
        self.assertEqual(item.total_price, 24000.0)

    def test_deal_item_forbids_extra_fields(self):
        with self.assertRaises(ValidationError):
            DealItem(
                item_id="prod-101",
                item_name="Silk Blouse",
                unit_price=12000.0,
                total_price=12000.0,
                unexpected_field="disallowed",  # type: ignore[call-arg]
            )

    def test_deal_evaluation_defaults(self):
        evaluation = DealEvaluation(
            subtotal=50000.0,
            total=45000.0,
            discount_amount=5000.0,
            total_cost=28000.0,
            margin=0.3778,
            loyalty_tier="VIP",
            applied_discount_percent=0.10,
        )
        self.assertEqual(evaluation.subtotal, 50000.0)
        self.assertEqual(evaluation.total, 45000.0)
        self.assertTrue(evaluation.is_auto_approved)
        self.assertFalse(evaluation.requires_approval)
        self.assertEqual(evaluation.triggered_rules, [])
        self.assertEqual(evaluation.flags, [])

    def test_payment_details_round_trip(self):
        payment = PaymentDetails(
            amount=45000.0,
            status="pending",
            url="https://payhere.lk/pay/inv_123",
            method="online",
            gateway_transaction_id="TXN-9988",
            expires_at="2026-09-12T12:00:00Z",
        )
        data = payment.model_dump()
        self.assertEqual(data["amount"], 45000.0)
        self.assertEqual(data["status"], "pending")
        self.assertEqual(data["url"], "https://payhere.lk/pay/inv_123")
        self.assertEqual(data["method"], "online")

    def test_courier_details_valid(self):
        courier = CourierDetails(
            carrier="PickMe",
            status="planned",
            delivery_address="45 Galle Road, Colombo 03",
            estimated_fee=650.0,
            tracking_number="PM-776655",
            estimated_eta="2026-09-12T16:00:00Z",
        )
        self.assertEqual(courier.carrier, "PickMe")
        self.assertEqual(courier.estimated_fee, 650.0)
        self.assertEqual(courier.status, "planned")

    def test_commerce_agent_input(self):
        agent_input = CommerceAgentInput(
            org_id="org-555",
            order_id="ord-999",
            customer_id="cust-111",
            customer_name="Alice Smith",
            items=[
                DealItem(
                    item_id="prod-1",
                    item_name="Linen Trousers",
                    quantity=1,
                    unit_price=8500.0,
                    wholesale_cost=4500.0,
                    total_price=8500.0,
                )
            ],
            proposed_discount=0.05,
            delivery_address="Colombo 07",
            channel="whatsapp",
        )
        self.assertEqual(agent_input.org_id, "org-555")
        self.assertEqual(len(agent_input.items), 1)
        self.assertEqual(agent_input.proposed_discount, 0.05)

    def test_commerce_agent_output_success(self):
        output = CommerceAgentOutput(
            status="success",
            summary="Deal approved for Alice with 5% discount. Total: LKR 8,075",
            needs_approval=False,
            deal=DealEvaluation(
                subtotal=8500.0,
                discount_amount=425.0,
                total=8075.0,
                total_cost=4500.0,
                margin=0.4427,
                loyalty_tier="Regular",
                applied_discount_percent=0.05,
            ),
            payment=PaymentDetails(
                amount=8075.0,
                status="pending",
                url="https://pay.example.com/checkout",
            ),
            courier=CourierDetails(
                carrier="PickMe",
                delivery_address="Colombo 07",
                estimated_fee=500.0,
            ),
        )
        data = output.model_dump()
        self.assertEqual(data["agent"], "commerce")
        self.assertEqual(data["status"], "success")
        self.assertFalse(data["needs_approval"])
        self.assertEqual(data["deal"]["total"], 8075.0)
        self.assertEqual(data["payment"]["url"], "https://pay.example.com/checkout")
        self.assertEqual(data["courier"]["carrier"], "PickMe")


if __name__ == "__main__":
    unittest.main()
