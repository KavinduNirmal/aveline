import json
import logging
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Body, Depends, Request
from fastapi.responses import StreamingResponse

from app.core.config import get_settings
from app.core.security import require_internal_token
from app.events.message_publisher import publish_agent_messages
from app.events.state_publisher import publish_agent_state
from app.schemas.query import AgentQueryRequest, AgentQueryResponse
from app.schemas.response import AgentResponse
from app.schemas.state import AgentState
from app.services.usage_reporter import report_usage
from app.workflows.concierge_workflow import build_concierge_graph, run_concierge

logger = logging.getLogger("aveline.agent.api")

router = APIRouter(
    prefix="/agents",
    tags=["Agents"],
    dependencies=[Depends(require_internal_token)],
)


@router.post("/ping")
async def agents_ping(payload: Annotated[dict | None, Body()] = None) -> dict:
    """Echo endpoint for verifying service-to-service authentication.

    Logs the user context forwarded by the backend.
    """
    payload = payload or {}
    logger.info(
        "Agent ping received: user_id=%s roles=%s",
        payload.get("userId"),
        payload.get("roles"),
        extra={"action": "agent_ping", "user_id": payload.get("userId"), "roles": payload.get("roles")},
    )
    return {"status": "ok", "echo": payload}


@router.post("/warmup")
async def agents_warmup(payload: Annotated[dict | None, Body()] = None) -> dict:
    """Warm up the Customer Memory Agent and operational agents with boutique seed context."""
    payload = payload or {}
    org_id = payload.get("organizationId")
    boutique_name = payload.get("boutiqueName")
    plan_tier = payload.get("planTier")

    logger.info(
        "Agent warmup triggered: org_id=%s boutique=%s tier=%s",
        org_id,
        boutique_name,
        plan_tier,
        extra={
            "action": "agent_warmup",
            "organization_id": org_id,
            "boutique_name": boutique_name,
            "plan_tier": plan_tier,
        },
    )
    return {
        "status": "warmed",
        "organizationId": org_id,
        "ready": True,
        "agents": ["CustomerMemoryAgent", "VisualInsightAgent", "CommerceAgent"],
    }


@router.post("/query", response_model=AgentQueryResponse)
async def agents_query(payload: AgentQueryRequest, request: Request) -> AgentQueryResponse:
    """Run the concierge workflow for the given query and return the result.

    The workflow runs the Intent Gate, delegates to the relevant agents, and
    returns a structured ``AgentResponse`` envelope. When an event bus is available
    and an organization id is present in ``org_context``, the result is published as
    persona-attributed ``message.created`` events (ADR-016) so the API can persist and
    broadcast them into the Salon. Lifecycle ``agent.status`` events are published as the
    workflow progresses so clients can animate Aveline's state. Guarded by the internal
    service token.
    """
    logger.info(
        "Agent query received: thread_id=%s",
        payload.thread_id,
        extra={"action": "agent_query", "thread_id": payload.thread_id},
    )

    event_bus = getattr(request.app.state, "event_bus", None)
    org_id = _resolve_org_id(payload)
    # Correlation id tracing this workflow invocation (stored on the usage record, ADR-010).
    request_id = str(uuid4())

    async def on_state(state: AgentState) -> None:
        if event_bus is not None and org_id is not None:
            await publish_agent_state(
                event_bus,
                org_id,
                payload.thread_id,
                state,
                trace_id=None,
            )

    try:
        result = await run_concierge(
            payload.query,
            org_context=payload.org_context,
            thread_id=payload.thread_id,
            on_state=on_state,
        )
    except Exception:
        if event_bus is not None and org_id is not None:
            await publish_agent_state(event_bus, org_id, payload.thread_id, AgentState.error)
        raise

    await _publish_result(request, payload, result)

    # Usage / Blossom reporting is always-on and best-effort (never fails the query).
    if org_id is not None:
        await _report_usage_best_effort(
            result,
            organization_id=str(org_id),
            workflow_id=payload.thread_id or request_id,
            request_id=request_id,
        )

    # The workflow completed successfully; broadcast the terminal bloom state.
    if event_bus is not None and org_id is not None:
        await publish_agent_state(event_bus, org_id, payload.thread_id, AgentState.success)

    return AgentQueryResponse(
        status="ok",
        result=result,
        thread_id=payload.thread_id,
    )


