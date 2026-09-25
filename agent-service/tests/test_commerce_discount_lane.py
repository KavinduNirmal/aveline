"""Tests for the two read-only commerce arms (ADR-028).

Commerce's only verb used to be "evaluate this deal". A question about a discount reached
``evaluate_deal``, skipped for want of line items, and produced nothing - so a staff message asking
what discount could be given on a named dress was answered by the memory agent's customer brief and
by nobody else.

Two arms answer instead, and neither may commit anything:

- ``explain_discount_ceiling``: what the tier and the house rules allow, needing no basket at all.
- ``present_quote``: what a discount on the *named* pieces would be, from the API's own resolution of
  them and the catalog's prices and costs.

The arithmetic is pinned against the rules it mirrors rather than against a literal, because the
whole risk here is a ceiling that disagrees with the margin check it is supposed to describe.
"""

import pytest
from _payment_fakes import AnsweringPaymentRegistry

from app.agents.commerce.graph import build_commerce_graph
from app.agents.commerce.nodes import (
    is_discount_question,
    margin_still_holds,
    max_discount_for_margin,
)

ORG = "01a078bf-45c1-7231-bae1-3e0ea1dc0471"
CUSTOMER = "01a0cb20-96f0-7c72-9823-98f59781c679"

#: The piece from the reported thread: LKR 10,000 at a LKR 9,500 cost, so a 5% margin - already under
#: the 25% floor before any discount is considered.
CHAMPAGNE = {
    "item_id": "b7f1c1a4-0000-4000-8000-000000000003",
    "item_name": "Champagne Rose Garden Satin Midi Dress",
    "quantity": 1,
    "unit_price": 10000.0,
    "wholesale_cost": 9500.0,
    "total_price": 10000.0,
}

#: Priced so the 25% floor binds *below* the 5% tier cap: room is 1 - 7300 / 7500 = 2.67%.
TIGHT_MARGIN = {
    "item_id": "b7f1c1a4-0000-4000-8000-000000000004",
    "item_name": "Champagne Rose Garden Satin Midi Dress",
    "quantity": 1,
    "unit_price": 10000.0,
    "wholesale_cost": 7300.0,
    "total_price": 10000.0,
}

#: Room to spare: the tier cap is what binds.
HEALTHY_MARGIN = {
    "item_id": "b7f1c1a4-0000-4000-8000-000000000005",
    "item_name": "Fuchsia Pink Bodycon Mini Dress",
    "quantity": 1,
    "unit_price": 12550.0,
    "wholesale_cost": 1000.0,
    "total_price": 12550.0,
}

FLOOR = 0.25
CAP = 0.05


def _graph():
    # The settlement arm needs a backend that answers; the read-only arms never call payment, so the
    # same double is harmless for them (plan §9.8).
    return build_commerce_graph(registry=AnsweringPaymentRegistry())


# --------------------------------------------------------------------------- the question
@pytest.mark.parametrize(
    "message",
    [
        "How much of a discount can we give this customer?",
        "can we give a discount on this?",
        "What markdown can we do?",
        "how much can we knock off?",
        "what is our loyalty cap?",
    ],
)
def test_is_discount_question_reads_the_policy_questions(message):
    assert is_discount_question(message) is True


@pytest.mark.parametrize(
    "message",
    [
        "",
        "How much is the pink dress?",
        "I want to buy the emerald saree",
        "Do you have the pink dress in a size 8?",
    ],
)
def test_is_discount_question_leaves_everything_else_alone(message):
    """A false positive would answer a discount question nobody asked."""
    assert is_discount_question(message) is False


# --------------------------------------------------------------------------- the arithmetic
def test_max_discount_for_margin_is_zero_when_the_floor_is_already_breached():
    # At full price the margin is 5%, so there is no discount - however small - that holds.
    assert max_discount_for_margin(10000.0, 9500.0, FLOOR) == 0.0


