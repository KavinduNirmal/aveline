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
from app.schemas.customer_memory import MemoryAgentOutput, normalise_memory_content
from app.tools.registry import ToolRegistry

logger = logging.getLogger("aveline.agent.customer_memory")

SKIPPED = "skipped"
SUCCESS = "success"
OUT_OF_SCOPE = "out_of_scope"

#: Reported when the consent store could not be read. It is not a persisted consent value; it is
#: the fail-closed outcome, and it tells the orchestrator to stop the whole ordered pipeline.
CONSENT_UNAVAILABLE = "unavailable"

#: How near a stored note must be to the current message to count as context for it (gap B1).
#: The backend applies this as a cosine-similarity predicate, so a customer whose notes say nothing
#: about this message retrieves nothing instead of the five least-unrelated notes on file. It is
#: deliberately low: the cost of missing a marginally relevant note is a less personal reply, while
#: the cost of including an unrelated one is a draft that contradicts what the customer just said.
MEMORY_SIMILARITY_FLOOR = 0.25

#: The provenance the agent attaches to a preference the customer stated themselves. A stated
#: preference is explicit and is believed strongly; the value is the extraction's own confidence, not
#: a property of the store.
STATED_PREFERENCE_CONFIDENCE = 0.9

#: The confidence attached to a note a staff member deliberately asked us to record ("add a note for
#: this customer: ..."). It is 1.0 because, unlike a preference inferred from what a customer said,
#: this is not an extraction: a human decided the fact was worth keeping and typed it themselves, so
#: there is nothing for the store to discount. A lower value would make a deliberate instruction look
#: like a guess next to the customer's own statements.
STAFF_NOTE_CONFIDENCE = 1.0


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


def _usage_from_result(result: Any) -> dict[str, Any] | None:
    """Token usage from a langchain completion, in the shape the reporter expects (ADR-010).

    Shared by the customer draft and the staff answer: a second extractor would be a second place
    for the reporter's field names to drift out of step with the ones the model actually returns.
    """
    meta = getattr(result, "usage_metadata", None) or {}
    token_details = meta.get("input_token_details") or {}
    cached_tokens = int(token_details.get("cache_read") or 0)
    usage: dict[str, Any] = {
        "input_tokens": int(meta.get("input_tokens") or 0),
        "output_tokens": int(meta.get("output_tokens") or 0),
    }
    if cached_tokens > 0:
        # Cached prompt tokens are span attributes only today; the cached direction is the one
        # additive token metric (R-15). Surfaced on state so run metrics can observe it.
        usage["cached_tokens"] = cached_tokens
    return usage


def _unique_notes(memories: Any) -> list[dict[str, Any]]:
    """The customer's stored notes, first occurrence first, near-identical rows collapsed.

    The store now enforces one live note per normalised statement (a partial unique index over the
    statement key), so this is the second line of defence rather than the only one: it still collapses
    rows written before that constraint existed, and it makes the block's own de-duplication
    independent of what the write path did. What it cannot collapse is a *reworded* statement, which
    is a different problem and needs a similarity threshold rather than a key.

    Each note keeps the provenance the search returned (source, explicitness, confidence and
    similarity). Dropping it here is what left the one surface that shows a reader what is on file
    unable to say whether a note was stated or inferred (gaps B2, B3).
    """
    notes: list[dict[str, Any]] = []
    seen: set[str] = set()

    for memory in memories or []:
        if not isinstance(memory, dict):
            continue
        content = str(memory.get("content") or "").strip()
        if not content:
            continue
        key = normalise_memory_content(content)
        if key in seen:
            continue
        seen.add(key)

        note: dict[str, Any] = {
            "content": content,
            "category": str(memory.get("category") or "").strip() or "memory",
        }
        source = memory.get("source")
        if isinstance(source, str) and source.strip():
            note["source"] = source.strip()
        if isinstance(memory.get("isExplicit"), bool):
            note["is_explicit"] = memory["isExplicit"]
        confidence = memory.get("confidence")
        if isinstance(confidence, (int, float)):
            note["confidence"] = float(confidence)
        similarity = memory.get("similarity")
        if isinstance(similarity, (int, float)):
            note["similarity"] = float(similarity)

        notes.append(note)

    return notes


