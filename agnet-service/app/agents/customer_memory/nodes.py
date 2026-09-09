"""Nodes for the Customer Memory Agent sub-graph (Slice 1).

The agent is a dependency-injected async node set over the shared ``ToolRegistry`` (which calls
the ASP.NET Core internal endpoints). All business decisions are deterministic (rule parsing +
template drafting) so the graph is testable without an LLM or a live backend.
"""

import json
import logging
from typing import Any

from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import HumanMessage, SystemMessage

from app.agents.customer_memory.parsing import parse_message
from app.agents.customer_memory.state import MemoryAgentState
from app.prompts.assembly import assemble_system_prompt
from app.schemas.customer_memory import MemoryAgentOutput
from app.tools.registry import ToolRegistry

logger = logging.getLogger("aveline.agent.customer_memory")

SKIPPED = "skipped"
SUCCESS = "success"
OUT_OF_SCOPE = "out_of_scope"


def coerce_output(output: dict[str, Any]) -> MemoryAgentOutput | None:
    """Validate an assembled ``MemoryAgentOutput``-shaped dict in the running path.

    The Pydantic models forbid extra fields, so any contract drift between the graph's plain
    dict and the typed output schema is caught here rather than silently escaping to the caller.
    Returns ``None`` when the dict does not conform.
    """
    try:
        return MemoryAgentOutput.model_validate(output)
    except Exception:  # noqa: BLE001 - a malformed output must degrade gracefully
        logger.error("Memory agent output failed schema validation; emitting error.", exc_info=True)
        return None