def test_max_discount_for_margin_rounds_the_claim_down_to_a_whole_percent():
    """The exact bound is 2.67%; the ceiling is 2%, and the margin holds at what is claimed.

    Asserting the property rather than the literal is the point: the unrounded bound sits one ulp
    inside the floor, so this pair disagreed until the ceiling was rounded down.
    """
    room = max_discount_for_margin(10000.0, 7300.0, FLOOR)

    assert room == pytest.approx(0.02)
    assert margin_still_holds(10000.0, 7300.0, room, FLOOR) is True
    # One whole percent more does not. This is the assertion that keeps the two in step.
    assert margin_still_holds(10000.0, 7300.0, room + 0.01, FLOOR) is False


@pytest.mark.parametrize(
    ("price", "cost"),
    [(12550.0, 1000.0), (75000.0, 1000.0), (500.0, 100.0), (10000.0, 7500.0)],
)
def test_the_ceiling_never_claims_a_discount_the_rules_would_reject(price, cost):
    room = max_discount_for_margin(price, cost, FLOOR)

    assert 0.0 <= room <= 1.0
    if room > 0:
        assert margin_still_holds(price, cost, room, FLOOR) is True
        assert margin_still_holds(price, cost, min(1.0, room + 0.01), FLOOR) is False


def test_a_piece_with_no_cost_leaves_the_tier_cap_as_the_binding_bound():
    assert max_discount_for_margin(15000.0, 0.0, FLOOR) == 1.0


@pytest.mark.parametrize(("price", "cost"), [(0.0, 0.0), (-1.0, 0.0)])
def test_a_priceless_piece_has_no_room(price, cost):
    assert max_discount_for_margin(price, cost, FLOOR) == 0.0


# --------------------------------------------------------------------------- the ceiling arm
async def test_a_discount_question_with_no_basket_is_answered_from_the_tier_and_the_rules():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "customer_id": CUSTOMER,
            "customer_name": "Kasha Vivian Perera",
            "items": [],
            "staff_query": True,
            "message": "How much of a discount can we give this customer?",
        }
    )

    output = result["output"]
    assert result["status"] == "success"
    assert output["status"] == "success"
    # Nothing is awaiting a human: this is a policy answer, not a paused deal.
    assert output["needs_approval"] is False
    assert output["payment"] is None
    assert output["courier"] is None
    assert "Kasha Vivian Perera" in output["summary"]
    assert "5%" in output["summary"]
    assert "25%" in output["summary"]


async def test_a_ceiling_with_no_customer_names_the_tier_rather_than_inventing_one():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "items": [],
            "staff_query": True,
            "message": "what discount can we give?",
        }
    )

    assert "This customer is" in result["output"]["summary"]


# --------------------------------------------------------------------------- the quote arm
async def test_a_quote_prices_the_named_piece_without_pausing_or_settling():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "customer_id": CUSTOMER,
            "customer_name": "Kasha Vivian Perera",
            "items": [CHAMPAGNE],
            "purpose": "quote",
            "staff_query": True,
            "message": "How much of a discount can we give this customer for the dress?",
        }
    )

    output = result["output"]
    assert result["status"] == "success"
    assert output["needs_approval"] is False
    assert output["payment"] is None
    assert output["courier"] is None
    assert output["approval_type"] is None
    # The answer names the piece, its price, and the reason there is no room on it.
    assert "Champagne Rose Garden Satin Midi Dress" in output["summary"]
    assert "LKR 10,000.00" in output["summary"]
    assert "25%" in output["summary"]


async def test_a_quote_whose_piece_has_room_reports_the_room_it_has():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "items": [TIGHT_MARGIN],
            "purpose": "quote",
            "staff_query": True,
            "message": "how much of a discount on the dress?",
        }
    )

    summary = result["output"]["summary"]
    # The floor caps it at 2%, below the 5% the tier allows, and the sentence says which bound bit.
    assert "LKR 200.00" in summary
    assert "margin floor" in summary


