"""Agent telemetry models and collector for run/step observability (FR-5.1–FR-5.12).

Captures run-level and step-level metrics conforming to ASP.NET Core
AgentRunReportRequest and AgentStepReportRequest contracts without leaking
prompt text, tool arguments, or raw output (FR-5.9).
"""

from __future__ import annotations

import hashlib
import time
from datetime import UTC, datetime
from typing import Any
from pydantic import BaseModel, ConfigDict, Field


def _utc_now_iso() -> str:
    return datetime.now(UTC).isoformat()


def hash_args(args: Any) -> str | None:
    """Compute deterministic SHA-256 hash of tool arguments."""
    if args is None:
        return None
    try:
        data_str = str(args).encode("utf-8")
        return hashlib.sha256(data_str).hexdigest()[:32]
    except Exception:
        return None


def map_agent_key(node_name: str | None) -> str:
    """Map a LangGraph node or specialist name to a registered agent key (BR-5.5)."""
    if not node_name:
        return "orchestrator"
    node_lower = node_name.lower()
    if "memory" in node_lower:
        return "customer_memory"
    if "visual" in node_lower:
        return "visual_insight"
    if "commerce" in node_lower:
        return "commerce"
    return "orchestrator"


class AgentStepTelemetry(BaseModel):
    """Execution telemetry for a single step/node/tool within an agent run."""

    model_config = ConfigDict(populate_by_name=True)

    step_index: int = Field(..., serialization_alias="stepIndex")
    agent_key: str = Field(..., serialization_alias="agentKey")
    node_name: str = Field(..., serialization_alias="nodeName")
    step_kind: str = Field(default="NodeTransition", serialization_alias="stepKind")
    tool_name: str | None = Field(default=None, serialization_alias="toolName")
    status: str = Field(default="Succeeded")
    attempt_number: int = Field(default=1, serialization_alias="attemptNumber")
    started_at: str = Field(default_factory=_utc_now_iso, serialization_alias="startedAt")
    completed_at: str | None = Field(default=None, serialization_alias="completedAt")
    duration_ms: int | None = Field(default=None, serialization_alias="durationMs")
    provider: str | None = None
    model: str | None = None
    input_tokens: int = Field(default=0, serialization_alias="inputTokens")
    output_tokens: int = Field(default=0, serialization_alias="outputTokens")
    cached_tokens: int = Field(default=0, serialization_alias="cachedTokens")
    actual_cost_usd: float = Field(default=0.0, serialization_alias="actualCostUsd")
    args_hash: str | None = Field(default=None, serialization_alias="argsHash")
    result_bytes: int | None = Field(default=None, serialization_alias="resultBytes")
    error_code: str | None = Field(default=None, serialization_alias="errorCode")


class AgentRunTelemetry(BaseModel):
    """Complete workflow run report payload matching AgentRunReportRequest."""

    model_config = ConfigDict(populate_by_name=True)

    workflow_id: str = Field(..., serialization_alias="workflowId")
    organization_id: str | None = Field(default=None, serialization_alias="organizationId")
    parent_workflow_run_id: str | None = Field(default=None, serialization_alias="parentWorkflowRunId")
    request_id: str | None = Field(default=None, serialization_alias="requestId")
    trace_id: str | None = Field(default=None, serialization_alias="traceId")
    trigger_kind: str = Field(default="ApiRequest", serialization_alias="triggerKind")
    trigger_ref: str | None = Field(default=None, serialization_alias="triggerRef")
    conversation_id: str | None = Field(default=None, serialization_alias="conversationId")
    customer_id: str | None = Field(default=None, serialization_alias="customerId")
    initiated_by_user_id: str | None = Field(default=None, serialization_alias="initiatedByUserId")
    status: str = Field(default="Succeeded")
    agents_involved: list[str] = Field(default_factory=list, serialization_alias="agentsInvolved")
    started_at: str = Field(default_factory=_utc_now_iso, serialization_alias="startedAt")
    completed_at: str | None = Field(default=None, serialization_alias="completedAt")
    duration_ms: int | None = Field(default=None, serialization_alias="durationMs")
    paused_at: str | None = Field(default=None, serialization_alias="pausedAt")
    resumed_at: str | None = Field(default=None, serialization_alias="resumedAt")
    approval_wait_ms: int | None = Field(default=None, serialization_alias="approvalWaitMs")
    tool_call_count: int = Field(default=0, serialization_alias="toolCallCount")
    retry_count: int = Field(default=0, serialization_alias="retryCount")
    input_tokens: int = Field(default=0, serialization_alias="inputTokens")
    output_tokens: int = Field(default=0, serialization_alias="outputTokens")
    cached_tokens: int = Field(default=0, serialization_alias="cachedTokens")
    actual_cost_usd: float = Field(default=0.0, serialization_alias="actualCostUsd")
    blossom_units: float = Field(default=0.0, serialization_alias="blossomUnits")
    pricing_rule_id: str | None = Field(default=None, serialization_alias="pricingRuleId")
    plan_tier_at_run: str | None = Field(default=None, serialization_alias="planTierAtRun")
    error_code: str | None = Field(default=None, serialization_alias="errorCode")
    ai_usage_record_id: str | None = Field(default=None, serialization_alias="aiUsageRecordId")
    steps: list[AgentStepTelemetry] = Field(default_factory=list)

    def to_api_payload(self) -> dict[str, Any]:
        """Serialize into camelCase payload for .NET backend API."""
        return self.model_dump(by_alias=True, exclude_none=False)


