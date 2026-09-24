"""Nodes for the Commerce Agent sub-graph (Slice 3 - Lina).

Handles deal pricing, margin calculation, loyalty discount applications,
business rule validations, and the mandatory Human-in-the-Loop (HITL) approval interrupt.
"""

import logging
import math
import re
from typing import Any

from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import HumanMessage, SystemMessage

from app.agents.commerce.state import CommerceAgentState
from app.llm.replies import unwrap_reply
from app.prompts.assembly import assemble_system_prompt
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

#: A pricing question (ADR-028): the same line items, evaluated against the same rules, but the run
#: answers what a discount would be instead of pausing for one. The other value on the wire is
#: ``"order"``, and an absent purpose is read as that, so this is the only one worth naming.
QUOTE_PURPOSE = "quote"

#: Messages that ask what a *discount* may be, as opposed to what a piece costs. Narrow on purpose:
#: this decides whether skipping the deal evaluation is silence or a question left unanswered, and a
#: false positive would answer a discount question nobody asked. The bare noun is the common shape
#: ("how much of a discount can we give this customer?"); the rest are the phrasings staff use when
#: they avoid it.
_DISCOUNT_QUESTION = re.compile(
    r"\bdiscounts?\b"
    r"|\bmark\s?downs?\b"
    r"|\bhow much (?:can|could|would|do|does) (?:we|i|you)\b[^.?!]{0,20}\boff\b"
    r"|\bwhat can (?:we|i) (?:take|knock)\b[^.?!]{0,12}\boff\b"
    r"|\bloyalty (?:cap|rate|discount)\b",
    re.IGNORECASE,
)


def is_discount_question(message: str) -> bool:
    """Whether ``message`` asks what a discount may be, rather than what something costs.

    Read by the graph to decide whether a run with nothing to evaluate may still answer. Commerce's
    only verb used to be "evaluate this deal", so a question about the *policy* - which needs no
    basket, because a discount ceiling is a property of the customer and the house rules - reached
    ``evaluate_deal``, skipped, and produced nothing at all.
    """
    return bool(_DISCOUNT_QUESTION.search(message or ""))


def margin_still_holds(unit_price: float, wholesale_cost: float, discount: float, min_margin: float) -> bool:
    """Whether selling at ``discount`` off ``unit_price`` still leaves ``min_margin``.

    The same arithmetic as ``calculate_margin``, stated once so the ceiling below and the tools
    cannot disagree about what "still holds" means: margin is measured against the *selling* price,
    so a discount lowers the denominator as well as the profit.
    """
    total = unit_price * (1.0 - discount)
    if total <= 0:
        return False
    return (total - wholesale_cost) / total >= min_margin


#: Discount ceilings are quoted to whole percent and rounded **down**.
#:
#: Two reasons, and the second was found by the property test beside this function rather than by
#: review. A ceiling stated to sixteen decimal places is not a number anyone can act on; and the
#: exact algebraic bound lands within one ulp of the floor, so quoting it verbatim promised
#: 73.33333333333334% off a LKR 500 piece whose margin then computed to 24.999999999999983% - a
#: ceiling that disagrees with the very check it exists to describe. Rounding the *claim* down is what
#: makes "comes off without sign-off" true.
_CEILING_STEP = 0.01


def max_discount_for_margin(unit_price: float, wholesale_cost: float, min_margin: float) -> float:
    """The largest whole-percent discount that still clears ``min_margin``, as a rate in ``[0, 1]``.

    ``margin = 1 - cost / (price * (1 - d))``, so ``margin >= m`` rearranges to
    ``d <= 1 - cost / ((1 - m) * price)``. Clamped at zero rather than allowed to go negative: a
    piece already priced under the floor has *no* room, and a negative ceiling would read as "add a
    surcharge", which is not what the rules say.
    """
    if unit_price <= 0 or min_margin >= 1.0:
        return 0.0

    floor = (1.0 - min_margin) * unit_price
    if floor <= 0:
        return 0.0

    exact = max(0.0, min(1.0, 1.0 - wholesale_cost / floor))
    # The epsilon absorbs binary representation, not slack: 0.05 / 0.01 is 5.000000000000001, and
    # without it a tier cap of exactly 5% would be quoted as 4%.
    return math.floor(exact / _CEILING_STEP + 1e-9) * _CEILING_STEP


