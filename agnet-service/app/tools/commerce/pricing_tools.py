"""Pricing and profit margin calculation tools for the Commerce Agent (Slice 3)."""

import logging
from typing import Any

logger = logging.getLogger("aveline.agent.tools.commerce.pricing")


def calculate_margin(subtotal: float, total_cost: float) -> dict[str, float]:
    """Compute gross profit and margin ratio based on revenue and wholesale cost.

    Args:
        subtotal: Gross order amount after discounts in LKR.
        total_cost: Total acquisition / wholesale cost of goods in LKR.

    Returns:
        Dictionary containing gross_profit and margin (ratio between 0.0 and 1.0).
    """
    if subtotal <= 0:
        return {"gross_profit": 0.0, "margin": 0.0}

    gross_profit = round(subtotal - total_cost, 2)
    margin = round(gross_profit / subtotal, 4)
    logger.debug("Computed margin: revenue=%.2f cost=%.2f margin=%.4f", subtotal, total_cost, margin)
    return {"gross_profit": gross_profit, "margin": margin}


def apply_discount(subtotal: float, discount_percent: float) -> dict[str, float]:
    """Calculate discount deduction and final total payable amount.

    Args:
        subtotal: Sum of item prices before discount.
        discount_percent: Discount rate (e.g. 0.10 for 10%).

    Returns:
        Dictionary with discount_amount and total payable.
    """
    discount_rate = max(0.0, min(1.0, discount_percent))
    discount_amount = round(subtotal * discount_rate, 2)
    total = round(subtotal - discount_amount, 2)
    return {"discount_amount": discount_amount, "total": total}
