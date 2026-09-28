import json
import logging
import time
from datetime import UTC, datetime
from typing import Annotated, Any
from uuid import UUID, uuid4

from fastapi import APIRouter, Body, Depends, HTTPException, Request, status
from fastapi.responses import StreamingResponse

from app.core.config import get_settings
from app.core.security import require_internal_token
from app.events.message_publisher import publish_agent_messages
from app.events.state_publisher import publish_agent_state
from app.observability.metrics import get_agent_metrics
from app.schemas.approvals import AGENT_APPROVAL_DECISIONS, normalize_decision
from app.schemas.query import AgentQueryRequest, AgentQueryResponse, AgentResumeRequest
from app.schemas.response import AgentResponse
from app.schemas.state import AgentState
from app.services.usage_reporter import report_agent_run, report_usage
from app.telemetry.agent_telemetry import NODE_STEP_KIND, AgentRunTelemetry, TelemetryCollector
from app.tools.registry import ToolRegistry
from app.workflows.concierge_workflow import (
    WORKFLOW_NAME,
    NoPausedRunError,
    build_concierge_graph,
    resume_concierge,
    run_concierge,
)

logger = logging.getLogger("aveline.agent.api")

#: The start report is awaited before the workflow runs, so its timeout bounds how long a slow
#: API can delay an answer. Comfortably longer than a local write, far shorter than the 10s the
#: completion report may take.
_RUN_START_REPORT_TIMEOUT_SECONDS = 2.0

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

    # Open the run row before the workflow starts, so `agent.runs_running` counts something real for
    # as long as the run lasts. Until this existed the agent reported a run exactly once, at
    # completion, so `AgentWorkflowRuns` never held a `Running` row and that gauge was structurally
    # 0 - which is what made the Agents dashboard look dead while the agent was answering.
    if org_id is not None:
        await _report_run_started_best_effort(
            organization_id=str(org_id),
            workflow_id=run_workflow_id,
            request_id=request_id,
            collector=collector,
        )

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
    else:
        # A run that stopped for owner sign-off is not a success: the workflow has not finished, and
        # the owner's decision is what completes it. Reporting `Succeeded` here is what used to hide
        # every pause from the focus feed (ADR-024).
        run_status = _run_status_from_response(result)
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
        terminal = (
            AgentState.waiting
            if run_status == "PausedForApproval"
            else AgentState.success
        )
        await publish_agent_state(event_bus, org_id, payload.thread_id, terminal)

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


