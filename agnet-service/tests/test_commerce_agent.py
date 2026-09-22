"""End-to-end tests for Commerce Agent (Lina) and Salon block translations.

Tests full deal workflows, Salon block translations via build_lina_blocks,
and concierge envelope outputs. Compatible with unittest and pytest.
"""

import unittest

from app.agents.commerce.graph import build_commerce_graph
from app.agents.commerce.state import CommerceAgentState
from app.events.block_builders import build_lina_blocks


class TestCommerceAgentE2E(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.graph = build_commerce_graph(registry=None)
        self.org_id = "org-boutique-colombo"

    async def test_commerce_deal_execution_and_salon_block_generation(self):
        commerce_state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-7788",
            "customer_id": "cust-55",
            "customer_name": "Eleanor",
            "items": [
                {
                    "item_id": "item-101",
                    "item_name": "Cashmere Scarf",
                    "quantity": 2,
                    "unit_price": 7500.0,
                    "wholesale_cost": 4000.0,
                    "total_price": 15000.0,
                }
            ],
            "proposed_discount": 0.05,
            "delivery_address": "45 Ward Place, Colombo 07",
            "channel": "whatsapp",
            "message": "Can I order 2 cashmere scarves for Rs. 15,000?",
        }

        result = await self.graph.ainvoke(commerce_state)
        output = result.get("output") or {}

        self.assertIsNotNone(output)
        self.assertEqual(output["status"], "success")
        self.assertFalse(output["needs_approval"])
        self.assertIsNotNone(output["payment"]["url"])
        self.assertEqual(output["courier"]["carrier"], "PickMe")

        # Verify Salon content blocks translation (ADR-016)
        blocks = build_lina_blocks(output)
        self.assertTrue(len(blocks) >= 2)

        block_types = [b["type"] for b in blocks]
        self.assertIn("text", block_types)
        self.assertIn("payment", block_types)
        self.assertIn("courier", block_types)

    async def test_settlement_without_a_delivery_address_does_not_crash(self):
        """The non-approval path must survive a missing delivery address.

        `plan_delivery` reports status "skipped" when there is no address, and "skipped" is not a
        courier state (`CourierStatusType` is planned/booked/in_transit/delivered/failed). Validating
        it raised a ValidationError, so every settlement without an address 500'd - the whole happy
        path, and the path an approved HITL deal settles through.
        """
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-no-address",
            "customer_name": "Eleanor",
            "items": [
                {
                    "item_id": "item-101",
                    "item_name": "Cashmere Scarf",
                    "quantity": 1,
                    "unit_price": 20000.0,
                    "wholesale_cost": 5000.0,
                    "total_price": 20000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": None,
            "channel": "whatsapp",
            "message": "Please confirm this order",
        }

        result = await self.graph.ainvoke(state)
        output = result.get("output") or {}

        self.assertEqual(output["status"], "success")
        self.assertFalse(output["needs_approval"])
        self.assertIsNotNone(output["payment"]["url"])
        # No address means no courier plan - not a courier in an invented "skipped" state.
        self.assertIsNone(output["courier"])

    async def test_high_value_order_pauses_for_human_approval(self):
        """The HITL trigger: a breach returns pending_approval rather than settling."""
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-high-value",
            "customer_name": "Eleanor",
            "items": [
                {
                    "item_id": "item-101",
                    "item_name": "Silk Gown",
                    "quantity": 1,
                    "unit_price": 50000.0,
                    "wholesale_cost": 20000.0,
                    "total_price": 50000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": "45 Ward Place, Colombo 07",
            "channel": "whatsapp",
            "message": "I would like to order this gown",
        }

        result = await self.graph.ainvoke(state)
        output = result.get("output") or {}

        self.assertEqual(output["status"], "pending_approval")
        self.assertTrue(output["needs_approval"])
        self.assertEqual(output["approval_type"], "high_value_order")
        self.assertIn("HIGH_VALUE_THRESHOLD_EXCEEDED", output["deal"]["triggered_rules"])
        # A paused deal must not hand out a payment link.
        self.assertIsNone(output["payment"])


if __name__ == "__main__":
    unittest.main()