def _notes_sentence(contents: list[str]) -> str:
    """How Aveline refers to the notes she is about to show, without reciting them.

    A count, not a list: the notes arrive as their own content block, and a sentence that enumerates
    them reads as a malfunction as soon as the store holds near-duplicates.
    """
    if not contents:
        return ""
    if len(contents) == 1:
        return "One note is on file."
    return f"{len(contents)} notes are on file."


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

    async def _consent_refusal_to_write(self, state: MemoryAgentState) -> str | None:
        """The message to show staff when consent forbids writing, or ``None`` to proceed.

        This node runs *before* ``check_consent`` and short-circuits the run when it answers, so
        without this guard a note or a rename reached the store for a customer who had revoked
        consent. ``persist`` already refuses to write in that case, and the documented contract is
        that revocation means nothing is stored - a staff instruction is not an exception to it.
        Consent is a property of the customer, not of who is asking.

        Fails closed on a backend failure for the same reason ``check_consent`` does: a privacy
        control that cannot read its own state must not write.
        """
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")
        try:
            consent = await self.registry.get_customer_consent(str(org_id), str(customer_id))
        except Exception:  # noqa: BLE001 - a privacy control must fail closed, never write
            logger.warning(
                "Consent check failed; refusing to write (fail closed). customer_id=%s",
                customer_id,
                exc_info=True,
            )
            return (
                "I could not check this customer's consent, so I have not changed anything. "
                "Try again once the customer book is reachable."
            )

        if ((consent or {}).get("consentStatus") or "pending") == "revoked":
            return (
                "This customer has revoked consent, so nothing may be stored about them. "
                "No note or update was applied."
            )
        return None

    async def apply_staff_update(self, state: MemoryAgentState) -> dict[str, Any]:
        """Apply an explicit staff instruction to the bound customer's details.

        Staff type "please update this customer with the name X and number Y" - or "please add a
        note for this customer: he prefers green tea" - into the Salon. This is the only path that
        writes identity fields or staff notes from chat, so it is deliberately narrow:

        - **Staff only.** A customer messaging in is never taken as instructing us to rewrite their
          own record. ``staff_query`` is what separates the two.
        - **Explicit only.** ``extract_customer_update`` requires an update verb or a note phrase; a
          message that merely mentions a name is not an instruction to store one.
        - **Bound customer only.** With no customer on the conversation there is nobody to update or
          annotate, and guessing from a name would risk editing the wrong record. It asks instead.
        - **Never creates.** Naming someone not on file is a different operation; silently creating
          a record from a chat message would be wrong.

        A failure is reported, not swallowed: an update that appears to succeed while doing nothing
        is worse than one that says why it could not. That applies to the note write too - a staff
        member who asked for a note to be recorded must not be told nothing happened.
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

        profile = state.get("profile")
        # Before the first write, not after: this node short-circuits the run, so `check_consent`
        # never gets to refuse a note or a rename for a revoked customer.
        consent_refusal = await self._consent_refusal_to_write(state)
        if consent_refusal is not None:
            return {"parsed_intent": intent, "customer_update_error": consent_refusal}

        # A note-only instruction must not send an identity PATCH with no fields: the backend would
        # receive a change request that changes nothing. The note below is the whole instruction.
        if instruction.full_name is not None or instruction.phone_number is not None:
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

        if instruction.note is not None:
            # The note is stored as staff wrote it. It is attached to the bound customer, so a
            # pronoun ("He prefers green tea") has a clear referent in context; rewriting it into
            # third person would put words in the staff member's mouth and discard the observation
            # they chose to record. `category="note"` keeps it distinguishable from a preference the
            # customer stated, and `source="staff"` records who vouched for it.
            try:
                await self.registry.save_customer_memory(
                    str(org_id),
                    str(customer_id),
                    instruction.note,
                    "note",
                    source="staff",
                    is_explicit=True,
                    confidence=STAFF_NOTE_CONFIDENCE,
                )
            except Exception as exc:  # noqa: BLE001 - reported to staff, never silently dropped
                logger.warning("Staff note save failed: %s", exc)
                return {
                    "parsed_intent": intent,
                    "customer_update_error": (
                        "I could not save that note to the customer book, so nothing was noted."
                    ),
                }

        return {
            "parsed_intent": intent,
            "profile": profile,
            "applied_customer_update": {
                "full_name": instruction.full_name,
                "phone_number": instruction.phone_number,
                "note": instruction.note,
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
        """A staff-facing sentence describing what an instruction did, or ``None``.

        Reports identity changes and a stored note. Only reached when the update node
        short-circuited the run, so it is never produced for an ordinary message.
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

        note = applied.get("note")
        # A note is stored verbatim, so it can end in its own punctuation; the sentence must not
        # then read "tea..".
        noted: str | None = None
        if note:
            noted = f"Noted for this customer: {note}"
            if not noted.endswith((".", "!", "?")):
                noted += "."

        if not changed:
            # A note-only instruction must never answer "Nothing was changed.": that sentence is the
            # silent no-op this path exists to end. The note was written, so it is confirmed.
            if noted:
                return noted
            return "Nothing was changed."

        confirmation = "Updated this customer's " + " and ".join(changed) + "."
        if noted:
            confirmation += f" {noted}"
        return confirmation

    async def check_consent(self, state: MemoryAgentState) -> dict[str, Any]:
        """Refuse to process customers who have revoked consent.

        A backend failure **fails closed**: personalization does not run for a customer whose
        consent could not be read, and the node reports a skip. It must never raise - an earlier
        version let a consent-store outage propagate through the graph and 500 the whole query
        (plan §8.2 defect 4 / Phase 1 item 1.4).

        The returned ``consent_status`` is what the orchestrator routes on, so a revoked or
        unverifiable customer is not merely skipped inside this sub-graph: it stops the visual and
        commerce agents too (item 1.3).
        """
        org_id = state.get("org_id")
        customer_id = state.get("customer_id")

        try:
            consent = await self.registry.get_customer_consent(str(org_id), str(customer_id))
        except Exception:  # noqa: BLE001 - a privacy control must fail closed, never 500
            logger.warning(
                "Consent check failed; refusing to personalize (fail closed). customer_id=%s",
                customer_id,
                exc_info=True,
            )
            return {**self._skip("consent check unavailable"), "consent_status": CONSENT_UNAVAILABLE}

        status = (consent or {}).get("consentStatus") or "pending"
        if status == "revoked":
            return {**self._skip("customer has revoked consent"), "consent_status": "revoked"}
        return {"consent_status": status}

    async def parse(self, state: MemoryAgentState) -> dict[str, Any]:
        """Extract structured intent and preference/event/experience signals from the message."""
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
            "_experience_signal": parsed["experience"],
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
                str(org_id),
                str(customer_id),
                state.get("message", ""),
                top_k=5,
                min_similarity=MEMORY_SIMILARITY_FLOOR,
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
            # The customer said this about themselves in the message being processed, so it is an
            # explicit, conversation-sourced statement. Both facts were computed here and then
            # dropped by a tool that accepted only content and category, which is why every
            # agent-written memory read as an unvouched-for inference (gap A1).
            if await self._try_save_memory(
                org_id,
                customer_id,
                content,
                "preference",
                source="conversation",
                is_explicit=True,
                confidence=STATED_PREFERENCE_CONFIDENCE,
            ):
                extracted.append(
                    {
                        "content": content,
                        "category": "preference",
                        "source": "conversation",
                        "is_explicit": True,
                        "confidence": STATED_PREFERENCE_CONFIDENCE,
                    }
                )
                # The interaction brief's preference summary is assembled from the preferences
                # table, not from memories, so a stated preference that only became a memory row
                # left the brief's own summary empty while the store held the fact (gap C4).
                # The memory row stays: it is the searchable, dated record of what was said.
                await self._try_save_preference(org_id, customer_id, signal)

        for event in state.get("detected_events", []):
            on_date = f" on {event['event_date']}" if event.get("event_date") else ""
            content = f"{name} has a {event['event_type']}{on_date}"
            # A date the customer gave in this conversation is a stated event, so it gets the same
            # provenance as a stated preference. The agent's own parse is what makes it explicit; a
            # date inferred from context rather than stated would not be.
            if await self._try_save_memory(
                org_id,
                customer_id,
                content,
                "event",
                source="conversation",
                is_explicit=True,
                confidence=STATED_PREFERENCE_CONFIDENCE,
            ):
                extracted.append(
                    {
                        "content": content,
                        "category": "event",
                        "source": "conversation",
                        "is_explicit": True,
                        "confidence": STATED_PREFERENCE_CONFIDENCE,
                    }
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

        # What the customer reported about the service, which the store had no record of before
        # (gap A7): the complaint/sentiment categories were declared and never written. The message
        # is the evidence, so the note quotes it rather than paraphrasing - a paraphrase is where
        # "the delivery was late" becomes "the customer is impatient".
        experience = state.get("_experience_signal")
        if experience in {"complaint", "sentiment"}:
            content = self._experience_note(name, experience, state.get("message", ""))
            if await self._try_save_memory(
                org_id,
                customer_id,
                content,
                experience,
                source="conversation",
                is_explicit=True,
                confidence=STATED_PREFERENCE_CONFIDENCE,
            ):
                extracted.append(
                    {
                        "content": content,
                        "category": experience,
                        "source": "conversation",
                        "is_explicit": True,
                        "confidence": STATED_PREFERENCE_CONFIDENCE,
                    }
                )

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

    async def _try_save_memory(
        self,
        org_id: str,
        customer_id: str,
        content: str,
        category: str,
        source: str | None = None,
        is_explicit: bool | None = None,
        confidence: float | None = None,
    ) -> bool:
        """Attempt to persist a memory; returns True on success (best-effort)."""
        try:
            await self.registry.save_customer_memory(
                org_id,
                customer_id,
                content,
                category,
                source=source,
                is_explicit=is_explicit,
                confidence=confidence,
            )
            return True
        except Exception:  # noqa: BLE001 - best-effort memory save
            logger.warning("Failed to save customer memory (category=%s).", category)
            return False

    async def _try_save_preference(
        self, org_id: str, customer_id: str, signal: dict[str, Any]
    ) -> None:
        """Mirror a stated preference into the preferences table the brief reads (gap C4).

        Best-effort like every other write here: the memory row is the record of what was said, and
        a failure to also update the summary must not fail the run.
        """
        try:
            await self.registry.save_customer_preference(
                org_id,
                customer_id,
                key=str(signal.get("preference_key") or "general"),
                value=str(signal.get("preference_value") or "").strip(),
                source="conversation",
                is_explicit=True,
                confidence=STATED_PREFERENCE_CONFIDENCE,
            )
        except Exception:  # noqa: BLE001 - best-effort preference save
            logger.warning("Failed to save customer preference (key=%s).", signal.get("preference_key"))

    @staticmethod
    def _experience_note(name: str, experience: str, message: str) -> str:
        """A note recording what the customer reported, quoting the message they sent.

        Quoted, not paraphrased: this is the sentence an associate will read to decide how serious
        the complaint is, and the customer's own words are the evidence for it.
        """
        verb = "reported a problem" if experience == "complaint" else "was pleased"
        said = " ".join(message.split())
        if len(said) > 280:
            said = said[:277].rstrip() + "..."
        return f'{name} {verb}: "{said}"'

    async def compose_output(self, state: MemoryAgentState) -> dict[str, Any]:
        """Assemble the final output.

        Two audiences (ADR-019):
        - Inbound CUSTOMER message: a staff-facing ``interaction_brief`` plus a customer-facing
          draft (``draft_response``, surfaced as a Suggestion for staff to approve/send).
        - STAFF query (no inbound direction): Ava answers the staff directly (the model when one is
          configured, the grounded template otherwise) and produces NO customer-facing draft /
          Suggestion.
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
            interaction_brief, usage = await self._staff_answer(state, name, profile, backend)
            draft = None
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
            # What was already on file, kept out of the brief sentence so the publisher can render
            # it as its own block (ADR-016's `at_a_glance`).
            "memories_on_file": _unique_notes(state.get("semantic_context")),
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
        """Assemble the staff-facing digest (name/status + semantic context + events/tags).

        The customer's own description leads when the backend brief carries one. It is the
        customer-level prose the brief exists to surface - who this person is - and the mechanical
        parts that follow are facts about them. The agent fetched it and never read it, so the one
        sentence an associate needs before making contact was the one line missing from the brief.
        """
        semantic_context = state.get("semantic_context") or []
        tags = profile.get("tags") or []
        status = backend.get("status") or profile.get("status") or "new"
        display_name = backend.get("customerName") or name

        parts: list[str] = []
        description = backend.get("description")
        if isinstance(description, str) and description.strip():
            parts.append(description.strip())
        parts.append(f"{display_name} ({status})")
        if semantic_context:
            parts.append(f"Context: {semantic_context[0].get('content')}")
        if backend.get("upcomingEvents"):
            parts.append(f"Upcoming: {backend['upcomingEvents']}")
        backend_tags = backend.get("tags")
        merged_tags = backend_tags if isinstance(backend_tags, list) and backend_tags else tags
        if merged_tags:
            parts.append(f"Tags: {', '.join(merged_tags)}")
        return " | ".join(parts)

    @staticmethod
    def _grounded_facts(backend: dict[str, Any]) -> list[str]:
        """The grounded backend facts, in a stable order, shared by the template and the model.

        The stored notes are deliberately absent: they are rendered as their own content block
        rather than recited here.
        """
        prefs = backend.get("preferenceSummary")
        events = backend.get("upcomingEvents")
        tags = backend.get("tags") if isinstance(backend.get("tags"), list) else []

        facts: list[str] = []
        if prefs:
            facts.append(f"preferences: {prefs}")
        if events:
            facts.append(f"upcoming events: {events}")
        if tags:
            facts.append(f"tags: {', '.join(tags)}")
        return facts

    def _staff_text(
        self,
        state: MemoryAgentState,
        name: str,
        profile: dict[str, Any],
        backend: dict[str, Any],
    ) -> str:
        """Answer a STAFF query directly (no customer-facing draft).

        This is the deterministic fallback for :meth:`_staff_answer`, used whenever no LLM is
        configured or the provider call fails. It uses the real backend facts (status, preferences,
        upcoming events, tags) so the answer is grounded. Event-style queries answer about events; a
        brand-new customer is announced; other queries summarise what is known, or state when
        nothing is on file yet.
        """
        display_name = backend.get("customerName") or name
        status = backend.get("status") or profile.get("status") or "new"
        intent_type = (state.get("parsed_intent") or {}).get("intent_type")
        events = backend.get("upcomingEvents")

        # What the boutique has actually recorded about this customer. These are semantic memories
        # (events, preferences, complaints), which the interaction brief does not carry - so a
        # question like "what do we have on file for her?" answered "nothing on file yet" while
        # three memories sat in the store. Collapsed to what a reader would call one note, because
        # the store itself holds duplicates.
        remembered = [note["content"] for note in _unique_notes(state.get("semantic_context"))]

        if intent_type == "event_query":
            if events:
                answer = f"Upcoming events for {display_name}: {events}."
            # The absence of a dated event stays the headline answer; what else is on file follows
            # as its own block, because it is usually what the staff member actually wanted to know.
            elif remembered:
                answer = f"No upcoming events on file for {display_name}. {_notes_sentence(remembered)}"
            else:
                answer = f"No upcoming events on file for {display_name}."
        else:
            # General staff query: a readable, grounded summary. The notes themselves are NOT
            # recited here - the publisher renders them as their own block (see `memories_on_file`),
            # because joining them into the sentence produced "on file: X; X; X" for every
            # near-duplicate the store had accumulated, which reads as a malfunction rather than as
            # an answer.
            facts = self._grounded_facts(backend)
            notes = _notes_sentence(remembered)

            if status == "new":
                if not facts and not remembered:
                    answer = f"{display_name} is a new customer - nothing on file yet."
                else:
                    # Announcing "new" stays useful for onboarding even when some memory exists, so
                    # the framing is kept and the facts are appended rather than replacing it.
                    sentence = f"{display_name} is a new customer"
                    if facts:
                        sentence += f" - {'; '.join(facts)}"
                    sentence += "."
                    answer = f"{sentence} {notes}" if notes else sentence
            elif not facts and not remembered:
                answer = f"{display_name} ({status}) has no preferences or events on file yet."
            else:
                sentence = f"{display_name} ({status})"
                if facts:
                    sentence += f" - {'; '.join(facts)}"
                sentence += "."
                answer = f"{sentence} {notes}" if notes else sentence

        # The customer-level description leads, exactly as it does in the inbound brief: it is who
        # the customer is, and the mechanical facts that follow are about them.
        description = backend.get("description")
        if isinstance(description, str) and description.strip():
            return f"{description.strip()} {answer}"
        return answer

    async def _staff_answer(
        self,
        state: MemoryAgentState,
        name: str,
        profile: dict[str, Any],
        backend: dict[str, Any],
    ) -> tuple[str, dict[str, Any] | None]:
        """Return ``(interaction_brief, usage)`` for a STAFF query.

        A staff query used to be answered only by the ``_staff_text`` template, which counts what is
        on file ("3 notes are on file") instead of answering the question from it. With a model
        configured the question is now answered from the same grounded facts and retrieved
        memories; the template stays as the fallback, so the graph never depends on the provider and
        the offline answer is byte-for-byte what it was.

        The model is told to use only the supplied facts. This is staff-facing, so it may be more
        direct than a customer draft, but a fact it invented about a customer would be repeated to
        that customer - "that is not on file" is always the better answer.
        """
        fallback = self._staff_text(state, name, profile, backend)
        if self.llm is None:
            return fallback, None

        context_lines = [f"Staff question: {state.get('message', '')}"]

        # The record is fenced off and every part of it is named. A single unlabelled "Facts on
        # file" blob let the model reclassify a row on the way out - an upcoming event came back as
        # "the only preference we hold is a wedding" - because nothing in the prompt said which
        # source a line came from.
        record: list[str] = []
        description = backend.get("description")
        if isinstance(description, str) and description.strip():
            record.append(f"- Customer description: {description.strip()}")

        prefs = backend.get("preferenceSummary")
        if prefs:
            record.append(f"- Preferences: {prefs}")

        events = backend.get("upcomingEvents")
        if events:
            record.append(f"- Upcoming events: {events}")

        tags = backend.get("tags") if isinstance(backend.get("tags"), list) else []
        if tags:
            record.append(f"- Tags: {', '.join(tags)}")

        remembered = [note["content"] for note in _unique_notes(state.get("semantic_context"))]
        if remembered:
            record.append("- Recorded notes:")
            record.extend(f"  - {note}" for note in remembered)

        if record:
            context_lines.append(
                "ON FILE - the customer record, and the only thing that may be described as on "
                "file:\n" + "\n".join(record)
            )

        context_lines.append(
            "Answer the staff member's question directly, in 1-3 sentences. Answer from the ON "
            "FILE section only. The conversation excerpt in the system prompt tells you what is "
            "being discussed; it is NOT the record, so never present something merely said in the "
            "conversation as though it were on file - if that is the situation, say it came up in "
            "the conversation but is not recorded. Keep every fact in the category it is listed "
            "under: an upcoming event is not a preference, and a preference is not a purchase. "
            "Never invent or guess a fact about the customer, and never state what is on file when "
            "the section is absent or empty - say nothing is on file instead. Reply with ONLY the "
            "plain text of the answer - no JSON, no code fences, no labels. Do not write a message "
            "to the customer."
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
            logger.warning("LLM staff answer failed; falling back to template.", exc_info=True)
            return fallback, None

        answer = _unwrap_reply(getattr(result, "content", None) or "")
        if not answer:
            return fallback, None

        return answer, _usage_from_result(result)

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

        return draft, _usage_from_result(result)

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
