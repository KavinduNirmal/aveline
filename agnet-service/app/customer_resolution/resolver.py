"""Resolution logic for the shared customer-lookup capability (Issue #161).

``resolve_customer`` turns either explicit context (an id/phone from ``org_context``) or a
raw message (name/phone typed by staff) into a single, shared ``CustomerResolution`` that
the concierge orchestrator injects into state so every specialist can consume it.

The actual lookup is delegated to a thin registry (``ToolRegistry.lookup_customers``), which
calls the backend ``/internal/customers/lookup`` endpoint. No business logic lives here.
"""

import logging
from typing import Any

from app.customer_resolution.extract import extract_customer_name, extract_phone
from app.customer_resolution.mentions import extract_mentions
from app.customer_resolution.models import CustomerCandidate, CustomerResolution

logger = logging.getLogger("aveline.agent.customer_resolution")


async def resolve_customer(
    org_id: str,
    message: str,
    *,
    registry: Any,
    customer_id: str | None = None,
    phone: str | None = None,
) -> CustomerResolution:
    """Resolve a customer from explicit context or ``message``.

    Precedence (ADR-019):
      1. An explicit ``customer_id`` (fast path, no backend call).
      2. An explicit mention in ``message``: ``@<name>`` (customer) or ``#<phone>``.
      3. A phone number, from ``phone`` or extracted from ``message``.
      4. A name extracted from ``message``.
      5. ``no_signal`` when the message does not mention a customer.
    """
    if customer_id:
        return CustomerResolution(kind="resolved", customer_id=str(customer_id), message=message)

    mentions = extract_mentions(message)

    # An explicit #phone is the strongest identity signal. When the read-only lookup finds no
    # such customer, the note is declaring a brand-new customer (often with an @name) -> onboard
    # them via identify (create) rather than asking for a number they already gave.
    if mentions.phone:
        lookup = _from_lookup(await registry.lookup_customers(org_id, phone=mentions.phone), message)
        if lookup.kind != "not_found":
            return lookup
        return await _identify_resolution(
            registry, org_id, message, phone=mentions.phone, name=mentions.customer)

    if mentions.customer:
        return _from_lookup(await registry.lookup_customers(org_id, name=mentions.customer), message)

    search_phone = phone or extract_phone(message)
    if search_phone:
        return _from_lookup(await registry.lookup_customers(org_id, phone=search_phone), message)

    name = extract_customer_name(message)
    if name:
        return _from_lookup(await registry.lookup_customers(org_id, name=name), message)

    return CustomerResolution(kind="no_signal", message=message)


async def _identify_resolution(
    registry: Any,
    org_id: str,
    message: str,
    *,
    phone: str,
    name: str | None,
) -> CustomerResolution:
    """Create-or-fetch a customer by phone (with an optional display name) and resolve to it."""
    profile = await registry.identify_customer(org_id, phone, name)
    customer_id = str(profile.get("customerId") or profile.get("id") or "")
    return CustomerResolution(kind="resolved", customer_id=customer_id, profile=profile, message=message)


def _from_lookup(payload: dict[str, Any] | None, message: str) -> CustomerResolution:
    """Map a backend lookup response onto a ``CustomerResolution``."""
    matches = (payload or {}).get("matches") or []

    if len(matches) == 1:
        match = matches[0]
        return CustomerResolution(
            kind="resolved",
            customer_id=str(match["customerId"]),
            profile=match,
            message=message,
        )

    if len(matches) > 1:
        return CustomerResolution(
            kind="ambiguous",
            candidates=[_candidate(m) for m in matches],
            message=message,
        )

    return CustomerResolution(kind="not_found", message=message)


def _candidate(match: dict[str, Any]) -> CustomerCandidate:
    """Map a backend match (camelCase) onto a ``CustomerCandidate``."""
    last_visit = match.get("lastVisitAt")
    return CustomerCandidate(
        customer_id=str(match["customerId"]),
        full_name=match.get("fullName"),
        phone_number=match.get("phoneNumber"),
        status=match.get("status") or "new",
        last_visit_at=str(last_visit) if last_visit is not None else None,
    )
