"""Business rules and approval threshold evaluation tools (Slice 3)."""

import logging
from typing import Any

logger = logging.getLogger("aveline.agent.tools.commerce.rules")

DEFAULT_HIGH_VALUE_THRESHOLD = 40000.0  # LKR 40,000
DEFAULT_MIN_MARGIN = 0.25                # 25% minimum required margin


async def validate_business_rules(
    org_id: str,
    order_total: float,
    margin: float,
    requested_discount: float = 0.0,
    customer_tier: str | None = None,
    registry: Any = None,
) -> dict[str, Any]:
    """Evaluate deal parameters against active organization business rules.

    Checks:
      1. High-Value Order threshold (LKR 40,000 default).
      2. Minimum Margin threshold (25% default).
      3. Customer tier discount cap (VIP 10%, Regular 5%).

    Returns:
        Dictionary with requires_approval, is_auto_approved, triggered_rules, and flags.
    """
    if registry and hasattr(registry, "_client"):
        try:
            result = await registry._client.request(
                "POST",
                f"/orgs/{org_id}/business-rules/evaluate",
                json={
                    "orderTotal": order_total,
                    "margin": margin,
                    "requestedDiscount": requested_discount,
                    "customerTier": customer_tier,
                },
            )
            if result and isinstance(result, dict):
                return {
                    "requires_approval": bool(result.get("requiresApproval")),
                    "is_auto_approved": bool(result.get("isAutoApproved")),
                    "triggered_rules": list(result.get("triggeredRules") or []),
                    "flags": list(result.get("flags") or []),
                    "max_allowed_discount": float(result.get("maxAllowedDiscount") or 0.10),
                    "min_required_margin": float(result.get("minRequiredMargin") or 0.25),
                    "high_value_threshold": float(result.get("highValueThreshold") or 40000.0),
                }
        except Exception as ex:
            logger.debug("Falling back to local rules evaluation engine: %s", ex)

    # Local fallback rule evaluator matching .NET BusinessRulesService defaults
    triggered_rules: list[str] = []
    flags: list[str] = []

    # 1. High value check
    if order_total > DEFAULT_HIGH_VALUE_THRESHOLD:
        triggered_rules.append("HIGH_VALUE_THRESHOLD_EXCEEDED")
        flags.append(f"Order total LKR {order_total:,.2f} exceeds high-value threshold of LKR {DEFAULT_HIGH_VALUE_THRESHOLD:,.2f}")

    # 2. Minimum margin check
    if margin < DEFAULT_MIN_MARGIN:
        triggered_rules.append("LOW_MARGIN_THRESHOLD")
        flags.append(f"Profit margin {margin:.1%} is below minimum requirement of {DEFAULT_MIN_MARGIN:.1%}")

    # 3. Discount cap check
    max_discount = 0.10 if (customer_tier or "").upper() == "VIP" else 0.05
    if requested_discount > max_discount:
        triggered_rules.append("DISCOUNT_LIMIT_EXCEEDED")
        flags.append(f"Requested discount {requested_discount:.1%} exceeds {customer_tier or 'Regular'} tier limit of {max_discount:.1%}")

    requires_approval = len(triggered_rules) > 0
    return {
        "requires_approval": requires_approval,
        "is_auto_approved": not requires_approval,
        "triggered_rules": triggered_rules,
        "flags": flags,
        "max_allowed_discount": max_discount,
        "min_required_margin": DEFAULT_MIN_MARGIN,
        "high_value_threshold": DEFAULT_HIGH_VALUE_THRESHOLD,
    }


def check_approval_threshold(
    order_total: float,
    margin: float,
    requested_discount: float = 0.0,
    customer_tier: str | None = None,
) -> bool:
    """Quick sync check whether an order breaches safety thresholds."""
    if order_total > DEFAULT_HIGH_VALUE_THRESHOLD:
        return True
    if margin < DEFAULT_MIN_MARGIN:
        return True
    max_discount = 0.10 if (customer_tier or "").upper() == "VIP" else 0.05
    if requested_discount > max_discount:
        return True
    return False