def _rate(value: Any) -> float:
    """``value`` as a rate in ``[0, 1]``, defaulting to zero.

    Rates arrive from three places - the tier table, the house-rules endpoint and its local fallback -
    and a missing or malformed one must read as "no discount agreed" rather than as a crash on a
    question nobody has a rule for.
    """
    try:
        return min(1.0, max(0.0, float(value)))
    except (TypeError, ValueError):
        return 0.0


def _money(amount: float) -> str:
    """LKR, written the way the rest of the agent writes it."""
    return f"LKR {amount:,.2f}"


def _ceiling_sentence(*, tier: str, cap: float, floor: float, name: str | None) -> str:
    """The answer to "how much of a discount can we give this customer?" (ADR-028).

    Deterministic, and deliberately built from the two figures it was handed rather than from a
    model: a ceiling is a policy number, and the reply must not be able to invent one.
    """
    who = f"{name} is" if name else "This customer is"

    if cap <= 0:
        return (
            f"{who} on the {tier} tier, which carries no standing discount, so any reduction needs "
            f"the owner's sign-off. Every order also has to clear a {floor:.0%} margin floor."
        )

    return (
        f"{who} on the {tier} tier, so up to {cap:.0%} comes off without sign-off. Anything beyond "
        f"that needs the owner, and every order also has to clear a {floor:.0%} margin floor."
    )


