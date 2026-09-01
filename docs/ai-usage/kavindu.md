# AI Usage Log — Kavindu

## Session 2026-08-25

**Task:** Initial Project Scaffolding, Directory Structuring, .gitignore Configuration, and Husky Setup
**Tool used:** Antigravity AI Assistant

### Summary of Activities

- Understood the project scope using `.agents/brain/PROJECT_CONTEXT.md` covering the three vertical slices (Customer Concierge & Memory, Visual Intelligence & Sourcing, Commerce Validation & Optimization).
- Set up industry-standard modular layer architectures for:
  - **ASP.NET Core API (`Aveline.Api/`)**: Vertical slice architecture with `Modules/CustomerConcierge`, `Modules/VisualIntelligence`, `Modules/Commerce`, plus `Common/` and `Infrastructure/` layers, with detailed `README.md` documentation for every folder.
  - **Python Agent Service (`agnet-service/`)**: Structured FastAPI + LangGraph service layers (`app/api`, `app/agents`, `app/tools`, `app/workflows`, `app/schemas`, `app/services`, `app/db`, `app/core`, `tests/`) and stubbed `main.py` with health endpoint and core `requirements.txt`.
  - **Flutter Mobile App (`frontend/aveline_mobile/lib/`)**: Reorganized into clean architecture feature modules (`customers`, `inventory`, `commerce` each with `data/`, `domain/`, `presentation/`), alongside `core/` and `shared/` directories.
  - **Web Frontend (`frontend/web/`)**: Left empty pending frontend technology stack confirmation, with placeholder README.
  - **Documentation (`docs/`)**: Added documentation structure for `ADR/`, `OpenApi/`, `reports/`, and `ai-usage/`.
  - **Docker & CI/CD**: Added multi-service local development orchestration in `docker-compose.yml` (PostgreSQL with pgvector, Redis, API, Agent Service) and GitHub Actions CI workflow in `.github/workflows/ci.yml`.
  - **Git & Package Manager Hygiene**: Completely revamped `.gitignore` to explicitly ignore non-bun lockfiles (`package-lock.json`, `npm-shrinkwrap.json`, `yarn.lock`, `pnpm-lock.yaml`, `pnpm-lock.yml`) while ensuring only `bun.lock` / `bun.lockb` is tracked. Added Husky pre-commit hook with 6 automated quality gates.
  - **Developer Onboarding (`GET_STARTED.md`)**: Rewrote comprehensive onboarding guide covering prerequisites, setup steps, agent skills integration (`bunx skills update`), local service execution, conventional commits & branching flow targeting `development`, and quality gates.
  - **Git Flow & Workflow Setup**: Created local `development` integration branch, updated `.github/workflows/ci.yml` to trigger across Git Flow branches with concurrency control, created formal CI specification at `spec/spec-process-cicd-ci.md` adhering to the `create-github-action-workflow-specification` skill, added PR template (`.github/pull_request_template.md`), GitHub issue templates for feature requests and bug reports, and documented Git Flow standards in `docs/git-flow.md`.

## Session 2026-08-30

**Task:** Project Status & Agentic Setup Review, then Developer-Experience Streamlining (Docker, CI hardening, version-control hygiene, docs alignment)
**Tool used:** opencode (Claude) AI coding agent

### Summary of Activities

- Reviewed repository status, agentic setup, and produced a detailed report covering project scaffolding state, `.agents/` rules/brain/skills, `.claude/` symlinks, Husky gates, CI workflow, and documentation status.
- **Docker / Infrastructure**: Implemented `Aveline.Api/Dockerfile` (multi-stage .NET 10) and `agnet-service/Dockerfile` (Python 3.12-slim), and added `.dockerignore` for both projects.
- **Environment templates**: Populated root `.env.example`, `Aveline.Api/.env.example`, and `agnet-service/.env.example` with required keys plus commented, described placeholders for Clerk, WhatsApp, payment gateway, courier, OpenAI, and LangSmith.
- **Version-control hygiene**: Removed `pubspec.lock` from `.gitignore` so the Flutter app lockfile is tracked for reproducible builds.
- **CI/CD hardening**: SHA-pinned all third-party GitHub Actions (`actions/checkout`, `actions/setup-dotnet`, `actions/setup-python`, `subosito/flutter-action`), removed `continue-on-error` from the Python lint and Flutter analyze jobs, added a `dotnet test` step, and added `agnet-service/pyproject.toml` with ruff configuration.
- **Documentation**: Aligned Flutter/Dart version references and documented the API port mapping (local `5091` / host `5000`) in `README.md` and `GET_STARTED.md`.

