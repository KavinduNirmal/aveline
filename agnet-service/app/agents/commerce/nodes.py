"""Nodes for the Commerce Agent sub-graph (Slice 3 - Lina).

Handles deal pricing, margin calculation, loyalty discount applications,
business rule validations, and the mandatory Human-in-the-Loop (HITL) approval interrupt.
"""

import logging
from typing import Any

from langchain_core.language_models.chat_models import BaseChatModel

from app.agents.commerce.state import CommerceAgentState
from app.schemas.commerce import (
    CommerceAgentOutput,
    CourierDetails,
    DealEvaluation,
    PaymentDetails,
)
from app.tools.commerce.delivery_tools import book_courier
from app.tools.commerce.loyalty_tools import get_customer_loyalty_tier
from app.tools.commerce.payment_tools import generate_payment_request
from app.tools.commerce.pricing_tools import apply_discount, calculate_margin
from app.tools.commerce.rules_tools import validate_business_rules

logger = logging.getLogger("aveline.agent.commerce")

SUCCESS = "success"
PENDING_APPROVAL = "pending_approval"
REJECTED = "rejected"
SKIPPED = "skipped"
ERROR = "error"


def coerce_commerce_output(output: dict[str, Any]) -> CommerceAgentOutput | None:
    """Validate assembled output dict against the strict CommerceAgentOutput schema."""
    try:
        return CommerceAgentOutput.model_validate(output)
    except Exception:
        logger.error("Commerce agent output failed schema validation.", exc_info=True)
        return None


