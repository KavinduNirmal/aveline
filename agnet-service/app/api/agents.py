import json
import logging
import time
from datetime import UTC, datetime
from typing import Annotated, Any
from uuid import UUID, uuid4

from fastapi import APIRouter, Body, Depends, Request
from fastapi.responses import StreamingResponse

from app.core.config import get_settings
from app.core.security import require_internal_token
from app.events.message_publisher import publish_agent_messages
from app.events.state_publisher import publish_agent_state
from app.observability.metrics import get_agent_metrics
from app.schemas.query import AgentQueryRequest, AgentQueryResponse
from app.schemas.response import AgentResponse
from app.schemas.state import AgentState
from app.services.usage_reporter import report_agent_run, report_usage
from app.telemetry.agent_telemetry import NODE_STEP_KIND, TelemetryCollector
from app.workflows.concierge_workflow import WORKFLOW_NAME, build_concierge_graph, run_concierge

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
    # The run id must be unique per invocation. It used to be `payload.thread_id`, which is stable
    # for the whole conversation - so the FIRST run was ingested and marked terminal, and every
    # later message in that thread posted the same id and was rejected with 409 "already terminal".
    # Run telemetry therefore recorded one run per conversation and silently dropped the rest,
    # which also hid any HITL pause after the first message.
    run_workflow_id = str(uuid4())
    org_context = payload.org_context or {}
    # The run collector is the step-record producer: every graph node writes a row through it
    # and Slice 4a's instruments observe the same run without a second mechanism.
    collector = TelemetryCollector(
        workflow_id=run_workflow_id,
        organization_id=str(org_id) if org_id is not None else None,
        request_id=request_id,
        conversation_id=_optional_id(org_context.get("conversation_id")),
        customer_id=_optional_id(org_context.get("customer_id")),
    )
    run_started = time.perf_counter()
    run_status = "Succeeded"

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
            collector=collector,
        )
    except Exception:
        run_status = "Failed"
        if event_bus is not None and org_id is not None:
            await publish_agent_state(event_bus, org_id, payload.thread_id, AgentState.error)
        raise
    finally:
        _record_run_outcome(
            status=run_status,
            duration_s=time.perf_counter() - run_started,
            org_id=org_id,
            collector=collector,
        )

    await _publish_result(request, payload, result)

    # Usage / Blossom reporting is always-on and best-effort (never fails the query).
    if org_id is not None:
        await _report_usage_best_effort(
            result,
            organization_id=str(org_id),
            workflow_id=run_workflow_id,
            request_id=request_id,
            collector=collector,
            run_status=run_status,
        )

    # The workflow completed successfully; broadcast the terminal bloom state.
    if event_bus is not None and org_id is not None:
        await publish_agent_state(event_bus, org_id, payload.thread_id, AgentState.success)

    return AgentQueryResponse(
        status="ok",
        result=result,
        thread_id=payload.thread_id,
    )


def _optional_id(value: Any) -> str | None:
    """A stringified id for the telemetry payload, or ``None`` when absent/blank.

    Org context arrives as loose JSON, so an id may be a UUID, a string, or missing entirely.
    The API's ingest stores these as plain columns, so a string is the right shape either way.
    """
    if value is None:
        return None
    text = str(value).strip()
    return text or None


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


def _record_run_outcome(
    *,
    status: str,
    duration_s: float,
    org_id: UUID | None,
    collector: TelemetryCollector | None,
) -> None:
    """Record the run-level agent metrics and the per-step derived counters (Slice 4a).

    Only bounded label values are used: the workflow *name*, the status enum, node names,
    provider/model and error classes. A run id or step index never becomes a label (plan §7.3).
    """
    metrics = get_agent_metrics()
    metrics.record_run(workflow=WORKFLOW_NAME, status=status, duration_s=duration_s)
    if org_id is None:
        # G-14: a run with no resolvable organization is skipped by the reporting path; the
        # counter is what makes that skip observable instead of silent.
        metrics.record_unattributed_run()
    if collector is None:
        return
    settings = get_settings()
    for step in collector.steps:
        if step.attempt_number > 1:
            metrics.record_retry(node=step.node_name)
        if step.cached_tokens > 0:
            metrics.record_cached_tokens(
                provider=step.provider or settings.llm_provider,
                model=step.model or settings.llm_model,
                count=step.cached_tokens,
            )


def _run_status_from_response(response: AgentResponse) -> str:
    """Map the public response status onto the backend's bounded run-status enum."""
    status_map = {
        "success": "Succeeded",
        "pending_approval": "PausedForApproval",
        "out_of_scope": "Succeeded",
        "error": "Failed",
    }
    return status_map.get(str(response.status), "Succeeded")