### Verification Performed

- `docker compose config` validated (with `POSTGRES_PASSWORD` set).
- `dotnet build` and `dotnet test` (0 tests) exit 0.
- `ruff check agnet-service/app/` passes.
- `flutter analyze --no-fatal-infos` reports no issues.
- `git diff` reviewed; no secrets introduced.

### Notes / Remaining Work

- Flagged `NU1903` high-severity vulnerability warning in `Microsoft.OpenApi 2.0.0` (pre-existing) — recommend bumping the package.
- No changes committed; `pubspec.lock` is now trackable but not yet added to version control.

### Follow-up (same session): Azure deployment research

- Researched an Azure deployment strategy for the demo phase (< $100 budget) targeting Azure for Students, Azure Container Apps, PostgreSQL Flexible Server (B1ms + pgvector), Static Web Apps, Key Vault, and Application Insights, with GitHub Actions + OIDC + Bicep for CI/CD.
- Wrote `docs/ADR/ADR-006-deployment-platform.md` (required ADR, follows the repo template).
- Wrote `docs/deployment.md` — phased implementation plan (to be implemented later).
- Sources verified from official Azure pages (Azure for Students, Container Apps pricing).
- No changes committed.

## Session 2026-09-01

**Task:** Implement Clerk Authentication roadmap — Issue #6 first: "Set up Clerk Application and Configure JWT Template"
**Tool used:** opencode (Claude) AI coding agent + Clerk CLI (v1.5.0)

### Intended Work (session start)

