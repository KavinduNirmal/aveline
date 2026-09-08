import json
import logging
from typing import Annotated
from uuid import UUID

from fastapi import APIRouter, Body, Depends, Request
from fastapi.responses import StreamingResponse

from app.core.security import require_internal_token
from app.events.message_publisher import publish_agent_messages
from app.schemas.query import AgentQueryRequest, AgentQueryResponse
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
    broadcast them into the Salon. Guarded by the internal service token.
    """
    logger.info(
        "Agent query received: thread_id=%s",
        payload.thread_id,
        extra={"action": "agent_query", "thread_id": payload.thread_id},
    )
    result = await run_concierge(
        payload.query,
        org_context=payload.org_context,
        thread_id=payload.thread_id,
    )

    await _publish_result(request, payload, result)

    return AgentQueryResponse(
        status="ok",
        result=result,
        thread_id=payload.thread_id,
    )


async def _publish_result(request: Request, payload: AgentQueryRequest, result) -> None:
    """Publish persona-attributed ``message.created`` events when possible.

    Publishing is best-effort: if no event bus is configured, or the org id is
    missing, the query still succeeds (the API may poll or the client may refresh).
    """
    event_bus = getattr(request.app.state, "event_bus", None)
    if event_bus is None:
        return

    org_context = payload.org_context or {}
    org_id_value = org_context.get("organization_id") or org_context.get("org_id")
    if org_id_value is None:
        return

    try:
        org_id = UUID(str(org_id_value))
    except (ValueError, TypeError):
        logger.warning("Invalid organization_id in org_context; skipping message publish.")
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

