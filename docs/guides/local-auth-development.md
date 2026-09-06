# Running Aveline Locally with Authentication

Step-by-step guide to run the full stack (API → agent → Flutter / web) and sign
in with Clerk. Companion to `GET_STARTED.md`, focused on auth.

## 1. Prerequisites

- .NET 10 SDK, Python 3.12, Flutter stable, Bun
- A Clerk account with the Aveline instance (see `docs/ADR/ADR-007-clerk-authentication.md`)
- (optional) `docker compose` for PostgreSQL/Redis/agent

## 2. Get the Clerk keys

From the Clerk Dashboard → *API Keys*, or via the CLI:

```bash
clerk env pull          # writes .env.local (publishable + secret keys)
```

Notes:
- The **publishable key** (`pk_...`) goes into the Flutter/web clients.
- The **issuer** (Frontend API base, `https://<instance>.clerk.accounts.dev`)
  is the API's `Clerk:Authority`.
- The `jwt-aveline-v1` JWT template must exist and mint `user_role` / `org_role`
  (see `docs/ADR/ADR-008-jwt-token-strategy.md`).

## 3. Start the API

Development defaults are already wired in `appsettings.Development.json`
(real Clerk authority, agent base URL, internal token `change-me-internal-token`).

```bash
cd Aveline.Api
cp .env.example .env.local    # optional; dev defaults cover auth
dotnet run
# http://localhost:5091  (OpenAPI at /openapi/v1.json)
```

Override with env vars if needed: `Clerk__Authority`, `AgentService__BaseUrl`,
`AgentService__InternalToken`, `Cors__AllowedOrigins__0`.

## 4. Start the agent service

```bash
cd agnet-service
cp .env.example .env.local
# set INTERNAL_API_TOKEN=change-me-internal-token  (must match the API)
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8000
# health: http://localhost:8000/health
```

## 5. Run the Flutter app (associates)

```bash
cd frontend/aveline_mobile
flutter pub get
flutter run \
  --dart-define=CLERK_PUBLISHABLE_KEY=pk_test_... \
  --dart-define=API_BASE_URL=http://10.0.2.2:5091
```

Sign in → you land on the home screen. Sign-out returns you to the auth screen.

## 6. Run the web dashboard (owners / managers)

```bash
cd frontend/web
bun install
cp .env.example .env.local     # fill in VITE_CLERK_PUBLISHABLE_KEY
bun dev
# http://localhost:5173
```

Sign in → `/` (dashboard). Non-admin accounts are redirected to `/forbidden`.

## 7. Verify the auth flow

- **Claims**: `GET http://localhost:5091/api/v1/auth/claims` with a token from
  the app (Bearer header) returns `userId`, `email`, `roles`, raw claims.
- **Policy demo**: `/api/v1/policies/associate|manager|owner` and
  `/api/v1/policies/approvals/approve` return 200 only for the right roles.
- **Service-to-service**: `/api/v1/agents/ping` proxies to the agent with the
  internal token and echoes `userId` + roles.

To capture a token from the browser: DevTools → *Application* → Clerk storage,
or use a test user's session token from the Clerk Dashboard.

## 8. Running everything via docker-compose (optional)

```bash
docker compose up -d   # postgres, redis, api, agent
```

The API and agent advertise `http://localhost:5091` / `http://localhost:8000`
on the host (`docker-compose.yml`).

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `Clerk:Authority is not configured` | Ran outside Development profile | Set `Clerk__Authority` env var |
| Agent returns 401 for `/agents/ping` | Token mismatch | Align `AgentService__InternalToken` and `INTERNAL_API_TOKEN` |
| Web/Fluent sign-in redirect loop | Wrong publishable key / origin | Check `CLERK_PUBLISHABLE_KEY` and the Clerk instance's allowed origins (must include `http://localhost:5173`) |
| 401 on API calls after sign-in | Token without template claims | Ensure clients request the `jwt-aveline-v1` template token |
