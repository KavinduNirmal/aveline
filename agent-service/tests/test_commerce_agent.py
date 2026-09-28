"""End-to-end tests for Commerce Agent (Lina) and Salon block translations.

Tests full deal workflows, Salon block translations via build_lina_blocks,
and concierge envelope outputs. Compatible with unittest and pytest.
"""

import json
import unittest

from _payment_fakes import AnsweringPaymentRegistry

from app.agents.commerce.graph import build_commerce_graph
from app.agents.commerce.state import CommerceAgentState
from app.events.block_builders import build_lina_blocks


class TestCommerceAgentE2E(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.graph = build_commerce_graph(registry=AnsweringPaymentRegistry())
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

    async def test_an_unreachable_payment_backend_errors_instead_of_handing_out_a_link(self):
        """The removed fabrication, asserted end to end (plan §9.8).

        A provider that cannot be reached must abort the settlement: an error the associate can see
        beats a checkout URL that leads nowhere. The courier is not booked either, because no sale
        happened.
        """
        graph = build_commerce_graph(
            registry=AnsweringPaymentRegistry(create_error=RuntimeError("connection refused"))
        )
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-provider-down",
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
            "delivery_address": "45 Ward Place, Colombo 07",
            "channel": "whatsapp",
            "message": "Please confirm this order",
        }

        result = await graph.ainvoke(state)
        output = result.get("output") or {}

        self.assertEqual(output["status"], "error")
        self.assertIsNone(output["payment"])
        self.assertIsNone(output["courier"])
        self.assertIsNone(result.get("payment_details"))
        self.assertIn("could not be reached", output["reason"])
        self.assertNotIn("pay.aveline.boutique", json.dumps(output))

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

    async def test_llm_produces_deal_narrative_and_usage_when_injected(self):
        fake_llm = FakeCommerceChatModel(reply="Lina has prepared the private order confirmation.")
        graph = build_commerce_graph(registry=AnsweringPaymentRegistry(), llm=fake_llm)
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-llm-1",
            "customer_name": "Eleanor",
            "items": [
                {
                    "item_id": "item-101",
                    "item_name": "Cashmere Scarf",
                    "quantity": 1,
                    "unit_price": 10000.0,
                    "wholesale_cost": 4000.0,
                    "total_price": 10000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": "45 Ward Place, Colombo 07",
            "channel": "whatsapp",
            "message": "Order 1 scarf please",
        }

        result = await graph.ainvoke(state)
        output = result.get("output") or {}

        self.assertEqual(output["status"], "success")
        self.assertEqual(output["summary"], "Lina has prepared the private order confirmation.")
        self.assertEqual(result.get("usage"), {"input_tokens": 15, "output_tokens": 8})
        self.assertEqual(len(fake_llm.invoked_messages), 1)
        # Verify Lina prompt was assembled and passed as SystemMessage
        system_msg = fake_llm.invoked_messages[0][0]
        self.assertIn("Lina", system_msg.content)
        self.assertIn("commercially astute", system_msg.content)

    async def test_llm_failure_falls_back_to_template_without_crashing(self):
        fake_llm = FakeCommerceChatModel(fail=True)
        graph = build_commerce_graph(registry=AnsweringPaymentRegistry(), llm=fake_llm)
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-llm-fail",
            "customer_name": "Eleanor",
            "items": [
                {
                    "item_id": "item-101",
                    "item_name": "Cashmere Scarf",
                    "quantity": 1,
                    "unit_price": 10000.0,
                    "wholesale_cost": 4000.0,
                    "total_price": 10000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": "45 Ward Place, Colombo 07",
            "channel": "whatsapp",
            "message": "Order 1 scarf please",
        }

        result = await graph.ainvoke(state)
        output = result.get("output") or {}

        self.assertEqual(output["status"], "success")
        self.assertTrue(output["summary"].startswith("Deal finalized for Eleanor"))
        self.assertIsNone(result.get("usage"))

    async def test_llm_json_envelope_is_unwrapped_to_plain_text(self):
        fake_llm = FakeCommerceChatModel(reply='{"reply": "Unwrapped bespoke order summary for VIP."}')
        graph = build_commerce_graph(registry=AnsweringPaymentRegistry(), llm=fake_llm)
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-llm-json",
            "customer_name": "Eleanor",
            "items": [
                {
                    "item_id": "item-101",
                    "item_name": "Cashmere Scarf",
                    "quantity": 1,
                    "unit_price": 10000.0,
                    "wholesale_cost": 4000.0,
                    "total_price": 10000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": "45 Ward Place, Colombo 07",
            "channel": "whatsapp",
            "message": "Order 1 scarf please",
        }

        result = await graph.ainvoke(state)
        output = result.get("output") or {}

        self.assertEqual(output["summary"], "Unwrapped bespoke order summary for VIP.")


class _Msg:
    def __init__(self, content: str, input_tokens: int = 15, output_tokens: int = 8):
        self.content = content
        self.usage_metadata = {"input_tokens": input_tokens, "output_tokens": output_tokens}


class FakeCommerceChatModel:
    """Minimal chat-model double exposing only ainvoke."""

    def __init__(self, reply: str = "Lina's bespoke deal narrative.", fail: bool = False):
        self.reply = reply
        self.fail = fail
        self.invoked_messages = []

    async def ainvoke(self, messages):
        self.invoked_messages.append(messages)
        if self.fail:
            raise RuntimeError("provider unavailable")
        return _Msg(self.reply)


if __name__ == "__main__":
    unittest.main()
