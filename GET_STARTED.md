# Getting Started with Aveline (Boutique Concierge AI)

Welcome to the **Aveline** repository! This document walks developers and coding agents through the setup, development workflow, git conventions, and project standards.

---

## 1. Prerequisites

Ensure you have the following installed on your machine:

- **Package Manager**: [Bun](https://bun.sh/) _(Recommended)_, [pnpm](https://pnpm.io/), or `npm`
- **.NET SDK**: .NET 10 SDK (for `Aveline.Api/`)
- **Python**: Python 3.12+ (for `agnet-service/`)
- **Flutter**: Flutter 3.13+ / Dart 3.13+ (for `frontend/aveline_mobile/`)
- **Docker & Docker Compose**: For local PostgreSQL (pgvector) and Redis instances
- **Git**: Configured with your university/GitHub credentials

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
> `USER_INFORMATION_FOR_AGENT.md` is automatically gitignored to keep your student information private.

### Step 3: Install Root Dependencies & Setup Husky

Install dependencies to activate the Husky pre-commit hooks:

```bash
bun install
# Alternatively: pnpm install / npm install
```

> [!IMPORTANT]
> **Lockfile Policy**: Only `bun.lock` / `bun.lockb` is tracked in version control. Non-bun lockfiles (`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`) are automatically ignored and blocked by pre-commit hooks.

### Step 4: Install and Update Agent Skills

This repository includes specialized agent skills in `.agents/skills/` covering testing patterns, database optimization, Flutter UI architecture, .NET patterns, and LangGraph agent orchestration.

To inspect or update agent skills for your environment:

```bash
bunx skills update
# or list active skills:
bunx skills list
```

### Step 5: Start Local Database and Infrastructure

Start PostgreSQL (with `pgvector`) and Redis containers using Docker Compose:

```bash
docker compose up -d postgres redis
```

---

## 3. Running Sub-Projects Locally

### ASP.NET Core API (`Aveline.Api/`)

```bash
cd Aveline.Api
dotnet restore
dotnet run
```

- Swagger / OpenAPI endpoint: `http://localhost:5000/openapi/v1.json`

### Python Agentic AI Service (`agnet-service/`)

```bash
cd agnet-service
python3 -m venv .venv
source .venv/bin/activate  # On Windows: .venv\Scripts\activate
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8000
```

- Health Check: `http://localhost:8000/health`

### Flutter Mobile App (`frontend/aveline_mobile/`)

```bash
cd frontend/aveline_mobile
flutter pub get
flutter run
```

---

## 4. Git Flow & Contribution Rules

### Branching Strategy

- **Default Integration Branch**: `development`
- **Main Production Branch**: `main`
- Always branch off from `development`:
  - Features: `feature/<slice-name>-<short-description>` (e.g. `feature/customer-memory-search`)
  - Bug fixes: `fix/<slice-name>-<short-description>` (e.g. `fix/commerce-margin-calculation`)
  - Chores: `chore/<description>`

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
   - Review [docs/ai-usage/README.md](/docs/ai-usage/README.md) for required formats.
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

---

## 7. Useful Commands Reference

| Task                             | Command                                           |
| -------------------------------- | ------------------------------------------------- |
| Run all backend tests            | `dotnet test Aveline.Api`                         |
| Run Python agent tests           | `pytest agnet-service/tests/ -v`                  |
| Run Flutter tests                | `flutter test` (inside `frontend/aveline_mobile`) |
| Check Flutter formatting & lints | `flutter analyze`                                 |
| View active agent skills         | `bunx skills list`                                |
| Update agent skills              | `bunx skills update`                              |
