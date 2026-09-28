"""The human-approval decision vocabulary, published once (ADR-024, Decision 3).

The API's HTTP verbs (``approve``/``reject``/``revise``) and the graph's expectations
(``approved``/``rejected``) used to be two different sets, and nothing compared them. The mismatch
was invisible until an approval actually reached the graph, where it fell through every branch.

This module is the graph's side of the contract and the source of truth for the resume payload.
The API maps its verbs onto these values before anything leaves the process
(``ApprovalDecisions.ToAgentDecision``), and both sides pin the literals in a test so drift across
the language boundary fails loudly instead of silently skipping commerce on resume.
"""

from enum import StrEnum

#: The action names carried in a resume payload's ``decision`` field.
AGENT_APPROVAL_DECISIONS: frozenset[str] = frozenset({"approved", "rejected", "revised"})


class AgentApprovalDecision(StrEnum):
    """What a human decided about a paused deal.

    Values are the wire contract: they are what the API sends and what the commerce sub-graph
    routes on. They are deliberately the *past-tense* forms the graph already matched, so the
    graph is the source of truth rather than the side that has to change.
    """

    approved = "approved"
    rejected = "rejected"
    revised = "revised"


def normalize_decision(value: object) -> str | None:
    """Return a canonical decision string, or ``None`` when ``value`` is not one.

    Accepts the graph vocabulary only. An unknown value is *not* coerced to a default: a pause
    must never be settled by a decision nobody made (ADR-024, A4).
    """
    if value is None:
        return None
    text = str(value).strip().lower()
    return text if text in AGENT_APPROVAL_DECISIONS else None
