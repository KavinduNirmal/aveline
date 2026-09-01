# ADR-009: Internal Service-to-Service Authentication

## Status
Accepted

## Context
Aveline.Api must invoke the Python agent service to run LangGraph workflows. The
agent must never accept client tokens directly — clients can only reach it
through the API, which validates user JWTs and authorizes roles first.

## Options Considered
1. **Shared-secret header (`X-Internal-Token`)** (chosen) — a secret known only to
   the API and the agent, compared with a constant-time hash (`hmac.compare_digest`).
   Pros: trivial to implement, no certificate infrastructure, fits an internal network.
   Cons: secret rotation is a coordinated env change.
2. **OAuth2 client credentials** — Pros: standard, scoped. Cons: requires a token
   endpoint and infrastructure the demo does not have.
3. **Mutual TLS / client certificates** — Pros: strong. Cons: certificate management
   overhead for a small demo; Container Apps setup complexity.
4. **No auth (trusted network)** — Pros: zero work. Cons: any reachable host could
   drive the agent; rejected.

## Decision
A **shared-secret header `X-Internal-Token`**, configured via env
(`AgentService__InternalToken` / `INTERNAL_API_TOKEN`). The API attaches it via
the `InternalServiceAuthHandler` (fails closed if unconfigured); the agent's
`require_internal_token` dependency rejects requests that do not match
(constant-time compare) and returns 500 if the token is unconfigured.

## Consequences
- The agent is unreachable by clients: it trusts only the API.
- The API propagates the authenticated `userId` + roles to the agent in the payload.
- Rotation = updating both `Aveline.Api` and `agnet-service` env config.
- Transport is HTTPS in deployment (Azure Container Apps).
