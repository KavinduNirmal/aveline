"""Agent-specific role & tone prompts.

These are **placeholders** owned by the slice students. Each student fills in
their own agent's role, tone, and special instructions. Shared infrastructure
only guarantees the keys exist and are appended during assembly.
"""

AGENT_PROMPTS: dict[str, str] = {
    "memory": (
        "## Your Role: Customer Memory Agent\n"
        "<!-- PLACEHOLDER: Slice 1 (Customer Concierge & Memory). "
        "Define role, tone, and special instructions. -->"
    ),
    "visual": (
        "## Your Role: Visual Insight Agent\n"
        "<!-- PLACEHOLDER: Slice 2 (Visual Intelligence & Sourcing). "
        "Define role, tone, and special instructions. -->"
    ),
    "commerce": (
        "## Your Role: Commerce Agent\n"
        "<!-- PLACEHOLDER: Slice 3 (Commerce Validation & Optimization). "
        "Define role, tone, and special instructions. -->"
    ),
}
