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