class TelemetryCollector:
    """High-resolution timing, step, and usage collector for an active workflow."""

    def __init__(self, workflow_id: str, organization_id: str | None = None, request_id: str | None = None):
        self.workflow_id = workflow_id
        self.organization_id = organization_id
        self.request_id = request_id
        self.start_perf = time.perf_counter()
        self.start_utc = _utc_now_iso()
        self.steps: list[AgentStepTelemetry] = []
        self._agents: set[str] = set()
        self._current_step_perf: float | None = None
        self._current_step_index: int = 0
        self.tool_call_count = 0
        self.retry_count = 0

    def start_step(
        self,
        node_name: str,
        step_kind: str = "NodeTransition",
        tool_name: str | None = None,
        attempt: int = 1,
    ) -> AgentStepTelemetry:
        agent_key = map_agent_key(node_name)
        self._agents.add(agent_key)
        self._current_step_perf = time.perf_counter()
        step = AgentStepTelemetry(
            step_index=self._current_step_index,
            agent_key=agent_key,
            node_name=node_name,
            step_kind=step_kind,
            tool_name=tool_name,
            status="Running",
            attempt_number=attempt,
            started_at=_utc_now_iso(),
        )
        self.steps.append(step)
        self._current_step_index += 1
        if step_kind == "ToolCall":
            self.tool_call_count += 1
        if attempt > 1:
            self.retry_count += 1
        return step

    def complete_current_step(
        self,
        status: str = "Succeeded",
        input_tokens: int = 0,
        output_tokens: int = 0,
        cached_tokens: int = 0,
        actual_cost_usd: float = 0.0,
        provider: str | None = None,
        model: str | None = None,
        args_hash: str | None = None,
        result_bytes: int | None = None,
        error_code: str | None = None,
    ) -> None:
        if not self.steps:
            return
        step = self.steps[-1]
        elapsed_ms = 0
        if self._current_step_perf is not None:
            elapsed_ms = int((time.perf_counter() - self._current_step_perf) * 1000)
        step.completed_at = _utc_now_iso()
        step.duration_ms = max(0, elapsed_ms)
        step.status = status
        step.input_tokens = input_tokens
        step.output_tokens = output_tokens
        step.cached_tokens = cached_tokens
        step.actual_cost_usd = actual_cost_usd
        step.provider = provider
        step.model = model
        step.args_hash = args_hash
        step.result_bytes = result_bytes
        step.error_code = error_code

    def finalize(
        self,
        status: str = "Succeeded",
        input_tokens: int = 0,
        output_tokens: int = 0,
        cached_tokens: int = 0,
        actual_cost_usd: float = 0.0,
        error_code: str | None = None,
    ) -> AgentRunTelemetry:
        elapsed_ms = int((time.perf_counter() - self.start_perf) * 1000)
        completed_utc = _utc_now_iso()
        return AgentRunTelemetry(
            workflow_id=self.workflow_id,
            organization_id=self.organization_id,
            request_id=self.request_id,
            status=status,
            agents_involved=sorted(self._agents) if self._agents else ["orchestrator"],
            started_at=self.start_utc,
            completed_at=completed_utc,
            duration_ms=max(0, elapsed_ms),
            tool_call_count=self.tool_call_count,
            retry_count=self.retry_count,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            cached_tokens=cached_tokens,
            actual_cost_usd=actual_cost_usd,
            error_code=error_code,
            steps=self.steps,
        )