class CommerceAgent:
    """LangGraph node set for the Commerce Agent, bound to a backend ToolRegistry."""

    def __init__(
        self,
        registry: Any = None,
        llm: BaseChatModel | None = None,
        org_context: dict[str, Any] | None = None,
    ) -> None:
        self.registry = registry
        self.llm = llm
        self.org_context = org_context or {}

    async def evaluate_deal(self, state: CommerceAgentState) -> dict[str, Any]:
        """Evaluate line items, profit margin, loyalty discounts, and business rules."""
        org_id = state.get("org_id")
        if not org_id:
            return {
                "status": SKIPPED,
                "reason": "organization context is missing",
                "output": {
                    "agent": "commerce",
                    "ran": True,
                    "status": SKIPPED,
                    "reason": "organization context is missing",
                },
            }

        items = state.get("items") or []
        proposed_discount = float(state.get("proposed_discount") or 0.0)
        customer_id = state.get("customer_id")

        # 1. Tally line items and costs
        if not items:
            return {
                "subtotal": 0.0,
                "discount_amount": 0.0,
                "total": 0.0,
                "total_cost": 0.0,
                "margin": 0.0,
                "loyalty_tier": "Regular",
                "is_auto_approved": True,
                "requires_approval": False,
                "approval_type": None,
                "approval_reason": None,
                "triggered_rules": [],
                "flags": [],
                "status": SKIPPED,
                "output": {
                    "agent": "commerce",
                    "ran": True,
                    "status": SKIPPED,
                    "reason": "no items in order context to evaluate",
                },
            }

        subtotal = sum(float(item.get("total_price") or (float(item.get("unit_price", 0.0)) * int(item.get("quantity", 1)))) for item in items)
        total_cost = sum(float(item.get("wholesale_cost", 0.0)) * int(item.get("quantity", 1)) for item in items)

        # 2. Look up customer loyalty tier
        tier_info = await get_customer_loyalty_tier(org_id, customer_id, registry=self.registry)
        tier = tier_info.get("tier", "Regular")

        # 3. Apply discount & calculate margin
        discount_res = apply_discount(subtotal, proposed_discount)
        total = discount_res["total"]
        discount_amount = discount_res["discount_amount"]

        margin_res = calculate_margin(total, total_cost)
        margin = margin_res["margin"]

        # 4. Check business rules
        rules_res = await validate_business_rules(
            org_id=org_id,
            order_total=total,
            margin=margin,
            requested_discount=proposed_discount,
            customer_tier=tier,
            registry=self.registry,
        )

        requires_approval = rules_res["requires_approval"]
        is_auto_approved = rules_res["is_auto_approved"]
        triggered_rules = rules_res["triggered_rules"]
        flags = rules_res["flags"]

        approval_type: str | None = None
        approval_reason: str | None = None

        if requires_approval:
            if "HIGH_VALUE_THRESHOLD_EXCEEDED" in triggered_rules:
                approval_type = "high_value_order"
                approval_reason = f"Order total LKR {total:,.2f} exceeds high-value threshold of LKR 40,000"
            elif "LOW_MARGIN_THRESHOLD" in triggered_rules:
                approval_type = "low_margin"
                approval_reason = f"Order margin {margin:.1%} is below minimum requirement of 25%"
            elif "DISCOUNT_LIMIT_EXCEEDED" in triggered_rules:
                approval_type = "discount"
                approval_reason = f"Requested discount {proposed_discount:.1%} exceeds {tier} tier cap"

        logger.info(
            "Evaluated deal for org %s: subtotal=%.2f total=%.2f margin=%.4f requires_approval=%s",
            org_id, subtotal, total, margin, requires_approval
        )

        return {
            "subtotal": subtotal,
            "discount_amount": discount_amount,
            "total": total,
            "total_cost": total_cost,
            "margin": margin,
            "loyalty_tier": tier,
            "is_auto_approved": is_auto_approved,
            "requires_approval": requires_approval,
            "approval_type": approval_type,
            "approval_reason": approval_reason,
            "triggered_rules": triggered_rules,
            "flags": flags,
        }

    async def pause_for_approval(self, state: CommerceAgentState) -> dict[str, Any]:
        """Human-in-the-Loop interrupt node when order exceeds business rules thresholds."""
        reason = state.get("approval_reason") or "Order requires manager sign-off"
        approval_type = state.get("approval_type") or "high_value_order"
        total = state.get("total", 0.0)

        summary = f"Deal requires owner approval: {reason} (Total: LKR {total:,.2f}). Workflow paused."

        deal_eval = DealEvaluation(
            subtotal=state.get("subtotal", 0.0),
            discount_amount=state.get("discount_amount", 0.0),
            total=total,
            total_cost=state.get("total_cost", 0.0),
            margin=state.get("margin", 0.0),
            loyalty_tier=state.get("loyalty_tier"),
            applied_discount_percent=float(state.get("proposed_discount") or 0.0),
            is_auto_approved=False,
            requires_approval=True,
            triggered_rules=state.get("triggered_rules") or [],
            flags=state.get("flags") or [],
        )

        output = CommerceAgentOutput(
            status=PENDING_APPROVAL,
            summary=summary,
            needs_approval=True,
            approval_type=approval_type,
            approval_reason=reason,
            deal=deal_eval,
            payment=None,
            courier=None,
            action_required="Owner/Manager sign-off required in Approval Queue",
            reason=reason,
        )

        return {
            "status": PENDING_APPROVAL,
            "requires_approval": True,
            "summary": summary,
            "output": output.model_dump(),
        }

    async def handle_rejection(self, state: CommerceAgentState) -> dict[str, Any]:
        """Handle deal rejection when the store owner rejects the order on Web Dashboard."""
        reason = state.get("approval_comment") or "Deal was rejected during manager review."
        total = state.get("total", 0.0)
        summary = f"Deal for LKR {total:,.2f} was rejected by store manager. {reason}"

        deal_eval = DealEvaluation(
            subtotal=state.get("subtotal", 0.0),
            discount_amount=state.get("discount_amount", 0.0),
            total=total,
            total_cost=state.get("total_cost", 0.0),
            margin=state.get("margin", 0.0),
            loyalty_tier=state.get("loyalty_tier"),
            applied_discount_percent=float(state.get("proposed_discount") or 0.0),
            is_auto_approved=False,
            requires_approval=False,
            triggered_rules=state.get("triggered_rules") or [],
            flags=state.get("flags") or [],
        )

        output = CommerceAgentOutput(
            status=REJECTED,
            summary=summary,
            needs_approval=False,
            approval_type=state.get("approval_type"),
            approval_reason=reason,
            deal=deal_eval,
            payment=None,
            courier=None,
            action_required="Please revise discount or order terms with customer.",
            reason=reason,
        )

        return {
            "status": REJECTED,
            "summary": summary,
            "output": output.model_dump(),
        }

    async def prepare_settlement(self, state: CommerceAgentState) -> dict[str, Any]:
        """Generate checkout link and book courier for approved orders."""
        org_id = state.get("org_id", "")
        order_id = state.get("order_id")
        total = state.get("total", 0.0)
        customer_name = state.get("customer_name") or "Customer"
        delivery_address = state.get("delivery_address")

        # 1. Generate payment request
        payment_dict = await generate_payment_request(
            org_id=org_id,
            order_id=order_id,
            amount=total,
            registry=self.registry,
        )
        payment_details = PaymentDetails.model_validate(payment_dict)

        # 2. Book courier if delivery address provided
        courier_dict = await book_courier(
            org_id=org_id,
            order_id=order_id,
            delivery_address=delivery_address,
            registry=self.registry,
        )
        courier_details = CourierDetails.model_validate(courier_dict)

        discount_pct = float(state.get("proposed_discount") or 0.0)
        if discount_pct > 0:
            summary = f"Deal finalized for {customer_name} with {discount_pct:.0%} discount. Total: LKR {total:,.2f}."
        else:
            summary = f"Deal finalized for {customer_name}. Total: LKR {total:,.2f}."

        deal_eval = DealEvaluation(
            subtotal=state.get("subtotal", total),
            discount_amount=state.get("discount_amount", 0.0),
            total=total,
            total_cost=state.get("total_cost", 0.0),
            margin=state.get("margin", 0.0),
            loyalty_tier=state.get("loyalty_tier"),
            applied_discount_percent=discount_pct,
            is_auto_approved=state.get("is_auto_approved", True),
            requires_approval=False,
            triggered_rules=state.get("triggered_rules") or [],
            flags=state.get("flags") or [],
        )

        output = CommerceAgentOutput(
            status=SUCCESS,
            summary=summary,
            needs_approval=False,
            deal=deal_eval,
            payment=payment_details,
            courier=courier_details,
            action_required=f"Share payment link with {customer_name}: {payment_details.url}" if payment_details.url else None,
        )

        return {
            "status": SUCCESS,
            "payment_details": payment_details.model_dump(),
            "courier_details": courier_details.model_dump(),
            "summary": summary,
            "output": output.model_dump(),
        }
