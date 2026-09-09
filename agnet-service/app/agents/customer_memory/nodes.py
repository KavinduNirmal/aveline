"""Nodes for the Customer Memory Agent sub-graph (Slice 1).

The agent is a dependency-injected async node set over the shared ``ToolRegistry`` (which calls
the ASP.NET Core internal endpoints). All business decisions are deterministic (rule parsing +
template drafting) so the graph is testable without an LLM or a live backend.
"""

import logging
from typing import Any

from app.agents.customer_memory.parsing import parse_message
from app.agents.customer_memory.state import MemoryAgentState
from app.tools.registry import ToolRegistry

logger = logging.getLogger("aveline.agent.customer_memory")

SKIPPED = "skipped"
SUCCESS = "success"
OUT_OF_SCOPE = "out_of_scope"


class CustomerMemoryAgent:
    """LangGraph node set for the Customer Memory Agent, bound to a ``ToolRegistry``."""

    def __init__(self, registry: ToolRegistry) -> None:
        self.registry = registry

    async def resolve_customer(self, state: MemoryAgentState) -> dict[str, Any]:
        """Resolve the customer id from explicit id or phone; short-circuit if unavailable."""
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")
        phone = state.get("phone_number")

        if not org_id:
            return self._skip("organization context is missing")
        if not customer_id and not phone:
            return self._skip("no customer context (customer_id/phone) available")

        if not customer_id and phone:
            profile = await self.registry.identify_customer(org_id, phone, state.get("customer_name"))
            customer_id = str(profile.get("customerId") or profile.get("id") or "")
            return {"customer_id": customer_id, "profile": profile}
        return {}

    async def check_consent(self, state: MemoryAgentState) -> dict[str, Any]:
        """Refuse to process customers who have revoked consent."""
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")
        consent = await self.registry.get_customer_consent(str(org_id), str(customer_id))
        status = (consent or {}).get("consentStatus") or "pending"
        if status == "revoked":
            return self._skip("customer has revoked consent")
        return {"consent_status": status}

    async def parse(self, state: MemoryAgentState) -> dict[str, Any]:
        """Extract structured intent and preference/event signals from the message."""
        parsed = parse_message(state.get("message", ""), intent_hint=state.get("intent_type"))
        return {
            "parsed_intent": parsed["parsed_intent"],
            "detected_events": [
                {
                    "event_type": parsed["event_type"],
                    "event_date": parsed["iso_date"],
                }
            ]
            if parsed["event_type"]
            else [],
            "_preference_signals": parsed["preference_signals"],
        }

    async def retrieve(self, state: MemoryAgentState) -> dict[str, Any]:
        """Semantically search the customer's memory for context on this message."""
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")
        results = await self.registry.get_customer_memories(
            str(org_id), str(customer_id), state.get("message", ""), top_k=5
        )
        memories = results if isinstance(results, list) else results.get("results", [])
        return {"semantic_context": memories}

    async def persist(self, state: MemoryAgentState) -> dict[str, Any]:
        """Save explicit preferences and detected events as memories (consent-granted only)."""
        if state.get("consent_status") == "revoked":
            return {}

        org_id = str(state.get("org_id"))
        customer_id = str(state.get("customer_id"))
        signals = state.get("_preference_signals", [])
        name = self._customer_name(state)

        extracted: list[dict[str, Any]] = []
        for signal in signals:
            if signal.get("negative"):
                content = f"{name} dislikes {signal['preference_value']}"
            else:
                content = f"{name} prefers {signal['preference_value']}"
            await self.registry.save_customer_memory(org_id, customer_id, content, "preference")
            extracted.append(
                {
                    "content": content,
                    "category": "preference",
                    "is_explicit": True,
                    "confidence": 0.9,
                }
            )

        for event in state.get("detected_events", []):
            on_date = f" on {event['event_date']}" if event.get("event_date") else ""
            content = f"{name} has a {event['event_type']}{on_date}"
            await self.registry.save_customer_memory(org_id, customer_id, content, "event")
            extracted.append(
                {"content": content, "category": "event", "is_explicit": True, "confidence": 0.9}
            )

        return {"extracted_memories": extracted}

    async def compose_output(self, state: MemoryAgentState) -> dict[str, Any]:
        """Assemble the final output: brief + a staff-review draft response."""
        profile = state.get("profile") or {}
        intent = state.get("parsed_intent") or {}
        name = self._customer_name(state)
        tags = profile.get("tags") or []

        brief = self._build_brief(name, profile, state.get("semantic_context", []), tags)

        draft = self._draft(name, intent)
        action = "send_whatsapp" if intent.get("intent_type") == "item_search" else None

        output = {
            "status": SUCCESS,
            "parsed_intent": intent,
            "customer": {
                "customer_id": state.get("customer_id"),
                "phone_number": profile.get("phoneNumber") or state.get("phone_number"),
                "full_name": profile.get("fullName") or name,
                "status": profile.get("status") or "new",
                "consent_status": state.get("consent_status") or "pending",
            },
            "extracted_memories": state.get("extracted_memories", []),
            "detected_events": state.get("detected_events", []),
            "interaction_brief": brief,
            "draft_response": draft,
            "action_required": action,
        }
        return {"output": output, "status": SUCCESS}

    # ------------------------------------------------------------------ helpers

    @staticmethod
    def _skip(reason: str) -> dict[str, Any]:
        return {
            "status": SKIPPED,
            "reason": reason,
            "output": {"status": SKIPPED, "reason": reason, "extracted_memories": [], "detected_events": []},
        }

    @staticmethod
    def _customer_name(state: MemoryAgentState) -> str:
        profile = state.get("profile") or {}
        return profile.get("fullName") or state.get("customer_name") or "The customer"

    @staticmethod
    def _build_brief(name: str, profile: dict[str, Any], context: list[dict[str, Any]], tags: list[str]) -> str:
        parts = [f"{name} ({profile.get('status', 'new')})"]
        if context:
            top = context[0]
            parts.append(f"Context: {top.get('content')}")
        if tags:
            parts.append(f"Tags: {', '.join(tags)}")
        return " | ".join(parts)

    @staticmethod
    def _draft(name: str, intent: dict[str, Any]) -> str:
        occasion = intent.get("occasion")
        color = intent.get("color")
        intent_type = intent.get("intent_type")
        if intent_type == "item_search" and occasion:
            topic = f"a {color + ' ' if color else ''}{occasion}"
            return f"Hi {name}! We'd love to help you find {topic}. Let me check our pieces for you."
        if intent_type == "item_search" and color:
            return f"Hi {name}! I'll find some beautiful {color} options in your size for you."
        if intent_type == "event_query" and occasion:
            return f"Hi {name}! That sounds lovely — tell me more about your {occasion} so I can prepare."
        return f"Hi {name}! Thank you for reaching out to Aveline — how can I assist you today?"
