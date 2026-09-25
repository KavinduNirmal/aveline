# Getting Started with Aveline (Boutique Concierge AI)

Welcome to the **Aveline** repository! This document walks developers and coding agents through the setup, development workflow, git conventions, and project standards.

---

## 1. Prerequisites

Ensure you have the following installed on your machine:

- **Package Manager**: [Bun](https://bun.sh/) _(Recommended)_, [pnpm](https://pnpm.io/), or `npm`
- **.NET SDK**: .NET 10 SDK (for `Aveline.Api/`)
- **Python**: Python 3.12+ (for `agnet-service/`)
- **Flutter**: Latest stable Flutter / Dart SDK 3.13+ (for `frontend/aveline_mobile/`)
- **Docker & Docker Compose**: For local PostgreSQL (pgvector), Redis, and the other containers
- **Git**: Configured with your university/GitHub credentials
- **OpenSSL** _(optional)_ — used below to generate the local secrets `docker compose` requires

---

## 2. Initial Setup Guide

### Step 1: Clone the Repository

```bash
git clone https://github.com/KavinduNirmal/aveline.git
cd aveline
```

### Step 2: Configure Personalization for AI Agents

To allow AI coding agents to automatically log and attribute your contributions correctly without exposing sensitive details:

```bash
cp USER_INFORMATION_FOR_AGENT.example.md USER_INFORMATION_FOR_AGENT.md
```

Open [USER_INFORMATION_FOR_AGENT.md](USER_INFORMATION_FOR_AGENT.md) and fill in your details:

```env
STUDENT_ID=ITxxxxxxxx
STUDENT_NAME=Your Full Name
```

> [!NOTE]
> `USER_INFORMATION_FOR_AGENT.md` is automatically gitignored to keep your student information private. [`.agents/rules/Rules.md`](.agents/rules/Rules.md) requires it to be filled in before any development work.

### Step 3: Install Root Dependencies & Setup Husky

Install dependencies to activate the Husky pre-commit hooks:

```bash
bun install
# Alternatively: pnpm install / npm install
```

> [!IMPORTANT]
> **Lockfile Policy**: Only `bun.lock` / `bun.lockb` is tracked in version control. Non-bun lockfiles (`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`) are automatically ignored and blocked by pre-commit hooks.

### Step 4: Local Agent Skills

This repository ships project-specific agent skills in [`.agents/skills/`](.agents/skills/) — one directory per skill, each with a `SKILL.md`. They cover testing patterns, database optimization, Flutter UI architecture, .NET patterns, and LangGraph agent orchestration. `skills-lock.json` records the installed skills and their source hashes.

Read the skills relevant to your task before implementing. [`.agents/rules/Rules.md`](.agents/rules/Rules.md) and [`.agents/rules/Rules2.md`](.agents/rules/Rules2.md) are the binding rules for agent-assisted work, and long-running work plans live in [`.agents/plans/`](.agents/plans/).

### Step 5: Configure the Environment and Start Local Infrastructure

`docker compose` reads the repository-root `.env`, and several values are declared as required — the stack refuses to start without them, even when you only bring up one service. Create the file first:

```bash
cp .env.example .env
```

Then fill in the required keys in `.env`:

| Key                          | How to set it                                                          |
| ---------------------------- | ---------------------------------------------------------------------- |
| `POSTGRES_PASSWORD`          | Any local password; `change-me` is the documented default.             |
| `CREDENTIALS_ENCRYPTION_KEY` | `openssl rand -base64 32` — AES-256-GCM key for tenant credentials.    |
| `METRICS_SCRAPE_TOKEN`       | `openssl rand -hex 32` — credential for the API's `/metrics` endpoint. |
| `GRAFANA_ADMIN_PASSWORD`     | `openssl rand -hex 32`                                                 |
| `POSTGRES_EXPORTER_PASSWORD` | `openssl rand -hex 32`                                                 |

> [!IMPORTANT]
> Until these are set, `docker compose` fails with `required variable METRICS_SCRAPE_TOKEN is missing a value` (and the same for each of the other four keys) — the three observability keys are empty in `.env.example` on purpose. The `CREDENTIALS_ENCRYPTION_KEY` placeholder is unusable too: the API refuses to boot unless the value is valid base64 that decodes to exactly 32 bytes.

The Prometheus scrape token and the exporter password must also exist as files, which the exporter and Prometheus mount. Generate them from `.env` before starting those containers:

```bash
./scripts/sync_observability_secrets.sh
```

Now start PostgreSQL (with `pgvector`) and Redis:

```bash
docker compose up -d postgres redis
```

- PostgreSQL publishes `${POSTGRES_PORT}` from `.env` (`5433` in `.env.example`); Redis publishes `6379`.
- `docker compose up -d` starts the whole stack instead — PostgreSQL, Redis, the API, the agent service, the OTel collector, Prometheus, Grafana, Jaeger, and the postgres exporter.

---

## 3. Running Sub-Projects Locally

> For authentication-focused setup (Clerk keys, JWT template, sign-in flows), see
> [Running Aveline Locally with Authentication](docs/guides/local-auth-development.md).

### ASP.NET Core API (`Aveline.Api/`)

```bash
cd Aveline.Api
dotnet restore
dotnet run
```

- The API listens on `http://localhost:5091` — the same port the compose `api` service publishes. The generated OpenAPI document is served at `http://localhost:5091/openapi/v1.json` in Development, and health endpoints are at `/health`, `/health/live`, and `/health/ready`.
- With no connection string configured the API falls back to an in-memory database, so it boots without PostgreSQL but persists nothing across restarts. To point a host run at the compose PostgreSQL, export the connection string (the host port is whatever `POSTGRES_PORT` says in `.env` — `5433` in `.env.example`):

  ```bash
  export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=aveline;Username=aveline;Password=<POSTGRES_PASSWORD>"
  ```

- The API authenticates to the agent service with `AgentService:InternalToken`. Its committed development default (`change-me-internal-token`) is rejected by the agent service, so a host run that talks to a host agent needs the same non-placeholder token on both sides:

  ```bash
  export AgentService__InternalToken=aveline-local-development-secret-token-2026
  ```

  When both services run under compose this is handled for you: `INTERNAL_API_TOKEN` in `.env` feeds both.

> [!NOTE]
> The API does not read an `.env` file itself — `Aveline.Api/.env.example` documents the keys, but the values must reach the process as real environment variables (`Clerk__Authority`, `ConnectionStrings__DefaultConnection`, `AgentService__InternalToken`, `Cors__AllowedOrigins__0`).

### Python Agentic AI Service (`agnet-service/`)

The service reads a `.env` file in its own directory, and refuses to start on a missing or placeholder `INTERNAL_API_TOKEN`:

```bash
cd agnet-service
cp .env.example .env          # set INTERNAL_API_TOKEN to the same value the API sends
python3 -m venv .venv
source .venv/bin/activate  # On Windows: .venv\Scripts\activate
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8000
```

- Health Check: `http://localhost:8000/health`
- `INTERNAL_API_TOKEN` must not be `change-me` or `change-me-internal-token`; `aveline-local-development-secret-token-2026` is the value `.env.example` ships.

### Flutter Mobile App (`frontend/aveline_mobile/`)

```bash
cd frontend/aveline_mobile
flutter pub get
flutter run \
  --dart-define=CLERK_PUBLISHABLE_KEY=pk_test_... \
  --dart-define=API_BASE_URL=http://10.0.2.2:5091
```

- `CLERK_PUBLISHABLE_KEY` (optional) — the Clerk publishable key (`pk_...`). Falls back to the test key committed in `lib/core/config/app_config.dart`, which that file documents as the default for local development.
- `API_BASE_URL` (optional) — defaults to `http://10.0.2.2:5091`, the host machine's
  API as seen from the Android emulator. On a physical device or iOS simulator,
  pass the host's LAN address instead (e.g. `http://192.168.x.x:5091`).
- `JWT_TEMPLATE_NAME` (optional) — defaults to `jwt-aveline-v1` (the Aveline
  template that mints `user_role`/`org_role` claims the backend authorizes on).
- `AVELINE_WEB_BASE_URL` (optional) — public origin of the web app, used to turn a
  handbook citation into an openable link. Empty by default; citations are still shown.

On sign-in the app persists the Clerk session; tokens are attached to API
requests automatically and a 401 triggers a token refresh, then sign-out.

### Web App (`frontend/web`)

```bash
cd frontend/web
bun install
cp .env.example .env.local   # fill in VITE_CLERK_PUBLISHABLE_KEY
bun dev
```

- `VITE_CLERK_PUBLISHABLE_KEY` (**required**) — Clerk publishable key (`pk_...`).
- `VITE_API_BASE_URL` (optional) — defaults to `http://localhost:5091`.
- `VITE_GRAFANA_ENABLED` / `VITE_GRAFANA_BASE_URL` (optional) — Grafana deep links; empty means a development build points at `http://localhost:3000` and a production build disables them.

Routes: `/` (public landing), `/plans`, `/contact`, `/docs/:slug` (product documentation), `/download`, `/sign-in/*`, `/sign-up/*`, `/onboarding` and `/org-setup` (authenticated setup), `/app/b/:slug/:section` (tenant dashboard), and `/admin/:userId/*` (administrator console). Sessions persist across reloads via Clerk.

---

## 4. Git Flow & Contribution Rules

### Branching Strategy

- **Default Integration Branch**: `development`
- **Production / Release Branch**: `master` (the repository's default branch; CI also watches `main`). Android and iOS releases are published only from `master` pushes.
- Always branch off from `development`:
  - Features: `feature/<slice-name>-<short-description>` (e.g. `feature/customer-memory-search`)
  - Bug fixes: `fix/<slice-name>-<short-description>` (e.g. `fix/commerce-margin-calculation`)
  - Chores: `chore/<description>`

See [docs/git-flow.md](docs/git-flow.md) for the full model, including how to branch a fix that
depends on another unmerged fix.

```bash
git checkout development
git pull origin development
git checkout -b feature/my-new-feature
```

### Frequent Syncing

> [!WARNING]
> Regularly pull the latest changes from `development` at the beginning and end of each working session to prevent stale branches and large merge conflicts:
>
> ```bash
> git fetch origin development
> git rebase origin/development  # or git merge origin/development
> ```

### Conventional Commits

Use standard conventional commit formats for all commits:

- `feat(customer): add semantic memory search endpoint`
- `fix(commerce): resolve discount threshold validation bug`
- `docs(adr): add ADR-003 for pgvector database strategy`
- `test(inventory): add unit tests for image attribute extraction`

---

## 5. Working with AI Coding Agents

This repository is agent-ready with established architectural boundaries and rules:

1. **Rule Enforcement**: All agent rules are located under `.agents/rules/`.
2. **Automated AI Usage Logging**:
   - University academic integrity requires logging AI usage per student.
   - At the beginning and end of every session, record your tasks, prompts, generated code, and manual modifications in:
     `docs/ai-usage/<your-name>.md`
   - Review [docs/ai-usage/README.md](docs/ai-usage/README.md) for required formats.
3. **Domain Ownership**:
   - **Slice 1 (Student 1)**: `CustomerConcierge` & `customer_memory` agent
   - **Slice 2 (Student 2)**: `VisualIntelligence` & `visual_insight` agent
   - **Slice 3 (Student 3)**: `Commerce` & `commerce` agent

---

## 6. Pre-Commit Quality Gates (Husky)

Before any commit is accepted, the automated Husky pre-commit hook runs 6 quality checks:

| Gate      | Check                                       | Action on Failure    |
| --------- | ------------------------------------------- | -------------------- |
| **[1/6]** | `.env` file staging check                   | ❌ Blocks commit     |
| **[2/6]** | Lockfile check (only `bun.lock` allowed)    | ❌ Blocks commit     |
| **[3/6]** | Secret detection scan                       | ❌ Blocks commit     |
| **[4/6]** | ASP.NET Core `dotnet build`                 | ❌ Blocks commit     |
| **[5/6]** | Python syntax validation (`py_compile`)     | ❌ Blocks commit     |
| **[6/6]** | Flutter static analysis (`flutter analyze`) | ⚠️ Displays warnings |

Gates 4–6 skip with a warning when the matching toolchain is not on your `PATH`. A false positive
in gate 3 can be bypassed for one commit with `git commit --no-verify`, but real credentials must
never be committed — not even in `.env.example`.

---

## 7. Useful Commands Reference

| Task                             | Command                                                |
| -------------------------------- | ------------------------------------------------------ |
| Run all backend tests            | `dotnet test Aveline.Api/Aveline.Api.sln`              |
| Run Python agent tests           | `cd agnet-service && pytest tests/ -v`                 |
| Lint the agent service           | `ruff check agnet-service/app/`                        |
| Run Flutter tests                | `cd frontend/aveline_mobile && flutter test`           |
| Check Flutter formatting & lints | `cd frontend/aveline_mobile && flutter analyze`        |
| Lint the web app                 | `cd frontend/web && bun run lint`                      |
| Run web tests                    | `cd frontend/web && bun run test`                      |
| Run web end-to-end tests         | `cd frontend/web && bun run test:e2e:install && bun run test:e2e` |
| Start the whole local stack      | `docker compose up -d`                                 |

---

## 8. Where to Go Next

| I want to…                                             | Read                                                                                             |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| Sign in locally with Clerk and see the full stack run  | [docs/guides/local-auth-development.md](docs/guides/local-auth-development.md)                   |
| Grant myself the Owner role in a local database        | [docs/guides/grant-team-owner-local.md](docs/guides/grant-team-owner-local.md)                   |
| Understand a service's environment variables           | [Aveline.Api/README.md](Aveline.Api/README.md), [agnet-service/README.md](agnet-service/README.md) |
| Run and extend the test suites                         | [docs/tests/README.md](docs/tests/README.md)                                                     |
| Call the API from a client                             | [docs/api/README.md](docs/api/README.md)                                                         |
| Understand why a decision was made                     | [docs/ADR/README.md](docs/ADR/README.md)                                                         |
