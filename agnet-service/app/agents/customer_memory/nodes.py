"""Nodes for the Customer Memory Agent sub-graph (Slice 1).

The agent is a dependency-injected async node set over the shared ``ToolRegistry`` (which calls
the ASP.NET Core internal endpoints). All business decisions are deterministic (rule parsing +
template drafting) so the graph is testable without an LLM or a live backend.
"""

import json
import logging
from typing import Any

import httpx
from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import HumanMessage, SystemMessage

from app.agents.customer_memory.introduction import extract_self_introduced_name
from app.agents.customer_memory.parsing import parse_message
from app.agents.customer_memory.state import MemoryAgentState
from app.agents.customer_memory.update_instruction import extract_customer_update
from app.context import render_context_block
from app.llm.replies import unwrap_reply
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


def _unwrap_reply(content: str) -> str:
    """Extract a plain-text reply from an LLM completion.

    Thin wrapper over the shared helper (``app/llm/replies.py``), which owns the envelope
    unwrapping because the visual agent needs exactly the same behaviour.
    """
    return unwrap_reply(content, fallback=content if isinstance(content, str) else "")


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
            # A first inbound message frequently *is* the introduction ("I'm Kasha vivian, this is
            # for a cocktail party"). Without reading the name out of it the customer stays
            # nameless: the profile is created on the phone number alone.
            name = state.get("customer_name") or extract_self_introduced_name(state.get("message"))
            profile = await self.registry.identify_customer(org_id, phone, name)
            customer_id = str(profile.get("customerId") or profile.get("id") or "")
            return {"customer_id": customer_id, "profile": profile}

        if customer_id:
            # The orchestrator may have resolved this customer from the phone before this node ran,
            # in which case the name was never offered for storage and the record stays unnamed.
            # Identify is idempotent and only backfills a *missing* name, so an existing name is
            # never overwritten by a later guess.
            profile = state.get("profile") or {}
            already_named = profile.get("fullName") or state.get("customer_name")
            introduced = extract_self_introduced_name(state.get("message"))
            if introduced and not already_named and phone:
                profile = await self.registry.identify_customer(org_id, phone, introduced)
                return {"customer_id": customer_id, "profile": profile}

        return {}

    async def apply_staff_update(self, state: MemoryAgentState) -> dict[str, Any]:
        """Apply an explicit staff instruction to the bound customer's details.

        Staff type "please update this customer with the name X and number Y" into the Salon. This
        is the only path that writes identity fields from chat, so it is deliberately narrow:

        - **Staff only.** A customer messaging in is never taken as instructing us to rewrite their
          own record. ``staff_query`` is what separates the two.
        - **Explicit only.** ``extract_customer_update`` requires an update verb; a message that
          merely mentions a name is not an instruction to store one.
        - **Bound customer only.** With no customer on the conversation there is nobody to update,
          and guessing from a name would risk editing the wrong record. It asks instead.
        - **Never creates.** Naming someone not on file is a different operation; silently creating
          a record from a chat message would be wrong.

        A failure is reported, not swallowed: an update that appears to succeed while doing nothing
        is worse than one that says why it could not.
        """
        if not state.get("staff_query"):
            return {}

        instruction = extract_customer_update(state.get("message"))
        if instruction is None or instruction.is_empty:
            return {}

        org_id = state.get("org_id")
        customer_id = state.get("customer_id")

        # The run short-circuits before `parse`, so the intent is carried from the upstream decision
        # instead of being left empty: `MemoryAgentOutput` requires one, and the upstream
        # classification is the honest value.
        intent_type = state.get("intent_type") or "general_inquiry"
        intent = {"intent_type": intent_type}

        if not customer_id:
            return {
                "parsed_intent": intent,
                "customer_update_error": (
                    "I could not tell which customer to update - no customer is linked to this "
                    "conversation yet. Open the customer's thread, or say which one you mean."
                ),
            }

        try:
            profile = await self.registry.update_customer(
                str(org_id),
                str(customer_id),
                full_name=instruction.full_name,
                phone_number=instruction.phone_number,
            )
        except httpx.HTTPStatusError as exc:
            return {
                "parsed_intent": intent,
                "customer_update_error": self._update_failure_message(exc, instruction),
            }
        except Exception as exc:  # noqa: BLE001 - reported to staff, never silently dropped
            logger.warning("Customer update failed: %s", exc)
            return {
                "parsed_intent": intent,
                "customer_update_error": "I could not reach the customer book to apply that.",
            }

        return {
            "parsed_intent": intent,
            "profile": profile,
            "applied_customer_update": {
                "full_name": instruction.full_name,
                "phone_number": instruction.phone_number,
                "customer_id": str(customer_id),
            },
        }

    @staticmethod
    def _update_failure_message(exc: httpx.HTTPStatusError, instruction: Any) -> str:
        """Turn a backend refusal into something a staff member can act on."""
        status = exc.response.status_code
        if status == 409:
            return (
                f"Another customer already has {instruction.phone_number}. "
                "Merge the two records or use a different number, then try again."
            )
        if status == 404:
            return "That customer is not in this boutique, so nothing was changed."
        if status == 400:
            return "The customer book rejected that change: the name or number looks invalid."
        return f"The customer book refused the change (HTTP {status}). Nothing was changed."

    @staticmethod
    def _update_confirmation(state: MemoryAgentState) -> str | None:
        """A staff-facing sentence describing what an update instruction did, or ``None``.

        Only reached when the update node short-circuited the run, so it is never produced for an
        ordinary message.
        """
        error = state.get("customer_update_error")
        if error:
            return str(error)

        applied = state.get("applied_customer_update")
        if not applied:
            return None

        changed: list[str] = []
        if applied.get("full_name"):
            changed.append(f"name to {applied['full_name']}")
        if applied.get("phone_number"):
            changed.append(f"number to {applied['phone_number']}")

        if not changed:
            return "Nothing was changed."

        return "Updated this customer's " + " and ".join(changed) + "."

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
        """Save explicit preferences and detected events as memories (consent-granted only).

        Backend writes are best-effort: a failure (e.g. the embedding provider is down) is logged
        and never fails the run, so inbound messages still produce a brief + draft.
        """
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
            if await self._try_save_memory(org_id, customer_id, content, "preference"):
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
            if await self._try_save_memory(org_id, customer_id, content, "event"):
                extracted.append(
                    {"content": content, "category": "event", "is_explicit": True, "confidence": 0.9}
                )
            # Persist a structured Customer_Event row when a concrete date is known, so event
            # queries and reminders answer from real data (not just free-text memories).
            if event.get("event_date"):
                try:
                    await self.registry.add_customer_event(
                        org_id,
                        customer_id,
                        event["event_type"],
                        event["event_date"],
                        description=event.get("description"),
                    )
                except Exception:  # noqa: BLE001 - best-effort event persistence
                    logger.warning("Failed to persist customer event (event_type=%s).", event["event_type"])

        # Log the inbound interaction with the structured intent the agent extracted (ADR-016).
        # Staff queries are not customer interactions, so only genuine inbound messages are logged.
        if not state.get("staff_query"):
            parsed_intent = state.get("parsed_intent") or {}
            try:
                await self.registry.record_customer_interaction(
                    org_id,
                    customer_id,
                    channel=state.get("channel", "whatsapp"),
                    direction=state.get("direction", "inbound"),
                    message_content=state.get("message", ""),
                    parsed_intent_json=json.dumps(parsed_intent) if parsed_intent else None,
                )
            except Exception:  # noqa: BLE001 - best-effort interaction logging
                logger.warning("Failed to record customer interaction (customer %s).", customer_id)

        return {"extracted_memories": extracted}

    async def _try_save_memory(self, org_id: str, customer_id: str, content: str, category: str) -> bool:
        """Attempt to persist a memory; returns True on success (best-effort)."""
        try:
            await self.registry.save_customer_memory(org_id, customer_id, content, category)
            return True
        except Exception:  # noqa: BLE001 - best-effort memory save
            logger.warning("Failed to save customer memory (category=%s).", category)
            return False

    async def compose_output(self, state: MemoryAgentState) -> dict[str, Any]:
        """Assemble the final output.

        Two audiences (ADR-019):
        - Inbound CUSTOMER message: a staff-facing ``interaction_brief`` plus a customer-facing
          draft (``draft_response``, surfaced as a Suggestion for staff to approve/send).
        - STAFF query (no inbound direction): Ava answers the staff directly as text and produces
          NO customer-facing draft / Suggestion.
        """
        profile = state.get("profile") or {}
        intent = state.get("parsed_intent") or {}
        name = self._customer_name(state)
        staff = bool(state.get("staff_query"))
        backend = await self._backend_brief(state)

        # A handled update answers the instruction directly. Running the normal brief/draft path
        # would add a customer-facing suggestion nobody asked for, and `parsed_intent` is empty here
        # because the run short-circuited before parsing.
        update_confirmation = self._update_confirmation(state)
        if update_confirmation is not None:
            interaction_brief = update_confirmation
            draft = None
            usage = None
            action = None
        elif staff:
            interaction_brief = self._staff_text(state, name, profile, backend)
            draft = None
            usage = None
            action = None
        else:
            interaction_brief = self._format_brief(state, name, profile, backend)
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
                # The backend brief is the one source that reliably knows the name: when the
                # conversation already carries a customer id, the resolver takes its fast path and
                # returns no profile, so `fullName` is absent and this block fell back to the
                # placeholder "The customer" - while the brief right beside it named them. Aveline's
                # summary line renders this field, so the two disagreed in the same message.
                "full_name": backend.get("customerName") or profile.get("fullName") or name,
                "status": profile.get("status") or "new",
                "consent_status": state.get("consent_status") or "pending",
            },
            "extracted_memories": state.get("extracted_memories", []),
            "detected_events": structured_events,
            "interaction_brief": interaction_brief,
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

    async def _backend_brief(self, state: MemoryAgentState) -> dict[str, Any]:
        """Fetch the backend interaction brief (real events/tags/preferences), or {} on failure."""
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")
        if not org_id or not customer_id:
            return {}
        try:
            return (await self.registry.generate_interaction_brief(str(org_id), str(customer_id))) or {}
        except Exception:  # noqa: BLE001 - brief failure must not break compose
            logger.warning("Interaction brief backend call failed; using local brief.", exc_info=True)
            return {}

    def _format_brief(
        self,
        state: MemoryAgentState,
        name: str,
        profile: dict[str, Any],
        backend: dict[str, Any],
    ) -> str:
        """Assemble the staff-facing digest (name/status + semantic context + events/tags)."""
        semantic_context = state.get("semantic_context") or []
        tags = profile.get("tags") or []
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

    def _staff_text(
        self,
        state: MemoryAgentState,
        name: str,
        profile: dict[str, Any],
        backend: dict[str, Any],
    ) -> str:
        """Answer a STAFF query directly (no customer-facing draft).

        Uses the real backend facts (status, preferences, upcoming events, tags) so the answer is
        grounded. Event-style queries answer about events; a brand-new customer is announced; other
        queries summarise what is known, or state when nothing is on file yet.
        """
        display_name = backend.get("customerName") or name
        status = backend.get("status") or profile.get("status") or "new"
        intent_type = (state.get("parsed_intent") or {}).get("intent_type")
        events = backend.get("upcomingEvents")
        prefs = backend.get("preferenceSummary")
        tags = backend.get("tags") if isinstance(backend.get("tags"), list) else []

        # What the boutique has actually recorded about this customer. These are semantic memories
        # (events, preferences, complaints), which the interaction brief does not carry - so a
        # question like "what do we have on file for her?" answered "nothing on file yet" while
        # three memories sat in the store.
        remembered = [
            str(memory.get("content")).strip()
            for memory in (state.get("semantic_context") or [])
            if isinstance(memory, dict) and memory.get("content")
        ]

        def _details() -> list[str]:
            """The grounded facts, in a stable order."""
            facts: list[str] = []
            if prefs:
                facts.append(f"preferences: {prefs}")
            if events:
                facts.append(f"upcoming events: {events}")
            if remembered:
                facts.append(f"on file: {'; '.join(remembered)}")
            if tags:
                facts.append(f"tags: {', '.join(tags)}")
            return facts

        if intent_type == "event_query":
            if events:
                return f"Upcoming events for {display_name}: {events}."
            # The absence of a dated event stays the headline answer; anything else on file is
            # added because it is usually what the staff member actually wanted to know.
            if remembered:
                return (
                    f"No upcoming events on file for {display_name}. "
                    f"On file: {'; '.join(remembered)}."
                )
            return f"No upcoming events on file for {display_name}."

        # General staff query: give the staff a readable, grounded summary.
        if status == "new":
            facts = _details()
            if not facts:
                return f"{display_name} is a new customer - nothing on file yet."
            # Announcing "new" stays useful for onboarding even when some memory exists, so the
            # framing is kept and the facts are appended rather than replacing it.
            return f"{display_name} is a new customer - {'; '.join(facts)}."

        facts = _details()
        if not facts:
            return f"{display_name} ({status}) has no preferences or events on file yet."
        return f"{display_name} ({status}) - {'; '.join(facts)}."

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
            "Reply with ONLY the plain text of that draft - no JSON, no code fences, no labels. "
            "Do not auto-send; it is reviewed by a human associate."
        )

        # The bounded conversation window (ADR-023), rendered here rather than added to
        # `context_lines` so it reaches the model as a system-layer fragment: conversation context
        # is data the model is given, not part of the current turn's instruction.
        dialogue_context = render_context_block(
            history=state.get("history"),
            thread_summary=state.get("thread_summary"),
            pinned_slots=state.get("pinned_slots"),
        )
        system = assemble_system_prompt(
            "memory", self.org_context, dialogue_context=dialogue_context
        )
        try:
            result = await self.llm.ainvoke(
                [SystemMessage(content=system), HumanMessage(content="\n".join(context_lines))]
            )
        except Exception:  # noqa: BLE001 - provider failure must not break the graph
            logger.warning("LLM draft generation failed; falling back to template.", exc_info=True)
            return self._draft(name, intent), None

        draft = _unwrap_reply(getattr(result, "content", None) or "")
        if not draft:
            return self._draft(name, intent), None

        meta = getattr(result, "usage_metadata", None) or {}
        token_details = meta.get("input_token_details") or {}
        cached_tokens = int(token_details.get("cache_read") or 0)
        usage: dict[str, Any] | None = {
            "input_tokens": int(meta.get("input_tokens") or 0),
            "output_tokens": int(meta.get("output_tokens") or 0),
        }
        if cached_tokens > 0:
            # Cached prompt tokens are span attributes only today; the cached direction is the
            # one additive token metric (R-15). Surfaced on state so run metrics can observe it.
            usage["cached_tokens"] = cached_tokens
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