def _quote_sentence(
    *,
    items: list[dict[str, Any]],
    tier: str,
    cap: float,
    floor: float,
    name: str | None,
) -> str:
    """What a discount on the named pieces would actually be (ADR-028).

    The tier cap is one bound on a discount and the margin floor is the other, and they bind
    differently per piece: 5% off a piece already priced near cost still loses money. Answering with
    the tier cap alone would be answering a different question, so each piece is priced against both
    and named with the number that actually applies to it.
    """
    who = f"{name} is" if name else "This customer is"
    sentences = [f"{who} on the {tier} tier."]

    for item in items[:5]:
        label = str(item.get("item_name") or "This piece")
        quantity = int(item.get("quantity") or 1)
        unit_price = float(item.get("unit_price") or 0.0)
        line_total = float(item.get("total_price") or unit_price * quantity)
        if quantity > 1:
            label = f"{quantity} x {label}"

        room = max_discount_for_margin(unit_price, float(item.get("wholesale_cost") or 0.0), floor)
        free = min(cap, room)
        amount = line_total * free

        if cap <= 0:
            sentences.append(
                f"{label} at {_money(line_total)} carries no standing discount for this tier, so "
                f"any reduction needs the owner."
            )
        elif free <= 0:
            # True whether the floor is already breached or leaves under a whole percent: a ceiling
            # rounded down to nothing is nothing.
            sentences.append(
                f"{label} at {_money(line_total)} has no whole-percent room under the {floor:.0%} "
                f"margin floor, so any reduction on it needs the owner."
            )
        elif room < cap:
            sentences.append(
                f"{label} at {_money(line_total)} takes {free:.0%} off ({_money(amount)}) without "
                f"sign-off, capped there by the {floor:.0%} margin floor rather than by the tier's "
                f"{cap:.0%}."
            )
        else:
            sentences.append(
                f"{label} at {_money(line_total)} takes the full {free:.0%} off ({_money(amount)}) "
                f"without sign-off."
            )

    return " ".join(sentences)


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

    async def _compose_narrative(
        self,
        state: CommerceAgentState,
        scenario: str,
        fallback: str,
    ) -> tuple[str, dict[str, int] | None]:
        """Generate an LLM-crafted summary narrative and token usage, or return fallback when unavailable."""
        if self.llm is None:
            return fallback, None

        org_id = state.get("org_id", "")
        customer_name = state.get("customer_name") or "the customer"
        total = state.get("total", 0.0)
        subtotal = state.get("subtotal", 0.0)
        margin = state.get("margin", 0.0)
        items = state.get("items") or []
        discount_pct = float(state.get("proposed_discount") or 0.0)
        approval_reason = state.get("approval_reason")
        rejection_reason = state.get("approval_comment")

        item_descriptions = ", ".join(
            f"{i.get('name', 'Garment')} (qty: {i.get('quantity', 1)}, price: LKR {float(i.get('unit_price', 0)):,.2f})"
            for i in items
        ) if items else "No specific line items"

        context_lines = [
            f"Scenario: {scenario}",
            f"Customer: {customer_name}",
            f"Items: {item_descriptions}",
            f"Subtotal: LKR {subtotal:,.2f}",
            f"Discount: {discount_pct:.1%}",
            f"Total: LKR {total:,.2f}",
            f"Profit Margin: {margin:.1%}",
        ]
        if approval_reason:
            context_lines.append(f"Approval Flag / Reason: {approval_reason}")
        if rejection_reason:
            context_lines.append(f"Manager Decision Comment: {rejection_reason}")

        if scenario == "approval_required":
            context_lines.append(
                "Write a concise, commercially articulate note (1-2 sentences) for the boutique manager "
                "explaining why this deal requires approval and highlighting the key figures. "
                "Do NOT emit JSON or code fences, only plain text."
            )
        elif scenario == "rejection":
            context_lines.append(
                "Write a tactful, professional note (1-2 sentences) informing the associate of the manager's "
                "rejection and advising how to follow up with the customer. "
                "Do NOT emit JSON or code fences, only plain text."
            )
        else:  # settlement / finalized
            context_lines.append(
                "Write an elegant, concise deal finalization note (1-2 sentences) confirming the order "
                "total and next steps. "
                "Do NOT emit JSON or code fences, only plain text."
            )

        try:
            system_prompt = assemble_system_prompt("commerce", self.org_context or {"organization_id": org_id})
            user_msg = "\n".join(context_lines)
            llm_res = await self.llm.ainvoke([SystemMessage(content=system_prompt), HumanMessage(content=user_msg)])
            content = getattr(llm_res, "content", None) or ""
            narrative = unwrap_reply(content, fallback=fallback, max_chars=300)

            usage: dict[str, int] | None = None
            meta = getattr(llm_res, "usage_metadata", None) or {}
            if meta:
                usage = {
                    "input_tokens": int(meta.get("input_tokens") or 0),
                    "output_tokens": int(meta.get("output_tokens") or 0),
                }
            return narrative, usage
        except Exception:
            logger.warning("LLM narrative composition failed in CommerceAgent; using fallback.", exc_info=True)
            return fallback, None

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
            # Carried into state so a quote applies the ceiling this evaluation just read, rather
            # than asking for the house rules a second time and risking two different answers.
            "max_allowed_discount": float(rules_res.get("max_allowed_discount") or 0.0),
            "min_required_margin": float(rules_res.get("min_required_margin") or 0.0),
        }

    async def explain_discount_ceiling(self, state: CommerceAgentState) -> dict[str, Any]:
        """Answer what discount may be given when there is no deal to evaluate (ADR-028).

        Every other verb in this agent is a deal verb, so a question about the *policy* - "how much
        of a discount can we give this customer?" - had nothing to answer with: ``evaluate_deal``
        skipped for want of line items and the run went silent, leaving the memory agent's customer
        brief as the only reply to a question about money.

        No basket is needed to answer it. A ceiling is a property of the customer's tier and of the
        house margin floor, and both are read here rather than derived.
        """
        org_id = state.get("org_id")
        if not org_id:
            return self._skip_output("organization context is missing")

        tier_info = await get_customer_loyalty_tier(
            org_id, state.get("customer_id"), registry=self.registry
        )
        tier = str(tier_info.get("tier") or "Regular")
        cap = _rate(tier_info.get("max_allowed_discount"))

        # Thresholds only, so that *reading* the policy cannot trip it: a zero total cannot exceed the
        # high-value threshold, a full margin cannot fall below the floor, and a zero discount cannot
        # exceed a cap. The call therefore comes back with the rules and no triggered rule.
        rules = await validate_business_rules(
            org_id,
            order_total=0.0,
            margin=1.0,
            requested_discount=0.0,
            customer_tier=tier,
            registry=self.registry,
        )
        floor = _rate(rules.get("min_required_margin"))

        summary = _ceiling_sentence(
            tier=tier, cap=cap, floor=floor, name=state.get("customer_name")
        )
        output = CommerceAgentOutput(
            status=SUCCESS,
            summary=summary,
            needs_approval=False,
            action_required="Policy answer only - no order was evaluated or created.",
        )

        return {
            "status": SUCCESS,
            "loyalty_tier": tier,
            "summary": summary,
            "output": output.model_dump(),
        }

    async def present_quote(self, state: CommerceAgentState) -> dict[str, Any]:
        """Price the pieces a *question* named, without committing anything (ADR-028).

        The items are the API's own resolution of what the message named, carrying the catalog's
        price and cost - the only figures the margin rules may be applied to, which is why the quote
        is built here from the wire payload rather than by having Elle look the economics up.

        What a quote deliberately does not do is pause or settle. ``evaluate_deal`` ran and its
        verdict is reported inside the sentence ("even 5% needs the owner"), but nothing is waiting
        on a human, so no approval row is written and no order exists. ``needs_approval`` is False for
        that reason and not because the rules passed - an order at this discount would still need
        sign-off, and the reply says so.
        """
        org_id = state.get("org_id")
        items = [item for item in (state.get("items") or []) if isinstance(item, dict)]
        if not org_id or not items:
            return self._skip_output("no line items to quote")

        tier = str(state.get("loyalty_tier") or "Regular")
        cap = _rate(state.get("max_allowed_discount"))
        floor = _rate(state.get("min_required_margin"))

        summary = _quote_sentence(
            items=items,
            tier=tier,
            cap=cap,
            floor=floor,
            name=state.get("customer_name"),
        )
        output = CommerceAgentOutput(
            status=SUCCESS,
            summary=summary,
            needs_approval=False,
            action_required="Quote only - no order was created.",
        )

        return {
            "status": SUCCESS,
            "summary": summary,
            "output": output.model_dump(),
        }

    def _skip_output(self, reason: str) -> dict[str, Any]:
        """A skipped run whose output is the same envelope every skip uses."""
        return {
            "status": SKIPPED,
            "reason": reason,
            "output": {
                "agent": "commerce",
                "ran": True,
                "status": SKIPPED,
                "reason": reason,
            },
        }

    async def pause_for_approval(self, state: CommerceAgentState) -> dict[str, Any]:
        """Human-in-the-Loop interrupt node when order exceeds business rules thresholds."""
        reason = state.get("approval_reason") or "Order requires manager sign-off"
        approval_type = state.get("approval_type") or "high_value_order"
        total = state.get("total", 0.0)

        fallback_summary = f"Deal requires owner approval: {reason} (Total: LKR {total:,.2f}). Workflow paused."
        summary, usage = await self._compose_narrative(state, "approval_required", fallback_summary)

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
            "usage": usage,
            "output": output.model_dump(),
        }

    async def handle_rejection(self, state: CommerceAgentState) -> dict[str, Any]:
        """Handle deal rejection when the store owner rejects the order on Web Dashboard."""
        reason = state.get("approval_comment") or "Deal was rejected during manager review."
        total = state.get("total", 0.0)
        fallback_summary = f"Deal for LKR {total:,.2f} was rejected by store manager. {reason}"
        summary, usage = await self._compose_narrative(state, "rejection", fallback_summary)

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
            "usage": usage,
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

        # 2. Plan the courier only when there is somewhere to deliver.
        courier_details: CourierDetails | None = None
        if delivery_address:
            courier_dict = await book_courier(
                org_id=org_id,
                order_id=order_id,
                delivery_address=delivery_address,
                registry=self.registry,
            )
            courier_details = CourierDetails.model_validate(courier_dict)

        discount_pct = float(state.get("proposed_discount") or 0.0)
        if discount_pct > 0:
            fallback_summary = f"Deal finalized for {customer_name} with {discount_pct:.0%} discount. Total: LKR {total:,.2f}."
        else:
            fallback_summary = f"Deal finalized for {customer_name}. Total: LKR {total:,.2f}."

        summary, usage = await self._compose_narrative(state, "settlement", fallback_summary)

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
            "courier_details": courier_details.model_dump() if courier_details else None,
            "summary": summary,
            "usage": usage,
            "output": output.model_dump(),
        }