async def _report_usage_best_effort(
    response: AgentResponse,
    organization_id: str,
    workflow_id: str,
    request_id: str,
    collector: TelemetryCollector | None = None,
    run_status: str | None = None,
) -> None:
    """Report AI usage for a completed workflow to the backend, swallowing failures.

    Rule-based runs (no LLM) carry a ``rule-based`` sentinel in ``response.metadata`` and report
    zero tokens; LLM runs report the configured provider/model and the captured token split.
    When ``collector`` carries the real graph steps, those rows are sent verbatim; otherwise a
    single synthetic step keeps direct callers working. A reporting failure is logged and never
    raised, so usage accounting cannot break a query.
    """
    metadata = response.metadata
    if metadata is None or metadata.model is None:
        return

    settings = get_settings()
    is_rule_based = metadata.model == "rule-based"
    provider = "rule-based" if is_rule_based else settings.llm_provider
    input_tokens = int(metadata.input_tokens or 0)
    output_tokens = int(metadata.output_tokens or 0)
    resolved_status = run_status or _run_status_from_response(response)

    try:
        await report_usage(
            organization_id=organization_id,
            request_id=request_id,
            workflow_id=workflow_id,
            provider=provider,
            model=metadata.model,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
        )
    except Exception:  # noqa: BLE001 - usage reporting must never fail the agent query
        logger.exception(
            "Failed to report usage for workflow %s (best-effort).", workflow_id,
            extra={"action": "report_usage", "workflow_id": workflow_id},
        )

    try:
        if collector is not None and collector.steps:
            # The real per-step rows produced on the graph path (Slice 4b). ``AgentRunTelemetry``
            # already serializes to the exact camelCase ``AgentRunReportRequest`` contract.
            run = collector.finalize(
                status=resolved_status,
                input_tokens=input_tokens,
                output_tokens=output_tokens,
                cached_tokens=sum(step.cached_tokens for step in collector.steps),
                actual_cost_usd=0.0,
            )
            run.blossom_units = float(metadata.blossoms_consumed or 0.0)
            run_payload = run.to_api_payload()
        else:
            run_payload = _synthetic_run_payload(
                response=response,
                organization_id=organization_id,
                workflow_id=workflow_id,
                request_id=request_id,
                provider=provider,
                run_status=resolved_status,
                input_tokens=input_tokens,
                output_tokens=output_tokens,
            )
        await report_agent_run(run_payload)
    except Exception:  # noqa: BLE001 - agent run telemetry must never fail the agent query
        logger.exception(
            "Failed to report agent run telemetry for workflow %s (best-effort).", workflow_id,
            extra={"action": "report_agent_run", "workflow_id": workflow_id},
        )


def _synthetic_run_payload(
    *,
    response: AgentResponse,
    organization_id: str,
    workflow_id: str,
    request_id: str,
    provider: str,
    run_status: str,
    input_tokens: int,
    output_tokens: int,
) -> dict:
    """Build the fallback single-step payload used when no collector was supplied."""
    metadata = response.metadata
    now_iso = datetime.now(UTC).isoformat()
    duration = int(metadata.duration_ms or 0) if metadata is not None else 0
    model = metadata.model if metadata is not None else None
    return {
        "workflowId": workflow_id,
        "organizationId": organization_id,
        "requestId": request_id,
        "triggerKind": "ApiRequest",
        "status": run_status,
        "agentsInvolved": ["customer_memory"] if "memory" in (model or "") else ["orchestrator"],
        "startedAt": now_iso,
        "completedAt": now_iso,
        "durationMs": duration,
        "toolCallCount": 0,
        "retryCount": 0,
        "inputTokens": input_tokens,
        "outputTokens": output_tokens,
        "cachedTokens": 0,
        "actualCostUsd": 0.0,
        "blossomUnits": float((metadata.blossoms_consumed if metadata is not None else 0) or 0.0),
        "steps": [
            {
                "stepIndex": 0,
                "agentKey": "orchestrator",
                "nodeName": "concierge_pipeline",
                "stepKind": NODE_STEP_KIND,
                "toolName": None,
                "status": "Succeeded" if run_status != "Failed" else "Failed",
                "attemptNumber": 1,
                "startedAt": now_iso,
                "completedAt": now_iso,
                "durationMs": duration,
                "provider": provider,
                "model": model,
                "inputTokens": input_tokens,
                "outputTokens": output_tokens,
                "cachedTokens": 0,
                "actualCostUsd": 0.0,
                "argsHash": None,
                "resultBytes": None,
                "errorCode": None,
            }
        ],
    }


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
    # G-10: streaming runs are otherwise indistinguishable from request/response runs.
    get_agent_metrics().record_stream_run()

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

