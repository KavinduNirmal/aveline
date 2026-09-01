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