@router.post("/resume", response_model=AgentQueryResponse)
async def agents_resume(payload: AgentResumeRequest, request: Request) -> AgentQueryResponse:
    """Settle a paused workflow through its checkpoint (ADR-024, Decision 3).

    This is the *only* way an owner's decision reaches the graph. It resumes the run at the node that
    interrupted, so nothing before the pause re-executes; the previous implementation posted the
    decision back to ``/agents/query`` as a new question, which re-ran the whole pipeline and left the
    decision unread.

    A thread with no paused run is a 404, and a decision outside the published vocabulary is a 400:
    neither is guessed at, because guessing would settle an approval nobody made.
    """
    decision = normalize_decision(payload.decision)
    if decision is None:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=(
                f"Unsupported decision '{payload.decision}'. "
                f"Expected one of: {', '.join(sorted(AGENT_APPROVAL_DECISIONS))}."
            ),
        )

    logger.info(
        "Agent resume received: thread_id=%s decision=%s",
        payload.thread_id,
        decision,
        extra={"action": "agent_resume", "thread_id": payload.thread_id, "decision": decision},
    )

    event_bus = getattr(request.app.state, "event_bus", None)
    org_id = _org_id_or_none(payload.organization_id)
    request_id = str(uuid4())
    run_workflow_id = str(uuid4())
    collector = TelemetryCollector(
        workflow_id=run_workflow_id,
        organization_id=str(org_id) if org_id is not None else None,
        request_id=request_id,
        conversation_id=_optional_id(payload.conversation_id),
        customer_id=_optional_id(payload.customer_id),
    )

    resume_value: dict[str, Any] = {"decision": decision}
    if payload.comment is not None:
        resume_value["comment"] = payload.comment
    if payload.revised_discount is not None:
        resume_value["revised_discount"] = payload.revised_discount
    if payload.order_id is not None:
        resume_value["order_id"] = payload.order_id
    if payload.customer_name is not None:
        resume_value["customer_name"] = payload.customer_name

    async def on_state(state: AgentState) -> None:
        if event_bus is not None and org_id is not None:
            await publish_agent_state(event_bus, org_id, payload.thread_id, state, trace_id=None)

    run_started = time.perf_counter()
    run_status = "Succeeded"

    try:
        result = await resume_concierge(
            payload.thread_id,
            resume_value,
            on_state=on_state,
            collector=collector,
        )
    except NoPausedRunError:
        # Nothing to resume. Reported as "not found" rather than silently starting a new run: a
        # resume that quietly becomes a fresh query is the defect this endpoint exists to remove.
        run_status = "Failed"
        _record_run_outcome(
            status=run_status,
            duration_s=time.perf_counter() - run_started,
            org_id=org_id,
            collector=collector,
        )
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Thread '{payload.thread_id}' has no paused run awaiting a decision.",
        ) from None
    except Exception:
        run_status = "Failed"
        if event_bus is not None and org_id is not None:
            await publish_agent_state(event_bus, org_id, payload.thread_id, AgentState.error)
        raise
    else:
        run_status = _run_status_from_response(result)

    _record_run_outcome(
        status=run_status,
        duration_s=time.perf_counter() - run_started,
        org_id=org_id,
        collector=collector,
    )

    # Publishing uses the same persona-attributed path as a fresh query, so a settlement reaches the
    # Salon exactly like any other Aveline message.
    await _publish_result_for_thread(request, org_id, payload.thread_id, result)

    if org_id is not None:
        await _report_usage_best_effort(
            result,
            organization_id=str(org_id),
            workflow_id=run_workflow_id,
            request_id=request_id,
            collector=collector,
            run_status=run_status,
        )

    if event_bus is not None and org_id is not None:
        await publish_agent_state(event_bus, org_id, payload.thread_id, AgentState.success)

    return AgentQueryResponse(status="ok", result=result, thread_id=payload.thread_id)


def _resolve_org_id(payload: AgentQueryRequest) -> UUID | None:
    """Resolve the organization id from ``org_context``, or ``None`` when absent."""
    return _org_id_or_none((payload.org_context or {}).get("organization_id") or (payload.org_context or {}).get("org_id"))


def _org_id_or_none(org_id_value: Any) -> UUID | None:
    """Coerce a loose organization id into a ``UUID``, or ``None`` when it is not one."""
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
    """Map the public response status onto the backend's bounded run-status enum.

    A consent skip is ``Skipped``, not ``Succeeded``: the run was deliberately not performed, and
    counting it as a completed answer corrupts both the run metric and the usage record (item 1.6).
    """
    status_map = {
        "success": "Succeeded",
        "pending_approval": "PausedForApproval",
        "out_of_scope": "Succeeded",
        "skipped": "Skipped",
        "error": "Failed",
    }
    return status_map.get(str(response.status), "Succeeded")


