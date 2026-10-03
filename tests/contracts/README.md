# API ↔ agent-service shared contract

`api-agent-contract.json` in this directory is the **single source of truth** for the
service-to-service hop between `Aveline.Api` (ASP.NET Core) and `agent-service` (FastAPI).
It is not owned by either side: both sides assert against it.

The gap this closes (SE3110 gap **E3**, `docs/final_document/se3110/chapters/09-integration.tex`
§"The Seam Nobody Tests"): before this file existed, each side tested only its own half of the
seam, so a renamed header, path, request field, response field or status literal could pass both
suites while breaking the real call.

## Why a shared file rather than a schema library

Two field sets meeting in the middle, in two languages, cannot be checked by a formatter. A
shared artefact makes the coupling explicit and testable with the dependencies each side already
has:

- `Aveline.Api.Tests/ApiAgentSharedContractTests.cs` reads this file and asserts against the real
  .NET types and the real outbound serialisation.
- `agent-service/tests/test_api_agent_contract.py` reads this file and asserts against the real
  pydantic models, the real router and the real `require_internal_token` dependency.

A rename on either side now fails that side's contract test, because this file still names the old
field. Changing the contract deliberately means updating this file **and** both tests, which is the
point.

## Field reference

| Key | Meaning | Asserted by |
|---|---|---|
| `contractVersion` | Bumped when the wire contract changes in a breaking way. | informational |
| `internalTokenHeader` | The HTTP header carrying the shared secret. `InternalServiceAuthHandler.HeaderName` must equal it; `app.core.security.INTERNAL_TOKEN_HEADER` must equal it. | both |
| `sampleInternalToken` | A non-secret fixture value used only to prove the two sides' token semantics agree. It is deliberately **not** in `agent-service/app/core/config.py`'s `_WEAK_INTERNAL_TOKENS`, so `validate_startup_settings` accepts it. | Python |
| `pendingApprovalStatus` | The `AgentResponse.status` literal that makes the API treat a run as paused. `AgentStatus.pending_approval` must equal it; `AgentQueryResult.IsPaused` must return true for it. | both |
| `paths.query` / `paths.resume` | The agent HTTP paths the API calls. | both |
| `queryRequestFields` | Top-level fields of `POST /agents/query`. The agent's `AgentQueryRequest.model_fields` and the API's outbound body must both equal this exact set. | both |
| `resumeRequestFields` | `AgentResumeRequest.model_fields` for `POST /agents/resume`. | Python |
| `apiSendsResumeFields` | The subset of `resumeRequestFields` the API actually puts on the resume wire. It is a strict subset: the agent model also declares optional `customer_id`, which no API path sends. `extra="forbid"` on the model makes `apiSendsResumeFields ⊆ resumeRequestFields` the compatibility rule. | both |
| `queryResponseFields` | `AgentQueryResponse.model_fields` returned by both agent endpoints. | Python |
| `agentResultFields` | `AgentResponse.model_fields`; `result` inside `queryResponseFields` is this envelope. | Python |
| `apiDeserialisedResponseFields` | The JSON property names on the API's `AgentQueryResult`. A strict subset of `queryResponseFields`: the API deliberately reads only `status` and `result`. | both |
| `apiDeserialisedResultFields` | The JSON property names on `AgentResultEnvelope`, read from inside `result`. A subset of `agentResultFields`. | both |

## Deliberately not pinned

`org_context` is a free-form `dict[str, Any]` on the agent side, and both sides tolerate missing
keys (for example `app/prompts/context.py` defaults `plan_tier`, and the API only sends the keys
the current run needs). Its interior is therefore a loose extension point, not a contract, and is
recorded here only as the single top-level field `org_context`. Pinning its keys would encode an
accident as a requirement.

The agent HTTP timeout bound is also out of scope here: the gap plan defers it until the k6 load
run (gap N1) produces real latency numbers (`09-integration.tex:105-131`).
