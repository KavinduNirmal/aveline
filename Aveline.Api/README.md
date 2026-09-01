# Aveline.Api — ASP.NET Core 10 Web API

Modular monolith (see `docs/ADR/ADR-001`) that validates Clerk JWTs, enforces
role/permission policies, and proxies authorized calls to the internal Python
agent service. This is the only backend that clients (Flutter, React) talk to.

## Prerequisites

- .NET 10 SDK

## Setup

```bash
cp .env.example .env.local   # fill in Clerk + agent values
dotnet restore
dotnet run
```

The API binds `http://localhost:5091` in development (see
`Properties/launchSettings.json`).

## Environment variables

| Config key (env) | Required | Purpose |
|---|---|---|
| `Clerk:Authority` (`Clerk__Authority`) | yes | Clerk Frontend API base, e.g. `https://<instance>.clerk.accounts.dev`. JwtBearer issuer + JWKS discovery. |
| `Clerk:RequireHttpsMetadata` | no (default `true`) | Set `false` only for local dev/tests against an HTTP authority. |
| `Cors:AllowedOrigins` | yes | Allow-list for browser clients (e.g. `http://localhost:5173`). |
| `AgentService:BaseUrl` (`AgentService__BaseUrl`) | yes | Base URL of the agent service (docker-compose: `http://agent:8000`). |
| `AgentService:InternalToken` (`AgentService__InternalToken`) | yes | Shared secret sent as `X-Internal-Token`; must match the agent's `INTERNAL_API_TOKEN`. |
| `Logging:UseJsonConsole` | no (default `false`) | Emit JSON structured logs (ELK / App Insights friendly). |

## Authentication & authorization

- **Token validation** (`Configurations/AuthenticationConfiguration.cs`):
  JwtBearer validates issuer, lifetime, and signing key against the Clerk JWKS.
  `user_role` / `org_role` claims from the `jwt-aveline-v1` template are promoted
  to `ClaimTypes.Role` at authentication time.
- **Policies** (`Configurations/AuthorizationConfiguration.cs`): `Associates`,
  `Managers`, `Owners`, plus one permission policy per catalog entry
  (`Permissions.cs`).
- **Internal auth** (`Infrastructure/Integrations/InternalServiceAuthHandler.cs`):
  outbound calls to the agent carry `X-Internal-Token` (fails closed if unset).
- **Logging**: auth success/failure events and 401/403 audits are structured
  (`Configurations/LoggingConfiguration.cs`).
- **Security headers**: `X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy` on every response.

## Endpoints (v1)

| Route | Auth | Description |
|---|---|---|
| `GET /auth/claims` | any signed-in | Current user id, email, roles, and raw claims |
| `GET /policies/*` | role policies | Demo of Associates / Managers / Owners + permission policies |
| `POST /agents/ping` | Associates | Proxies a ping to the agent with the internal token |

OpenAPI is served at `/openapi/v1.json` in development.

## Tests

```bash
dotnet test Aveline.Api/Aveline.Api.sln -c Release
```

Unit + integration tests cover JWT validation rules, role normalization,
policies, CORS, internal-token handling, and the full auth flow
(`Aveline.Api.Tests/`). See `docs/tests/README.md`.

## Further reading

- Auth architecture: `docs/architecture/authentication.md`
- JWT strategy: `docs/ADR/ADR-008-jwt-token-strategy.md`
- Internal service auth: `docs/ADR/ADR-009-internal-service-authentication.md`