async def _report_run_started_best_effort(
    *,
    organization_id: str,
    workflow_id: str,
    request_id: str,
    collector: TelemetryCollector,
) -> None:
    """Register the run as ``Running`` before the workflow starts (best-effort).

    The completion report used to be the only one, so no `Running` row was ever written and the
    `aveline_agent_runs_running_count` gauge had no producer. This is the missing half.

    Deliberately **awaited**, with a short timeout, rather than fired and forgotten: a start report
    that lost the race to the completion report would arrive at an already-terminal row and be
    rejected as a conflict. The write is one local HTTP call; the timeout is short because a slow
    API must not hold up an answer, and a failure is logged rather than raised - the run row is
    telemetry, and telemetry never fails a query.
    """
    try:
        run = AgentRunTelemetry(
            workflow_id=workflow_id,
            organization_id=organization_id,
            request_id=request_id,
            conversation_id=collector.conversation_id,
            customer_id=collector.customer_id,
            status="Running",
            started_at=collector.start_utc,
        )
        await report_agent_run(run.to_api_payload(), timeout=_RUN_START_REPORT_TIMEOUT_SECONDS)
    except Exception:  # noqa: BLE001 - run telemetry must never fail the agent query
        logger.warning(
            "Failed to report the start of run %s (best-effort); the completion report will still "
            "open the row.",
            workflow_id,
            extra={"action": "report_agent_run_started", "workflow_id": workflow_id},
        )


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
    org_id = _resolve_org_id(payload)
    await _publish_result_for_thread(request, org_id, payload.thread_id, result)


async def _publish_result_for_thread(request: Request, org_id: UUID | None, thread_id: str | None, result) -> None:
    """Publish a workflow result into the Salon, best-effort.

    Shared by the query and resume paths so a settlement reaches the thread through exactly the same
    persona-attributed path as a fresh answer (ADR-024).
    """
    event_bus = getattr(request.app.state, "event_bus", None)
    if event_bus is None or org_id is None:
        return

    try:
        await publish_agent_messages(event_bus, org_id, thread_id, result)
    except Exception:  # noqa: BLE001 - publishing must never fail the query
        logger.exception("Failed to publish agent messages for thread %s.", thread_id)


@router.post("/query/stream")
async def agents_query_stream(payload: AgentQueryRequest) -> StreamingResponse:
    """Stream agent workflow events to the client over Server-Sent Events.

    Relays LangGraph ``astream_events`` output. The ``X-Accel-Buffering: no``
    header prevents Nginx/Traefik from buffering the stream and breaking
    real-time token delivery.

    The route does not call :func:`run_concierge`; it builds the graph itself, so it needs its own
    consent guard (plan §8.2, Phase 1 item 1.5). The guard is checked before the graph is built:
    a revoked customer produces a single ``consent_skipped`` frame and no agent runs at all.
    """
    logger.info(
        "Agent query stream started: thread_id=%s",
        payload.thread_id,
        extra={"action": "agent_query_stream", "thread_id": payload.thread_id},
    )
    # G-10: streaming runs are otherwise indistinguishable from request/response runs.
    get_agent_metrics().record_stream_run()

    async def event_source():
        skip_reason = await _stream_consent_skip_reason(payload)
        if skip_reason is not None:
            yield f"data: {json.dumps({'event': 'consent_skipped', 'reason': skip_reason})}\n\n"
            return

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
                "consent_status": None,
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


async def _stream_consent_skip_reason(payload: AgentQueryRequest) -> str | None:
    """The reason the streaming route must not run the graph for this request, else ``None``.

    Mirrors the API-side ingress gate: no bound customer cannot be consent-gated (the message is
    processed), ``revoked`` skips, and a read failure **fails closed** with
    ``consent_check_unavailable`` rather than raising - a streaming request must not 500 because
    the consent store is down.
    """
    org_context = payload.org_context or {}
    customer_id = org_context.get("customer_id")
    if not customer_id:
        return None

    org_id = org_context.get("organization_id") or org_context.get("org_id")
    if not org_id:
        return None

    try:
        consent = await ToolRegistry().get_customer_consent(str(org_id), str(customer_id))
    except Exception:  # noqa: BLE001 - a privacy control fails closed, it never 500s the stream
        logger.warning(
            "Consent check failed for streamed run; refusing to process (fail closed). customer_id=%s",
            customer_id,
            exc_info=True,
        )
        return "consent_check_unavailable"

    status = (consent or {}).get("consentStatus") or "pending"
    return "revoked" if status == "revoked" else None

