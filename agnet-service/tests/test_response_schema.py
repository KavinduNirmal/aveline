"""Tests for the shared agent output envelope (app/schemas/response.py).

The envelope is the stable contract between the agent service and ASP.NET Core.
Every agent output must conform to it, so it is validated strictly.
"""

import pytest
from pydantic import ValidationError

from app.schemas.response import AgentMetadata, AgentOutput, AgentResponse, AgentStatus


def test_status_enum_values():
    assert {s.value for s in AgentStatus} == {
        "success",
        "pending_approval",
        "out_of_scope",
        "error",
    }


def test_metadata_defaults():
    metadata = AgentMetadata()
    assert metadata.duration_ms is None
    assert metadata.model is None
    assert metadata.tokens_used is None
    assert metadata.blossoms_consumed is None


def test_metadata_full():
    metadata = AgentMetadata(
        duration_ms=1234,
        model="deepseek-v4-flash",
        tokens_used=800,
        blossoms_consumed=1,
    )
    assert metadata.duration_ms == 1234
    assert metadata.model == "deepseek-v4-flash"
    assert metadata.tokens_used == 800
    assert metadata.blossoms_consumed == 1


def test_agent_output_success():
    output = AgentOutput(status="success", output={"draft": "Hello"})
    assert output.status == AgentStatus.success
    assert output.output == {"draft": "Hello"}


def test_agent_output_accepts_enum():
    output = AgentOutput(status=AgentStatus.pending_approval, output={})
    assert output.status == AgentStatus.pending_approval


def test_agent_output_rejects_invalid_status():
    with pytest.raises(ValidationError):
        AgentOutput(status="not_a_status", output={})


def test_agent_output_requires_output():
    with pytest.raises(ValidationError):
        AgentOutput(status="success")  # type: ignore[call-arg]


def test_agent_response_round_trip():
    response = AgentResponse(
        status="success",
        output={"draft": "Hello"},
        metadata=AgentMetadata(duration_ms=10, model="m", tokens_used=5, blossoms_consumed=1),
    )
    data = response.model_dump()
    assert data["status"] == "success"
    assert data["metadata"]["duration_ms"] == 10
    assert data["metadata"]["blossoms_consumed"] == 1


def test_agent_response_metadata_optional():
    response = AgentResponse(status="error", output={})
    assert response.metadata is None


def test_agent_response_rejects_extra_fields():
    with pytest.raises(ValidationError):
        AgentResponse(status="success", output={}, unexpected="x")  # type: ignore[call-arg]