async def test_a_quote_where_the_tier_cap_binds_reports_the_tier_cap():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "items": [HEALTHY_MARGIN],
            "purpose": "quote",
            "staff_query": True,
            "message": "how much of a discount on the dress?",
        }
    )

    summary = result["output"]["summary"]
    assert "5%" in summary
    assert "LKR 627.50" in summary  # 5% of LKR 12,550.00


async def test_a_quote_is_never_a_pause_even_when_the_rules_would_pause_an_order():
    """The load-bearing difference: the same items, evaluated, and still not a commitment."""
    order = await _graph().ainvoke(
        {
            "org_id": ORG,
            "customer_id": CUSTOMER,
            "items": [CHAMPAGNE],
            "purpose": "order",
            "message": "buy the dress",
        }
    )
    quote = await _graph().ainvoke(
        {
            "org_id": ORG,
            "customer_id": CUSTOMER,
            "items": [CHAMPAGNE],
            "purpose": "quote",
            "staff_query": True,
            "message": "how much is the dress?",
        }
    )

    assert order["status"] == "pending_approval"
    assert order["output"]["needs_approval"] is True
    assert quote["output"]["needs_approval"] is False


async def test_a_customer_quote_stays_silent_because_the_ceiling_is_the_house_s_own_policy():
    """A quote is shown to staff and never to a customer, whose price Elle answers separately."""
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "customer_id": CUSTOMER,
            "items": [CHAMPAGNE],
            "purpose": "quote",
            "staff_query": False,
            "message": "how much is this dress?",
        }
    )

    assert result.get("output") is None
    assert result.get("summary") is None


async def test_a_quote_whose_items_never_resolved_falls_back_to_the_ceiling():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "items": [],
            "purpose": "quote",
            "staff_query": True,
            "message": "how much of a discount can we give?",
        }
    )

    assert result["status"] == "success"
    assert "tier" in result["output"]["summary"]


# --------------------------------------------------------------------------- unchanged behaviour
async def test_a_no_items_skip_that_is_not_a_discount_question_is_still_silent():
    result = await _graph().ainvoke(
        {
            "org_id": ORG,
            "items": [],
            "message": "Do you have the emerald green saree?",
        }
    )

    assert result["status"] == "skipped"
    assert result["output"]["reason"] == "no items in order context to evaluate"


async def test_an_order_still_settles_and_pauses_exactly_as_before():
    settled = await _graph().ainvoke(
        {
            "org_id": ORG,
            "order_id": "ord-1",
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
            "message": "I'll take two silk scarves",
        }
    )

    assert settled["status"] == "success"
    assert settled["output"]["payment"] is not None


async def test_margin_floor_is_read_from_the_rules_rather_than_assumed():
    """The ceiling quotes the floor it was given: a house rule change must move both numbers."""
    import app.agents.commerce.nodes as nodes

    captured: dict[str, object] = {}
    original = nodes.validate_business_rules

    async def fake_rules(org_id, *, order_total, margin, requested_discount, customer_tier, registry):
        captured["order_total"] = order_total
        captured["margin"] = margin
        captured["requested_discount"] = requested_discount
        return {
            "requires_approval": False,
            "is_auto_approved": True,
            "triggered_rules": [],
            "flags": [],
            "max_allowed_discount": 0.10,
            "min_required_margin": 0.40,
            "high_value_threshold": 40000.0,
        }

    nodes.validate_business_rules = fake_rules
    try:
        result = await _graph().ainvoke(
            {
                "org_id": ORG,
                "items": [],
                "message": "what discount can we give?",
            }
        )
    finally:
        nodes.validate_business_rules = original

    assert "40%" in result["output"]["summary"]
    # Reading the policy must not trip it: zero total, full margin, no requested discount.
    assert captured == {"order_total": 0.0, "margin": 1.0, "requested_discount": 0.0}
