"""Tests for the shared customer-resolution capability (Issue #161).

The orchestrator resolves a customer from the raw message (name or phone) once, so any
specialist can consume the result. These tests cover the deterministic extraction and the
resolution decision (resolved / ambiguous / not_found / no_signal) against a fake registry.
"""

from app.customer_resolution import (
    CustomerResolution,
    extract_customer_name,
    extract_phone,
    resolve_customer,
)

MATCH_SARAH = {
    "customerId": "cust-sarah",
    "fullName": "Samantha Arias",
    "phoneNumber": "+94771234567",
    "status": "vip",
    "lastVisitAt": "2026-08-20T10:00:00Z",
}


class FakeLookupRegistry:
    """Records lookup calls and returns a canned backend response."""

    def __init__(self, response: dict) -> None:
        self.response = response
        self.calls: list[tuple[str, str | None, str | None]] = []
        self.identify_calls: list[tuple[str, str, str | None]] = []

    async def lookup_customers(self, org_id, name=None, phone=None):
        self.calls.append((org_id, name, phone))
        return self.response

    async def identify_customer(self, org_id, phone_number, full_name=None):
        self.identify_calls.append((org_id, phone_number, full_name))
        return {
            "customerId": f"cust-{phone_number}",
            "fullName": full_name or "New Customer",
            "phoneNumber": phone_number,
            "status": "new",
        }


def _exact(match: dict) -> dict:
    return {"matches": [match], "isExact": True, "total": 1}


def _many(matches: list[dict]) -> dict:
    return {"matches": matches, "isExact": False, "total": len(matches)}


# --------------------------------------------------------------------------- extraction


def test_extract_phone_finds_local_number():
    assert extract_phone("Can you look up 0771234567 for me?") == "0771234567"


def test_extract_phone_finds_e164_number():
    assert extract_phone("Any events for +94771234567?") == "+94771234567"


def test_extract_phone_returns_none_when_absent():
    assert extract_phone("Any events for Samantha Arias?") is None


def test_extract_name_full_name():
    assert extract_customer_name("Any events for Samantha Arias?") == "Samantha Arias"


def test_extract_name_single_name():
    assert extract_customer_name("Any events for Samantha?") == "Samantha"


def test_extract_name_after_lookup_context():
    assert extract_customer_name("What do we know about Michael Perera?") == "Michael Perera"


def test_extract_name_none_when_no_proper_noun():
    assert extract_customer_name("Hi, how are you?") is None


def test_extract_name_none_when_no_customer_mentioned():
    assert extract_customer_name("Do you have wedding sarees?") is None


def test_extract_name_ignores_intent_cue():
    assert extract_customer_name("Remember Anjali prefers silk?") == "Anjali"


# --------------------------------------------------------------------------- resolution


async def test_resolve_explicit_customer_id_skips_lookup():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "any events?", registry=registry, customer_id="cust-sarah")

    assert res.kind == "resolved"
    assert res.customer_id == "cust-sarah"
    assert registry.calls == []


async def test_resolve_by_phone_in_message():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "look up 0771234567", registry=registry)

    assert res.kind == "resolved"
    assert res.customer_id == "cust-sarah"
    assert registry.calls == [("org-1", None, "0771234567")]


async def test_resolve_by_name_single_match_is_resolved():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "Any events for Samantha Arias?", registry=registry)

    assert res.kind == "resolved"
    assert res.customer_id == "cust-sarah"
    assert registry.calls == [("org-1", "Samantha Arias", None)]


async def test_resolve_multiple_matches_is_ambiguous():
    second = {**MATCH_SARAH, "customerId": "cust-sarah2", "fullName": "Samantha R"}
    registry = FakeLookupRegistry(_many([MATCH_SARAH, second]))
    res = await resolve_customer("org-1", "Any events for Samantha Arias?", registry=registry)

    assert res.kind == "ambiguous"
    assert res.needs_clarification is True
    assert [c.customer_id for c in res.candidates] == ["cust-sarah", "cust-sarah2"]


async def test_resolve_no_match_is_not_found():
    registry = FakeLookupRegistry({"matches": [], "isExact": False, "total": 0})
    res = await resolve_customer("org-1", "Any events for Zara Nobody?", registry=registry)

    assert res.kind == "not_found"
    assert res.needs_clarification is True
    assert res.candidates == []


async def test_resolve_no_signal_when_no_customer_mentioned():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "Hi how are you", registry=registry)

    assert res.kind == "no_signal"
    assert res.needs_clarification is False
    assert registry.calls == []


async def test_resolve_lowercase_at_mention_resolves_by_name():
    """A lower-case @mention resolves even though free-text extraction requires title-case (ADR-019)."""
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "any events for @samantha arias?", registry=registry)

    assert res.kind == "resolved"
    assert res.customer_id == "cust-sarah"
    assert registry.calls == [("org-1", "samantha arias", None)]


async def test_resolve_at_mention_stops_at_prose():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "@samantha arias dropped by next week", registry=registry)

    assert res.kind == "resolved"
    assert registry.calls == [("org-1", "samantha arias", None)]


async def test_resolve_hash_phone_mention_looks_up_by_phone():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "reach #0771234567 please", registry=registry)

    assert res.kind == "resolved"
    assert registry.calls == [("org-1", None, "0771234567")]
    assert registry.identify_calls == []


async def test_resolve_new_customer_with_name_and_phone_onboards():
    """A staff note naming a brand-new customer with a #phone should onboard them, not ask again."""
    registry = FakeLookupRegistry({"matches": [], "isExact": False, "total": 0})
    res = await resolve_customer(
        "org-1", "A new customer dropped by, @Jason smith, #0751234567. he will come by tomorrow",
        registry=registry,
    )

    assert res.kind == "resolved"
    # Phone (strongest identity) was looked up first, then onboarded via identify with the name.
    assert registry.calls == [("org-1", None, "0751234567")]
    assert registry.identify_calls == [("org-1", "0751234567", "Jason smith")]
    assert res.customer_id == "cust-0751234567"


async def test_resolve_phone_mention_existing_customer_does_not_onboard():
    registry = FakeLookupRegistry(_exact(MATCH_SARAH))
    res = await resolve_customer("org-1", "reach #0771234567 for @Samantha Arias", registry=registry)

    assert res.kind == "resolved"
    assert res.customer_id == "cust-sarah"
    assert registry.identify_calls == []


def test_resolution_shapes():
    assert CustomerResolution(kind="resolved", customer_id="c").is_resolved
    assert not CustomerResolution(kind="no_signal").is_resolved
