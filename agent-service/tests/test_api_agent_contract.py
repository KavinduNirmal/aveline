"""SE3110 gap E3 - the agent-service half of the shared API<->agent contract.

The gap (``09-integration.tex:105-131``, "The Seam Nobody Tests"): the .NET API and this service
each tested only their own half of the hop, so a renamed header, path, request field, response
field or pause-status literal would pass both suites and break the real call.

Every assertion below is made against the single shared artefact,
``tests/contracts/api-agent-contract.json`` (see ``tests/contracts/README.md``). The field sets are
read from that file rather than hard-coded, so renaming a field on this side fails this test, and
changing the contract deliberately means changing the file and the .NET test together.
"""

import json
from pathlib import Path

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient

from app.api.agents import router as agents_router
from app.core.config import (
    _WEAK_INTERNAL_TOKENS,
    Settings,
    get_settings,
    validate_startup_settings,
)
from app.core.security import INTERNAL_TOKEN_HEADER, require_internal_token
from app.main import app
from app.schemas.query import AgentQueryRequest, AgentQueryResponse, AgentResumeRequest
from app.schemas.response import AgentResponse, AgentStatus

_CONTRACT_RELATIVE_PATH = Path("tests") / "contracts" / "api-agent-contract.json"


def _contract_path() -> Path:
    """Locate the shared contract by walking up from this file (no absolute path baked in)."""
    for parent in Path(__file__).resolve().parents:
        candidate = parent / _CONTRACT_RELATIVE_PATH
        if candidate.is_file():
            return candidate
    raise FileNotFoundError(
        f"Could not find {_CONTRACT_RELATIVE_PATH} above {Path(__file__).resolve()}"
    )


@pytest.fixture(scope="module")
def contract() -> dict:
    return json.loads(_contract_path().read_text(encoding="utf-8"))


def _declared_fields(contract: dict, key: str) -> set[str]:
    """The declared field set, with a guard that the file stays sorted and duplicate-free."""
    fields = contract[key]
    assert isinstance(fields, list) and fields, f"{key} must be a non-empty list"
    assert fields == sorted(fields), f"{key} must be sorted and free of duplicates"
    return set(fields)


def _model_fields(model) -> set[str]:
    return set(model.model_fields)


# =============================================================================================
# The token header - the name both sides must use
# =============================================================================================


def test_internal_token_header_matches_the_shared_contract(contract):
    assert INTERNAL_TOKEN_HEADER == contract["internalTokenHeader"]


def test_every_contract_path_binds_the_contract_header_on_the_route(contract):
    """The effective wire header, not just the module constant.

    ``require_internal_token`` binds the header through FastAPI's parameter-name conversion
    (``x_internal_token`` -> ``x-internal-token``), so ``INTERNAL_TOKEN_HEADER`` alone is not proof
    that the route reads the header the contract names. The OpenAPI operation parameters are the
    authoritative record of what the route actually accepts; header names are case-insensitive, so
    the comparison is too.
    """
    spec = app.openapi()
    expected = contract["internalTokenHeader"].lower()

    for path in contract["paths"].values():
        operation = spec["paths"][path]["post"]
        bound = {
            parameter["name"].lower()
            for parameter in operation.get("parameters", [])
            if parameter["in"] == "header"
        }
        assert expected in bound, f"{path} does not bind the contract header {expected!r}"


def test_the_router_exposes_the_contract_paths(contract):
    router_paths = {route.path for route in agents_router.routes}

    for path in contract["paths"].values():
        assert path in router_paths


# =============================================================================================
# The request models - what the agent accepts
# =============================================================================================


def test_query_request_model_has_exactly_the_contract_fields(contract):
    assert _model_fields(AgentQueryRequest) == _declared_fields(contract, "queryRequestFields")


def test_resume_request_model_has_exactly_the_contract_fields(contract):
    assert _model_fields(AgentResumeRequest) == _declared_fields(contract, "resumeRequestFields")


def test_the_api_sends_only_fields_the_resume_model_accepts(contract):
    # AgentResumeRequest sets ``extra="forbid"``, so a field the API sends that the model does not
    # declare is a 422 on the wire. The contract records the API's actual subset separately because
    # it never sends the model's optional ``customer_id``.
    assert _declared_fields(contract, "apiSendsResumeFields") <= _declared_fields(
        contract, "resumeRequestFields"
    )


# =============================================================================================
# The response models - what the agent emits
# =============================================================================================


def test_query_response_models_match_the_contract(contract):
    assert _model_fields(AgentQueryResponse) == _declared_fields(contract, "queryResponseFields")
    assert _model_fields(AgentResponse) == _declared_fields(contract, "agentResultFields")

    # The API reads a strict subset of each envelope; a contract edit that has it read a field the
    # agent never emits must not pass here.
    assert _declared_fields(contract, "apiDeserialisedResponseFields") <= _declared_fields(
        contract, "queryResponseFields"
    )
    assert _declared_fields(contract, "apiDeserialisedResultFields") <= _declared_fields(
        contract, "agentResultFields"
    )


def test_pause_status_literal_matches_the_shared_contract(contract):
    assert AgentStatus.pending_approval.value == contract["pendingApprovalStatus"]


# =============================================================================================
# The token value - the two sides' semantics actually agree
# =============================================================================================


def test_the_contract_sample_token_survives_the_startup_validator(contract):
    token = contract["sampleInternalToken"]

    # The fixture value must be one the service would actually boot with: a weak/placeholder value
    # is refused by the validator, which would make the end-to-end assertion below meaningless.
    assert token not in _WEAK_INTERNAL_TOKENS
    validate_startup_settings(Settings(internal_api_token=token))


@pytest.fixture
def contract_token(contract, monkeypatch):
    """Configure the service with the contract's sample token, the way the API would send it."""
    token = contract["sampleInternalToken"]
    monkeypatch.setenv("INTERNAL_API_TOKEN", token)
    get_settings.cache_clear()
    yield token
    get_settings.cache_clear()


@pytest.mark.asyncio
async def test_the_contract_token_is_accepted_and_a_different_token_is_rejected(
    contract_token,
):
    # hmac.compare_digest semantics: the exact shared secret passes, and a different value of a
    # different length fails (a timing-safe comparison, not a signature scheme).
    await require_internal_token(x_internal_token=contract_token)

    with pytest.raises(HTTPException) as exc:
        await require_internal_token(x_internal_token=contract_token + "-different")

    assert exc.value.status_code == 401


def test_the_contract_token_authenticates_against_the_real_route(contract, contract_token):
    """End to end through the ASGI app: the header the API sends, with the token the API sends."""
    header_name = contract["internalTokenHeader"]
    client = TestClient(app)

    accepted = client.post(
        "/agents/ping", headers={header_name: contract_token}, json={"userId": "contract-user"}
    )
    assert accepted.status_code == 200

    rejected = client.post(
        "/agents/ping",
        headers={header_name: contract_token + "-different"},
        json={"userId": "contract-user"},
    )
    assert rejected.status_code == 401
