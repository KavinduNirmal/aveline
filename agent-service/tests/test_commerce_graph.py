"""Unit tests for the Commerce Agent sub-graph (app/agents/commerce/graph.py).

Verifies deal evaluation, auto-approvals, human-in-the-loop approval pauses,
rejection handling, and resumption states. Compatible with unittest and pytest.
"""

import unittest

from _payment_fakes import AnsweringPaymentRegistry

from app.agents.commerce.graph import build_commerce_graph
from app.agents.commerce.state import CommerceAgentState


class TestCommerceGraph(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        # A settled deal needs a backend that actually answers, because the agent no longer invents
        # a checkout link (plan §9.8).
        self.graph = build_commerce_graph(registry=AnsweringPaymentRegistry())
        self.org_id = "org-test-100"

    async def test_approved_deal_settles_with_the_courier_booked(self):
        """A conversational order settles only once the owner has signed it off.

        This used to be "auto approved": a deal under every threshold settled on its own. That is no
        longer the case for an order raised through the conversation - it always queues for owner
        review - so the settlement arm is reached by a decision. `requires_approval` stays true on
        the settled state on purpose: it records that this order needed a decision, which is what
        the audit and the ApprovalQueueEntry history are read for.
        """
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-001",
            "customer_id": "cust-001",
            "customer_name": "Sophia",
            "items": [
                {
                    "item_id": "item-1",
                    "item_name": "Silk Scarf",
                    "quantity": 2,
                    "unit_price": 7500.0,
                    "wholesale_cost": 4000.0,
                    "total_price": 15000.0,
                }
            ],
            "proposed_discount": 0.05,
            "delivery_address": "Colombo 03",
            "channel": "whatsapp",
            "approval_decision": "approved",
        }

        result = await self.graph.ainvoke(state)

        self.assertEqual(result["status"], "success")
        self.assertTrue(result["requires_approval"])
        self.assertEqual(result["total"], 14250.0)

        output = result["output"]
        self.assertEqual(output["status"], "success")
        self.assertFalse(output["needs_approval"])
        self.assertIsNotNone(output["payment"]["url"])
        self.assertEqual(output["courier"]["carrier"], "PickMe")

    async def test_high_value_order_triggers_approval_pause(self):
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-002",
            "customer_id": "cust-002",
            "customer_name": "Alexander",
            "items": [
                {
                    "item_id": "item-2",
                    "item_name": "Designer Tuxedo",
                    "quantity": 1,
                    "unit_price": 85000.0,  # > 40,000 threshold
                    "wholesale_cost": 45000.0,
                    "total_price": 85000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": "Colombo 07",
            "channel": "in_store",
        }

        result = await self.graph.ainvoke(state)

        self.assertEqual(result["status"], "pending_approval")
        self.assertTrue(result["requires_approval"])

        output = result["output"]
        self.assertEqual(output["status"], "pending_approval")
        self.assertTrue(output["needs_approval"])
        self.assertEqual(output["approval_type"], "high_value_order")
        self.assertIn("HIGH_VALUE_THRESHOLD_EXCEEDED", output["deal"]["triggered_rules"])
        self.assertIsNone(output["payment"])

    async def test_a_conversational_order_always_queues_for_owner_review(self):
        """The gate that made this file's other tests stale, pinned in its own right.

        Every order raised through the conversation queues for owner review, even one under every
        threshold. This is the deliberate counterpart to ADR-024's threshold rule rather than a
        replacement for it: the pause still carries whichever rule was breached (asserted by the two
        tests above), and this is the floor underneath them.
        """
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-004",
            "customer_id": "cust-004",
            "customer_name": "Sophia",
            # Under every threshold: total 14,250 < 40,000, margin 43.9% > 25%, discount 5% = cap.
            "items": [
                {
                    "item_id": "item-1",
                    "item_name": "Silk Scarf",
                    "quantity": 2,
                    "unit_price": 7500.0,
                    "wholesale_cost": 4000.0,
                    "total_price": 15000.0,
                }
            ],
            "proposed_discount": 0.05,
            "delivery_address": "Colombo 03",
            "channel": "whatsapp",
        }

        result = await self.graph.ainvoke(state)

        self.assertEqual(result["status"], "pending_approval")
        output = result["output"]
        self.assertEqual(output["approval_type"], "order_approval")
        self.assertEqual(output["deal"]["triggered_rules"], [])
        # The reason names the review rather than a rule, because no rule was breached.
        self.assertIn("owner review", output["approval_reason"])
        # A pause is a pause: no link is minted for a deal nobody has signed off.
        self.assertIsNone(output["payment"])

    async def test_low_margin_order_triggers_approval_pause(self):
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-003",
            "customer_id": "cust-003",
            "items": [
                {
                    "item_id": "item-3",
                    "item_name": "Clearance Bag",
                    "quantity": 1,
                    "unit_price": 10000.0,
                    "wholesale_cost": 8500.0,  # Margin = 15% (< 25% minimum)
                    "total_price": 10000.0,
                }
            ],
            "proposed_discount": 0.0,
        }

        result = await self.graph.ainvoke(state)

        self.assertEqual(result["status"], "pending_approval")
        self.assertTrue(result["requires_approval"])

        output = result["output"]
        self.assertEqual(output["approval_type"], "low_margin")
        self.assertIn("LOW_MARGIN_THRESHOLD", output["deal"]["triggered_rules"])

    async def test_resume_workflow_when_owner_approved(self):
        # Simulating resumed state where owner approved the high-value order
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-002",
            "customer_name": "Alexander",
            "items": [
                {
                    "item_id": "item-2",
                    "item_name": "Designer Tuxedo",
                    "quantity": 1,
                    "unit_price": 85000.0,
                    "wholesale_cost": 45000.0,
                    "total_price": 85000.0,
                }
            ],
            "proposed_discount": 0.0,
            "delivery_address": "Colombo 07",
            "approval_decision": "approved",
            "approval_comment": "Owner approved via Web Dashboard",
        }

        result = await self.graph.ainvoke(state)

        self.assertEqual(result["status"], "success")
        output = result["output"]
        self.assertEqual(output["status"], "success")
        self.assertFalse(output["needs_approval"])
        self.assertIsNotNone(output["payment"]["url"])

    async def test_resume_workflow_when_owner_rejected(self):
        # Simulating resumed state where owner rejected the high-value order
        state: CommerceAgentState = {
            "org_id": self.org_id,
            "order_id": "ord-002",
            "items": [
                {
                    "item_id": "item-2",
                    "item_name": "Designer Tuxedo",
                    "quantity": 1,
                    "unit_price": 85000.0,
                    "wholesale_cost": 45000.0,
                    "total_price": 85000.0,
                }
            ],
            "approval_decision": "rejected",
            "approval_comment": "Stock reserved for showroom display.",
        }

        result = await self.graph.ainvoke(state)

        self.assertEqual(result["status"], "rejected")
        output = result["output"]
        self.assertEqual(output["status"], "rejected")
        self.assertIn("Stock reserved", output["reason"])
        self.assertIsNone(output["payment"])

    async def test_missing_org_context_skips(self):
        state: CommerceAgentState = {
            "org_id": "",
            "items": [],
        }

        result = await self.graph.ainvoke(state)
        self.assertEqual(result["status"], "skipped")


if __name__ == "__main__":
    unittest.main()