- Execute the prioritized Clerk auth roadmap (issues #6–#29) one issue at a time, pausing after each for user review.
- Approved decisions: `.NET` JWT via JwtBearer + JWKS; insert "scaffold Vite dashboard" as a prerequisite before the React issues (#11/#12/#13); first issue = #6.
- JWT template `jwt-aveline-v1` claims: `user_role` = `{{user.public_metadata.role}}` (Aveline team role), `org_role` = `{{org.role}}` (per-store owner/staff role), plus `org_id`/`org_slug`.

### Work Performed (Issue #6)

- Confirmed Clerk CLI logged in as `kavindunirmald@gmail.com`; linked the repo to the **Aveline** app `app_3IixDnrbjebOuMCicC8FSsiq7Zw` (`clerk link --app ...`).
- Verified the JWT template `jwt-aveline-v1` exists via Clerk Backend API (id `jtmp_3Ij0A97NwM6f8pIjmsqeXd9zrgy`) with the four claims.
- Pulled dev keys with `clerk env pull` into gitignored `.env.local` (`CLERK_PUBLISHABLE_KEY`, `CLERK_SECRET_KEY`); values not printed/exposed.
- Configured the Vite dev allowed origin `http://localhost:5173` on the Clerk instance via `PATCH /instance`; verified.
- Ran `clerk doctor` — clean for dev instance (production instance not configured yet; CLI update 1.5.0 → 3.2.0 available).
- Updated `Aveline.Api/.env.example` with Clerk variable names + issuer/JWKS URLs + JWT template reference + allowed origin (no secret values).

### Verification Performed

- `clerk whoami` / `clerk apps list` → linked to correct app.
- `clerk api jwt_templates` → template claims confirmed.
- `clerk api instance` → `allowed_origins: ["http://localhost:5173"]`.
- `git check-ignore .env.local` → ignored (no secrets tracked).
- `git status` → no secret files staged.

### Remaining Work / Notes

- Issue #6 was closed by the user (commit `4bc371a feat: Initialized Clerk Closes #6`); Clerk CLI updated to 3.2.0.
- Production origin + production instance are deployment-phase items.

### Follow-up (same session): Issue #7 — Auth flow design

- Created `docs/architecture/authentication.md` with C4 context + container diagrams, sequence diagrams for login and an authenticated API call (including internal agent call), and the role model (`user_role` vs `org_role`).
- Created `docs/ADR/ADR-007-clerk-authentication.md` (template-compliant): Clerk as identity provider; `JwtBearer` + Clerk JWKS for validation; service-to-service shared-secret decision deferred to #18.
- Updated `docs/ADR/README.md` to list ADR-007.

### Follow-up (same session): Issue #14 — .NET backend JWT auth

- Added `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.11) to `Aveline.Api.csproj`.
- Rewrote `Program.cs`: `AddAuthentication().AddJwtBearer()` against the Clerk **Authority** (OIDC discovery → JWKS), with issuer/lifetime/signing-key validation. Audience validation disabled because Clerk `jwt-aveline-v1` template tokens carry no `aud` claim (instance signing key already scopes tokens). Claims: `NameClaimType = NameIdentifier` (sub), and `OnTokenValidated` exposes `user_role` + `org_role` as `ClaimTypes.Role` so `[Authorize(Roles=...)]` works for both. Added protected `GET /api/auth/claims` verification endpoint.
- Added `Clerk:Authority` to `appsettings.Development.json` (dev Frontend API URL; overridable via `Clerk__Authority`).
- Documented `Clerk__Authority` in `Aveline.Api/.env.example`; added a `.http` request for the claims endpoint.
- **Refactor (user request):** moved JwtBearer + authorization setup out of `Program.cs` into `Aveline.Api/Configurations/AuthenticationConfiguration.cs` (`AddAvelineAuthentication(this IServiceCollection, IConfiguration)`). `Program.cs` is now concise (endpoints only). Re-verified: no token → 401, valid fresh token → 200 with `roles: ["associate","org:admin"]`.

### Verification (Issue #14)

- Ran the API locally; `GET /api/auth/claims`:
  - no token → **401**
  - garbage token → **401**
  - valid token (minted from `jwt-aveline-v1` via Clerk Backend API) → **200**, `userId: user_3Ij3bVkA6VdraN15mEnh5782F48`, `roles: ["associate"]`
- Created a test org + `org:admin` membership; fresh token carried `org_id`/`org_role: org:admin`/`org_slug`; API returned `roles: ["associate","org:admin"]`.
- `dotnet build` 0 errors (only pre-existing NU1903 warning).
- Test data left in dev instance: user `aveline_test1` (role `associate`), org `Aveline Boutique Colombo` — reused by later auth issues (#15/#16/#22).
- No changes committed; awaiting user review before closing #14 and starting #15.

### Follow-up (same session): Issue #15 — Authorization policies in .NET

- Created `Aveline.Api/Configurations/AuthorizationConfiguration.cs`: role-based policies (`Associates`, `Managers`, `Owners`) and permission-based policies backed by a permission→roles matrix (`PermissionRequirement` + `PermissionAuthorizationHandler`). Added `AddAvelineAuthorization(this IServiceCollection)`.
- Removed `AddAuthorization()` from `AuthenticationConfiguration.cs` (now lives in the new file).
- Created `Aveline.Api/Endpoints/AuthPolicyDemoEndpoints.cs` (`/api/policies/*`) to demonstrate the policies; registered in `Program.cs`.

### Verification (Issue #15)

- Ran the API with the `associate` + `org:admin` test token:
  - `/api/policies/associate` → **200**, `/manager` → **200**, `/owner` → **403**
  - `/api/policies/approvals/approve` (permission) → **200**, `/api/policies/payments/refund` (permission) → **403**
  - no token / garbage token → **401**
- Fixed a handler bug during verification: the permission handler was looking up roles in a permission→roles map (wrong direction); corrected to resolve allowed roles from the permission then intersect with the caller's roles.
- No changes committed; awaiting user review before closing #15 and starting #16.

### Follow-up (same session): versioning, CORS (#17), and authorization refactor

- **API versioning:** moved all API endpoints under `/api/v1` using route groups (`var v1 = app.MapGroup("/api/v1")`). Verified OpenAPI exposes only `/api/v1/*` paths; the old `/api/auth/claims` path returns 404. (URL-prefix versioning; can be upgraded to `Asp.Versioning.Http` negotiation later if needed.)
- **CORS (Issue #17):** created `Aveline.Api/Configurations/CorsConfiguration.cs` with `AddAvelineCors()` — named policy `aveline-cors`, origins from `Cors:AllowedOrigins` (added `http://localhost:5173` to `appsettings.json`), `AllowAnyHeader`/`AllowAnyMethod`. Applied via `app.UseCors(...)`. Native Flutter clients send no Origin and are unaffected.
- **Authorization refactor (user request):** split `AuthorizationConfiguration.cs` into single-responsibility files:
  - `Authorization/Roles.cs` — role constants (model)
  - `Authorization/Permissions.cs` — permission names + permission→roles catalog (model)
  - `Authorization/PermissionRequirement.cs` — requirement (model)
  - `Authorization/PermissionAuthorizationHandler.cs` — handler (implementation)
  - `Configurations/AuthorizationConfiguration.cs` — registration only (policies + handler DI)
- Moved `GET /api/auth/claims` out of `Program.cs` into `Endpoints/AuthEndpoints.cs`; updated `AuthPolicyDemoEndpoints.cs` to the `/api/v1/policies` group and to use `Permissions.*` constants. `Program.cs` is now ~50 lines.

### Verification (versioning + CORS + refactor)

- `dotnet build` 0 errors.
- `/api/v1/auth/claims`: no token → 401, valid token → 200; old `/api/auth/claims` → 404.
- `/api/v1/policies/*`: associate 200, manager 200, owner 403, approvals/approve 200, payments/refund 403.
- CORS preflight: Origin `http://localhost:5173` → 204 + `Access-Control-Allow-Origin`; Origin `http://evil.com` → 204 with **no** allow-origin header.
- OpenAPI lists all paths under `/api/v1`.
- No changes committed; #16 skipped (needs richer response), #17 pending user review to close.

### Follow-up (same session): Issues #18/#19 — service-to-service auth & secure Python agent

- **Backend (#18):** created the internal-auth HTTP client stack:
  - `Infrastructure/Integrations/InternalServiceAuthHandler.cs` — `DelegatingHandler` adding `X-Internal-Token` from `AgentService:InternalToken`.
  - `Infrastructure/Integrations/AgentServiceClient.cs` — typed `HttpClient` (`IAgentServiceClient`).
  - `Configurations/ServiceClientsConfiguration.cs` — `AddAgentServiceClient()` (base URL from `AgentService:BaseUrl`).
  - `Endpoints/AgentEndpoints.cs` — `POST /api/v1/agents/ping` (requires staff auth) that forwards the caller's `userId`/`roles` to the agent via the internal token.
  - Config: `AgentService:BaseUrl`/`InternalToken` in `appsettings.Development.json` + documented in `Aveline.Api/.env.example`.
- **Python agent (#19):** secured `agnet-service`:
  - `app/core/config.py` — pydantic-settings `Settings` (`INTERNAL_API_TOKEN`).
  - `app/core/security.py` — `require_internal_token` FastAPI dependency (constant-time `hmac.compare_digest`; 401 on mismatch; 500 if unconfigured).
  - `app/api/agents.py` — `APIRouter` with router-level dependency; `POST /agents/ping` logs forwarded user context.
  - `app/main.py` — mounts the router; `/health` stays public.
  - Added `__init__.py` for `app/`, `app/core/`, `app/api/`.
  - `agnet-service/.env.example` + root `.env.example`: `INTERNAL_API_TOKEN`.
- **docker-compose:** agent no longer exposed to the host (`expose: 8000` only — private network, satisfies "not accessible from public internet"); `INTERNAL_API_TOKEN` passed to both services; API uses `AgentService__BaseUrl=http://agent:8000`.

### Verification (#18/#19)

- Agent: `/health` → 200 (public); `/agents/ping` no token → **401**, wrong token → **401**, correct token → **200**; forwarded user context appears in agent logs.
- Backend→agent end-to-end: `/api/v1/agents/ping` no user token → **401**; with valid Clerk token → **200**, echo contains `userId: user_3Ij3bVkA6VdraN15mEnh5782F48` and `roles: ["associate","org:admin"]`.
- `ruff check app/` passes (fixed B008/B006 in `agents.py`); `dotnet build` 0 errors; `docker compose config` valid.
- Fixed during implementation: `InternalServiceAuthHandler` must be **transient** (not singleton) for `HttpClientFactory` (DelegatingHandler `InnerHandler` assignment).
- No changes committed; #18/#19 pending user review to close.

### Follow-up (same session): Issues #20/#21 — unit tests for backend + Python auth

- **Backend testability refactor:** extracted `AuthenticationConfiguration.BuildTokenValidationParameters(authority)` and created `Authorization/RoleClaimNormalizer.cs` (`PromoteRoleClaims`) so the auth rules are directly testable.
- **`.NET` test project `Aveline.Api.Tests`** (xUnit, added to `Aveline.Api.sln`; `dotnet test Aveline.Api/Aveline.Api.sln` now runs it). Test files (independent):
  - `JwtValidationTests.cs` — offline, locally-signed RSA tokens: valid, wrong-key, expired, not-yet-valid, wrong issuer, missing `aud`, mismatched audience, missing role claims.
  - `RoleClaimNormalizerTests.cs` — promotion of `user_role`/`org_role`, no-role, empty value, existing role not duplicated, null identity.
  - `AuthorizationPolicyTests.cs` — every role policy (Associates/Managers/Owners) allowed/denied cases + permission policies (`approvals:approve`, `payments:refund`, `catalog:view`, `settings:manage`) + raw-unpromoted claim denial.
  - `AuthenticationConfigurationTests.cs` — Bearer scheme registration + throws when authority missing.
  - `CorsConfigurationTests.cs` — policy origins allow-list + throws when missing/empty.
  - `ServiceToServiceAuthTests.cs` — handler adds `X-Internal-Token`, throws when unset; typed client forwards path to base address; client registration + throws when BaseUrl missing.
- **Python tests** `agnet-service/tests/test_internal_auth.py` (pytest + TestClient): `/health` public; `/agents/ping` missing/invalid token → 401, valid → 200 with echo; logs forwarded user context; dependency-level valid/missing/invalid → pass/401/401; unconfigured token → 500. Added `[tool.pytest.ini_options]` (`testpaths`, `pythonpath`, `asyncio_mode=auto`) to `agnet-service/pyproject.toml`.
- **Robustness fixes surfaced by tests:** `CorsConfiguration` and `InternalServiceAuthHandler` now reject empty/whitespace config values.
- **CI fix:** `build-api` now builds the solution (`Aveline.Api.sln`) so the `dotnet test --no-build` step can run the new test project.

### Verification (#20/#21)

- `dotnet test Aveline.Api/Aveline.Api.sln`: **58 passed, 0 failed** (Release build 0 errors).
- `pytest agnet-service/tests/`: **9 passed** (works from repo root too).
- `ruff check app/ tests/` passes.
- Coverage (per-class, auth/config/client): AuthorizationConfiguration, PermissionAuthorizationHandler, Permissions, RoleClaimNormalizer, CorsConfiguration, ServiceClientsConfiguration, InternalServiceAuthHandler, AgentServiceClient, PermissionRequirement → **100%**; AuthenticationConfiguration 63% (validation rules covered; DI plumbing not). Auth-code coverage comfortably above 80%.
- No changes committed; #20/#21 pending user review to close.

### Follow-up (same session): docs/tests + Issue #22 — integration tests for the full auth flow

- Created `docs/tests/README.md` — documents the test layers (unit vs integration), how to run each suite, coverage targets, and CI wiring.
- **Issue #22 — `Aveline.Api.Tests/FullAuthFlowIntegrationTests.cs`** (xUnit, `IAsyncLifetime`), fully offline:
  - `StubAuthServer` (real Kestrel on an ephemeral port) serves OIDC discovery + a JWKS for a locally-generated RSA key, so `AddJwtBearer` runs its real discovery → signature → issuer → lifetime pipeline.
  - `StubAgentServer` records the `X-Internal-Token` header and echoes the payload (mirrors `agnet-service/app/api/agents.py`).
  - `WebApplicationFactory<Program>` boots the real API; `Clerk__Authority`, `AgentService__BaseUrl`, `AgentService__InternalToken`, and `Clerk__RequireHttpsMetadata=false` are set via env vars (env vars load after `appsettings.*.json` and reliably win).
  - Cases: no token → 401; invalid token → 401; valid token → 200 with agent receiving the internal token and `userId`/`roles` propagated; valid token without roles → 403 (agent not called).
- **App change to enable this:** `AddAvelineAuthentication` now reads `Clerk:RequireHttpsMetadata` (defaults to `true`; overridable for dev/test HTTP authorities).
- Exposed `Program` for the test host: `public partial class Program;` in `Program.cs`.
- Test project now references `Microsoft.AspNetCore.Mvc.Testing` + `FrameworkReference Microsoft.AspNetCore.App`.

### Verification (#22 + full suite)

- `dotnet test Aveline.Api/Aveline.Api.sln`: **62 passed** (58 unit + 4 integration), 0 failed, no warnings.
- `pytest agnet-service/tests/`: 9 passed.
- Coverage with integration tests: **100%** across all auth/config/client classes **including `AuthenticationConfiguration` and `Program`**.
- Fixed along the way: dynamic-port Kestrel binding (`Listen(IPAddress.Loopback, 0)` not `ListenLocalhost(0)`); `RequireHttpsMetadata` for the HTTP stub; env-var config override (in-memory `ConfigureAppConfiguration` did not take precedence over `appsettings.Development.json`).
- No changes committed; #22 pending user review to close.

### Follow-up (same session): Issues #8/#9 — Clerk SDK in Flutter + auth token interceptor

Stack decided with user: **Provider (ChangeNotifier) + dio + go_router**, matching the app's clean-arch feature modules. Research on the beta `clerk_flutter` SDK (0.0.18-beta): `ClerkAuth`/`ClerkAuthBuilder`/`ClerkAuthentication`/`ClerkErrorListener`, `ClerkAuthState.sessionToken(templateName:)` → `SessionToken.jwt`, `isSignedIn`, `signOut()`, sessions persisted by the SDK.

- **#8 — Clerk SDK integration** (`frontend/aveline_mobile/lib/`):
  - `core/config/app_config.dart` — reads `CLERK_PUBLISHABLE_KEY` (required), `API_BASE_URL` (default `http://10.0.2.2:5091` for Android emulator), `JWT_TEMPLATE_NAME` (default `jwt-aveline-v1`) from `--dart-define`.
  - `core/theme/app_theme.dart` — Material 3 theme (seed `0xFF7B4B6F`).
  - `features/auth/{domain,data,presentation}` — `AuthRepository` (extends `core`'s `AuthTokenProvider` port), `AuthUser`, `AuthClaims` (parses `user_role`/`org_role`/`org_id`/`org_slug`), `ClerkAuthRepository`, `AuthUserMapper`, `AuthScreen` (SDK prebuilt sign-in/up UI under `ClerkErrorListener`).
  - `features/home/presentation/screens/home_screen.dart` — signed-in landing with user info + sign-out.
  - `app.dart` — composition root: `ClerkAuth` > `ClerkAuthBuilder` > `AvelineAppShell` (owns repository, Dio, GoRouter) > `MultiProvider` > `MaterialApp.router`. `core/router/route_guards.dart` holds the pure auth redirect; `ClerkAuthState` is the router's `refreshListenable`, so sign-in/out (and 401 sign-out) redirect automatically.
- **#9 — Auth token interceptor** (`core/network/`): `AuthTokenProvider` port, `AuthInterceptor` (attaches `Bearer`, on 401 refreshes via `refreshToken()`, retries once, and signs out if still rejected), `ApiClientFactory.create()`. Single Dio instance provided via DI.
- Android: added `INTERNET` permission; `clerk_auth` added as a direct dependency (imported by data layer).
- Docs: `features/auth/README.md`, `features/home/README.md`, updated `features/README.md` + `core/README.md`, and `GET_STARTED.md` Flutter run instructions.

### Verification (#8/#9)

- `flutter analyze`: **no issues** (CI command `flutter analyze --no-fatal-infos` also clean).
- `flutter test`: **13 passed** — `AuthClaims.fromBody`, `AuthUser` display-name/withClaims, `AuthInterceptor` (Bearer attach, no-token, 401 refresh+retry once, persistent-401 → sign-out, non-401 no retry), theme smoke test.
- `flutter build apk --debug` **failed in the native Gradle step**: the machine only has JDK 26 (`/usr/lib/jvm/java-26-openjdk`), and AGP 9.1.0's `JdkImageTransform`/`jlink` cannot run under it (`/home/kavindu/Android/platforms/android-36/core-for-system-modules.jar` transform error). Pre-existing toolchain limitation, not a code error — Dart code compiles and is verified by analyze + tests. Fix requires installing JDK 21 (or older) and pointing Gradle at it.
- No changes committed; #8/#9 pending user review to close.

### Follow-up (same session): Vite scaffold + Issues #10/#11 — Flutter route-guard tests + Clerk in the React dashboard

- **Scaffolded the web dashboard** (`frontend/web/`): Vite 8 + React 19 + TypeScript (via `bun create vite`), plus `@clerk/react@6.14.8`, `react-router-dom@7.18.3`, `axios@1.20.0` (bun package manager, matching the repo's `bun.lock` convention). Replaced the template demo with a real app structure:
  - `src/main.tsx` — `ClerkProvider` (signInUrl/signUpUrl) > `BrowserRouter` > `App`.
  - `src/App.tsx` — route table: `/`, `/sign-in/*`, `/sign-up/*`; protected tree via `ProtectedRoute`.
  - `src/components/` — `ProtectedRoute` (`useAuth`, redirects to `/sign-in`), `SignOutButton` (`useClerk().signOut(() => navigate(...))`).
  - `src/routes/` — `RootLayout` (header, identity, sign-out), `Dashboard`, `SignInPage`/`SignUpPage` (Clerk prebuilt components with `fallbackRedirectUrl="/"`).
  - `src/lib/` — `env.ts` (typed env: `VITE_CLERK_PUBLISHABLE_KEY`, `VITE_API_BASE_URL`), `api.ts` (axios client for #13).
  - `.env.example` + gitignored `.env.local` (real publishable key from root `.env.local`); `index.html` title/meta; clean `index.css`.
- **#11 (Clerk React SDK)** implemented by this scaffold: sign-in/sign-up UI, session persistence + logout via Clerk, protected dashboard.
- **#10 (Flutter route guards)**: the guard logic already shipped in #8/#9; hardened it for testability — `RouteGuards.redirectForAuth` now takes just `matchedLocation` (decoupled from `GoRouterState`) and added `test/core/router/route_guards_test.dart` (4 cases: unauth → redirect to `/auth`, unauth on auth screen → no redirect, auth on protected screen → no redirect, auth on auth screen → redirect home).
- Docs: `frontend/web/README.md`, updated `README.md` (web stack row) and `GET_STARTED.md` (web run instructions). Note: `@clerk/react` v6 removed provider-level `afterSignInUrl/afterSignUpUrl` — redirects are now component-level `fallbackRedirectUrl`.

### Verification (scaffold + #10/#11)

- `bun run build` (tsc -b + vite build): **passes** (98 modules, 360 kB JS / 108 kB gzip).
- `bun run lint` (oxlint): passes.
- `flutter analyze`: no issues; `flutter test`: **17 passed** (13 prior + 4 route-guard tests).
- No changes committed; web scaffold + #10/#11 pending user review to close.

### Follow-up (same session): Brand theming — shadcn in the web dashboard + Material Theme for Flutter

Applied the new brand identity in `.agents/brain/DESIGN.md` to both frontends. Per the user's instruction, only **colors, typography, and styles** were referenced; the DESIGN **spacing tokens were intentionally not applied**.

- **Web (`frontend/web/`)** — initialized **Tailwind CSS v4** + **shadcn/ui**:
  - Installed `tailwindcss`, `@tailwindcss/vite` (Vite plugin), `tw-animate-css`, `clsx`, `tailwind-merge`, `lucide-react`, `class-variance-authority`; `radix-ui` pulled in by the CLI.
  - Config: `components.json`, `src/lib/utils.ts` (`cn`), `@/*` alias in `vite.config.ts` (ESM-safe `fileURLToPath`) + `tsconfig.app.json` (`paths` without the deprecated `baseUrl`), Google Fonts (Hanken Grotesk + Playfair Display) in `index.html`.
  - `src/index.css` — Tailwind v4 theme mapping the brand tokens to shadcn CSS variables: warm oatmeal `--background #fff8f7`, charcoal `--foreground #1e1b1b`, maroon `--primary #7a303f` (per the DESIGN "solid burgundy buttons" spec) with `--ring #5d1a29`, tonal surfaces for `--muted/accent`, `--radius: 1rem` (16px card radius), `--chart-1..5` from the agent states (commerce maroon, visual gold, memory rose), plus a token-derived dark mode. Fonts wired as `--font-sans: Hanken Grotesk`, `--font-serif: Playfair Display`.
  - Added shadcn components: `button card input label badge separator avatar` (the CLI initially emitted them to a literal `@/` dir due to alias resolution — moved to `src/components/ui/`; `class-variance-authority` was missing from `bun add` and added manually).
  - Restyled `RootLayout`, `Dashboard`, `SignInPage`, `SignUpPage`, `SignOutButton`, `ProtectedRoute` with the brand: serif headings, uppercase overlines, warm maroon-tinted card shadow (`0 4px 20px rgba(122,48,63,0.06)`).
- **Flutter (`frontend/aveline_mobile/`)** — added `google_fonts`; rewrote `core/theme/app_theme.dart`:
  - Full Material 3 `ColorScheme` built from the DESIGN M3 tokens (primary `#5D1A29`, primaryContainer `#7A303F`, surface `#FFF8F7`, all surface-container tones, outline variants, inverse tokens). Note: this Flutter SDK names the token `onInverseSurface` (not `inverseOnSurface`).
  - `textTheme` from Google Fonts: Playfair Display for display/headlines, Hanken Grotesk for body/title/label (heights 1.2–1.6, letterSpacing converted from `em` at the DESIGN font sizes).
  - ThemeData: tonal card theme (16px radius), maroon FilledButtons (`primaryContainer` bg + white text), soft-beige filled inputs with maroon focus border.

### Verification (brand theming)

- Web: `bun run build` passes (2010 modules, CSS 30 kB); `bun run lint` passes with 2 non-fatal oxlint fast-refresh warnings on shadcn's generated `buttonVariants`/`badgeVariants` exports.
- Flutter: `flutter analyze` no issues; `flutter test` **17 passed**.
- No changes committed; theming pending user review.




