"""Build the dynamic per-organization context prompt layer.

Injected per-request based on the boutique's plan tier, brand voice, and active
business rules. This is the third layer appended after the universal and
agent-specific prompts.
"""

from typing import Any


def build_customer_prompt(org_context: dict[str, Any] | None = None) -> str:
    """Render the dynamic boutique context as a prompt fragment.

    Args:
        org_context: The organization's business context. Recognized keys:
            ``plan_tier``, ``brand_voice``, ``business_rules`` (a mapping of
            rule name to value). Unknown keys are ignored.

    Returns:
        A markdown fragment describing the boutique context and business rules.
    """
    org_context = org_context or {}
    plan_tier = org_context.get("plan_tier", "seed")
    brand_voice = org_context.get("brand_voice", "Elegant and formal")
    business_rules = org_context.get("business_rules", {}) or {}

    lines = [
        "## Boutique Context (Dynamic)",
        f"- Plan Tier: {plan_tier}",
        f"- Brand Voice: {brand_voice}",
        "",
        "## Business Rules",
    ]
    if business_rules:
        for rule_name, rule_value in business_rules.items():
            lines.append(f"- {rule_name}: {rule_value}")
    else:
        lines.append("- (none configured)")

    return "\n".join(lines)
