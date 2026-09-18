"""Tests for Commerce Agent TypedState (app/agents/commerce/state.py).

Verifies state initialization, typed dictionary fields, and JSON-serializability
required for LangGraph Postgres checkpointing (ADR-002). Compatible with pytest and unittest.
"""

import json
import unittest

from app.agents.commerce.state import CommerceAgentState


class TestCommerceAgentState(unittest.TestCase):
    def test_state_initialization_and_assignment(self):
        state: CommerceAgentState = {
            "org_id": "org-123",
            "order_id": "ord-456",
            "customer_id": "cust-789",
            "customer_name": "Sophia Bennett",
            "items": [
                {
                    "item_id": "item-1",
                    "item_name": "Evening Silk Gown",
                    "quantity": 1,
                    "unit_price": 45000.0,
                    "wholesale_cost": 25000.0,
                    "total_price": 45000.0,
                }
            ],
            "proposed_discount": 0.10,
            "delivery_address": "77 Flower Road, Colombo 07",
            "channel": "whatsapp",
            "message": "Can I get this gown with 10% VIP discount?",
            "direction": "inbound",
            "staff_query": False,
        }

        self.assertEqual(state["org_id"], "org-123")
        self.assertEqual(state["order_id"], "ord-456")
        self.assertEqual(len(state["items"]), 1)
        self.assertEqual(state["proposed_discount"], 0.10)

    def test_state_working_fields_and_serializability(self):
        state: CommerceAgentState = {
            "org_id": "org-123",
            "subtotal": 45000.0,
            "discount_amount": 4500.0,
            "total": 40500.0,
            "total_cost": 25000.0,
            "margin": 0.3827,
            "loyalty_tier": "VIP",
            "is_auto_approved": False,
            "requires_approval": True,
            "approval_type": "high_value_order",
            "approval_reason": "Order total LKR 40,500 exceeds high value threshold LKR 40,000",
            "triggered_rules": ["HIGH_VALUE_THRESHOLD_EXCEEDED"],
            "flags": ["High-value order requires manager sign-off"],
            "approval_decision": "approved",
            "approval_comment": "Approved by store owner on Web Dashboard",
            "thread_id": "thread-check-999",
            "payment_details": {
                "amount": 40500.0,
                "status": "pending",
                "url": "https://payhere.lk/pay/inv99",
            },
            "courier_details": {
                "carrier": "PickMe",
                "status": "planned",
                "delivery_address": "77 Flower Road, Colombo 07",
            },
            "summary": "Order approved with 10% VIP discount. Total: LKR 40,500",
            "status": "success",
            "output": {
                "agent": "commerce",
                "ran": True,
                "status": "success",
                "summary": "Order approved with 10% VIP discount. Total: LKR 40,500",
            },
        }

        # Verify JSON serializability for LangGraph Postgres checkpointer (ADR-002)
        serialized = json.dumps(state)
        deserialized = json.loads(serialized)

        self.assertEqual(deserialized["org_id"], "org-123")
        self.assertEqual(deserialized["total"], 40500.0)
        self.assertEqual(deserialized["approval_decision"], "approved")
        self.assertEqual(deserialized["triggered_rules"], ["HIGH_VALUE_THRESHOLD_EXCEEDED"])
        self.assertEqual(deserialized["payment_details"]["status"], "pending")


if __name__ == "__main__":
    unittest.main()
