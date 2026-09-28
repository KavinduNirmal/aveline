"""Shared, agent-agnostic customer resolution (Issue #161).

The concierge orchestrator runs this once per inbound message to turn free text into a
resolved customer id (or a pending clarification) that any specialist - Ava now, Elle and
Lina later - can consume from shared state.
"""

from app.customer_resolution.extract import extract_customer_name, extract_phone
from app.customer_resolution.models import CustomerCandidate, CustomerResolution
from app.customer_resolution.resolver import resolve_customer

__all__ = [
    "CustomerCandidate",
    "CustomerResolution",
    "extract_customer_name",
    "extract_phone",
    "resolve_customer",
]