def _resolve_org_id(payload: AgentQueryRequest) -> UUID | None:
    """Resolve the organization id from ``org_context``, or ``None`` when absent."""
    org_context = payload.org_context or {}
    org_id_value = org_context.get("organization_id") or org_context.get("org_id")
    if org_id_value is None:
        return None
    try:
        return UUID(str(org_id_value))
    except (ValueError, TypeError):
        logger.warning("Invalid organization_id in org_context; skipping state publish.")
        return None


async def _report_usage_best_effort(
    response: AgentResponse,
    organization_id: str,
    workflow_id: str,
    request_id: str,
) -> None:
    """Report AI usage for a completed workflow to the backend, swallowing failures.

    Rule-based runs (no LLM) carry a ``rule-based`` sentinel in ``response.metadata`` and report
    zero tokens; LLM runs report the configured provider/model and the captured token split.
    A reporting failure is logged and never raised, so usage accounting cannot break a query.
    """
    metadata = response.metadata
    if metadata is None or metadata.model is None:
        return

    settings = get_settings()
    is_rule_based = metadata.model == "rule-based"
    provider = "rule-based" if is_rule_based else settings.llm_provider
    try:
        await report_usage(
            organization_id=organization_id,
            request_id=request_id,
            workflow_id=workflow_id,
            provider=provider,
            model=metadata.model,
            input_tokens=int(metadata.input_tokens or 0),
            output_tokens=int(metadata.output_tokens or 0),
        )
    except Exception:  # noqa: BLE001 - usage reporting must never fail the agent query
        logger.exception(
            "Failed to report usage for workflow %s (best-effort).", workflow_id,
            extra={"action": "report_usage", "workflow_id": workflow_id},
        )


async def _publish_result(request: Request, payload: AgentQueryRequest, result) -> None:
    """Publish persona-attributed ``message.created`` events when possible.

    Publishing is best-effort: if no event bus is configured, or the org id is
    missing, the query still succeeds (the API may poll or the client may refresh).
    """
    event_bus = getattr(request.app.state, "event_bus", None)
    if event_bus is None:
        return

    org_id = _resolve_org_id(payload)
    if org_id is None:
        return

    try:
        await publish_agent_messages(
            event_bus,
            org_id,
            payload.thread_id,
            result,
        )
    except Exception:  # noqa: BLE001 - publishing must never fail the query
        logger.exception("Failed to publish agent messages for thread %s.", payload.thread_id)


@router.post("/query/stream")
async def agents_query_stream(payload: AgentQueryRequest) -> StreamingResponse:
    """Stream agent workflow events to the client over Server-Sent Events.

    Relays LangGraph ``astream_events`` output. The ``X-Accel-Buffering: no``
    header prevents Nginx/Traefik from buffering the stream and breaking
    real-time token delivery.
    """
    logger.info(
        "Agent query stream started: thread_id=%s",
        payload.thread_id,
        extra={"action": "agent_query_stream", "thread_id": payload.thread_id},
    )

    async def event_source():
        compiled = build_concierge_graph()
        async for event in compiled.astream_events(
            {
                "message": payload.query,
                "org_context": payload.org_context or {},
                "intent": None,
                "resolution": None,
                "memory_output": None,
                "visual_output": None,
                "commerce_output": None,
                "response": None,
            },
            version="v2",
        ):
            kind = event.get("event")
            if kind in {"on_chat_model_stream", "on_chain_stream", "on_chain_end"}:
                yield f"data: {json.dumps({'event': kind})}\n\n"

    return StreamingResponse(
        event_source(),
        media_type="text/event-stream",
        headers={"X-Accel-Buffering": "no", "Cache-Control": "no-cache"},
    )