class CustomerMemoryAgent:
    """LangGraph node set for the Customer Memory Agent, bound to a ``ToolRegistry``."""

    def __init__(
        self,
        registry: ToolRegistry,
        llm: BaseChatModel | None = None,
        org_context: dict[str, Any] | None = None,
    ) -> None:
        self.registry = registry
        self.llm = llm
        self.org_context = org_context

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
        """Semantically search the customer's memory for context on this message.

        Semantic search depends on the backend embedding provider; when it is unavailable the agent
        degrades gracefully (empty context) rather than failing the whole run, so resolution and
        brief/draft composition still happen.
        """
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")
        try:
            results = await self.registry.get_customer_memories(
                str(org_id), str(customer_id), state.get("message", ""), top_k=5
            )
            memories = results if isinstance(results, list) else results.get("results", [])
            return {"semantic_context": memories}
        except Exception:  # noqa: BLE001 - retrieval must not fail personalization
            logger.warning(
                "Semantic memory retrieval failed; continuing without context (customer %s).",
                customer_id,
                exc_info=True,
            )
            return {"semantic_context": []}

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
            # Persist a structured Customer_Event row when a concrete date is known, so event
            # queries and reminders answer from real data (not just free-text memories).
            if event.get("event_date"):
                await self.registry.add_customer_event(
                    org_id,
                    customer_id,
                    event["event_type"],
                    event["event_date"],
                    description=event.get("description"),
                )

        # Log the inbound interaction with the structured intent the agent extracted (ADR-016).
        parsed_intent = state.get("parsed_intent") or {}
        await self.registry.record_customer_interaction(
            org_id,
            customer_id,
            channel=state.get("channel", "whatsapp"),
            direction=state.get("direction", "inbound"),
            message_content=state.get("message", ""),
            parsed_intent_json=json.dumps(parsed_intent) if parsed_intent else None,
        )

        return {"extracted_memories": extracted}

    async def compose_output(self, state: MemoryAgentState) -> dict[str, Any]:
        """Assemble the final output: brief + a staff-review draft response."""
        profile = state.get("profile") or {}
        intent = state.get("parsed_intent") or {}
        name = self._customer_name(state)
        tags = profile.get("tags") or []

        brief = await self._interaction_brief(state, name, profile, tags)

        draft, usage = await self._generate_draft(state, profile, intent, name)
        action = "send_whatsapp" if intent.get("intent_type") == "item_search" else None

        # Only dateable events are emitted as structured DetectedEvents (the schema requires a
        # date); undated signals remain as free-text memories so the output stays schema-valid.
        structured_events = [e for e in (state.get("detected_events") or []) if e.get("event_date")]

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
            "detected_events": structured_events,
            "interaction_brief": brief,
            "draft_response": draft,
            "action_required": action,
        }

        # Validate against the typed schema in the running path (Issue #167). On failure emit a
        # safe error fallback so a malformed output never escapes the graph un-validated.
        if coerce_output(output) is None:
            fallback = {
                "status": "error",
                "reason": "memory output failed schema validation",
                "extracted_memories": [],
                "detected_events": [],
            }
            return {"output": fallback, "status": "error", "usage": usage}

        return {"output": output, "status": SUCCESS, "usage": usage}

    # ------------------------------------------------------------------ helpers

    async def _interaction_brief(
        self,
        state: MemoryAgentState,
        name: str,
        profile: dict[str, Any],
        tags: list[str],
    ) -> str:
        """Assemble a staff-facing brief, enriched from the backend's real events/tags.

        Uses the backend ``/brief`` result (CustomerMemoryService.GenerateBriefAsync) so event
        queries answer from actual ``Customer_Event`` rows. Falls back to the local semantic
        context when the backend is unavailable.
        """
        semantic_context = state.get("semantic_context") or []
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")

        backend: dict[str, Any] = {}
        if org_id and customer_id:
            try:
                backend = (await self.registry.generate_interaction_brief(str(org_id), str(customer_id))) or {}
            except Exception:  # noqa: BLE001 - brief failure must not break compose
                logger.warning("Interaction brief backend call failed; using local brief.", exc_info=True)

        status = backend.get("status") or profile.get("status") or "new"
        display_name = backend.get("customerName") or name

        parts = [f"{display_name} ({status})"]
        if semantic_context:
            parts.append(f"Context: {semantic_context[0].get('content')}")
        if backend.get("upcomingEvents"):
            parts.append(f"Upcoming: {backend['upcomingEvents']}")
        backend_tags = backend.get("tags")
        merged_tags = backend_tags if isinstance(backend_tags, list) and backend_tags else tags
        if merged_tags:
            parts.append(f"Tags: {', '.join(merged_tags)}")
        return " | ".join(parts)

    async def _generate_draft(
        self,
        state: MemoryAgentState,
        profile: dict[str, Any],
        intent: dict[str, Any],
        name: str,
    ) -> tuple[str, dict[str, Any] | None]:
        """Return ``(draft_response, usage)`` using the injected LLM when available.

        The deterministic ``_draft`` template is the fallback whenever no LLM is configured or
        the provider call fails, so a live LLM is never a hard dependency of the graph. When an
        LLM successfully produces a draft, the langchain ``usage_metadata`` token counts are
        returned for usage reporting (ADR-010); ``usage`` is ``None`` otherwise.
        """
        if self.llm is None:
            return self._draft(name, intent), None

        context_lines = [f"Customer message: {state.get('message', '')}"]
        semantic = state.get("semantic_context") or []
        if semantic:
            top = semantic[0]
            context_lines.append(f"Relevant memory: {top.get('content')}")
        if intent:
            context_lines.append(f"Detected intent: {json.dumps(intent)}")
        context_lines.append(
            "Write a short, warm, staff-facing DRAFT customer reply (2-4 sentences). "
            "Do not auto-send; it is reviewed by a human associate."
        )

        system = assemble_system_prompt("memory", self.org_context)
        try:
            result = await self.llm.ainvoke(
                [SystemMessage(content=system), HumanMessage(content="\n".join(context_lines))]
            )
        except Exception:  # noqa: BLE001 - provider failure must not break the graph
            logger.warning("LLM draft generation failed; falling back to template.", exc_info=True)
            return self._draft(name, intent), None

        draft = (getattr(result, "content", None) or "").strip()
        if not draft:
            return self._draft(name, intent), None

        meta = getattr(result, "usage_metadata", None) or {}
        usage: dict[str, Any] | None = {
            "input_tokens": int(meta.get("input_tokens") or 0),
            "output_tokens": int(meta.get("output_tokens") or 0),
        }
        return draft, usage

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
