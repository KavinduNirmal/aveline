# ADR-006: Deployment Platform

## Status

Accepted

## Context

The Aveline platform must be deployed to a cloud platform for the SE3090 demo phase. The deployment must satisfy the assignment's cross-platform evidence requirement — the full **Flutter → API → Agent → Database → React → API → Flutter** workflow must run end-to-end against a hosted backend — while staying within a strict **< $100 demo budget**.

Components that must be hosted:

| Component | Runtime | Notes |
|---|---|---|
| `Aveline.Api` | ASP.NET Core 10 Web API | Modular monolith, exposed to Flutter/React/WhatsApp |
| `agnet-service` | Python 3.12 / FastAPI + LangGraph | Multi-agent orchestration, PostgreSQL checkpointing, human-in-the-loop pauses |
| Database | PostgreSQL 16 + `pgvector` | Business data + `Customer_Memory` embeddings + LangGraph checkpoints |
| Web dashboard | React (Vite) | Owner/manager approvals (technology still TBD) |
| Flutter mobile | N/A (client) | APK built and distributed from CI, not cloud-hosted |
| Auth | Clerk (external SaaS) | JWT-based, no hosting required |

Non-functional constraints:

- Budget: **< $100** total for the demo phase (one semester).
- Automated deployments through **CI/CD** (GitHub Actions is already the repo's CI platform).
- A **DevOps narrative** is part of the assignment grading: IaC, environment separation, secrets management, observability.
- The team of 3 are full-time university students with access to **Azure for Students**.

## Options Considered

### 1. Microsoft Azure (Container Apps + Flexible Server + Static Web Apps)

**Pros:**

- **Azure for Students** provides a **$100 credit, no credit card, 12-month free tiers**, renewable yearly. The demo phase can run at approximately **$0/month**.
- Free tiers match the required services exactly: Container Apps (180k vCPU-seconds + 360k GiB-seconds + 2M requests/mo), PostgreSQL Flexible Server Burstable B1ms (750 hours + 32 GB storage/mo), Static Web Apps (100 GB bandwidth/mo), Container Registry Standard (12-month offer).
- `pgvector` is supported on Azure Database for PostgreSQL Flexible Server (`CREATE EXTENSION vector`).
- Native GitHub Actions integration with **OIDC passwordless auth** — no stored cloud credentials.
- Strong DevOps demonstration surface: **Bicep** IaC, managed identity for secrets, Application Insights, Cost Management budgets/alerts, revision-based deployments.
- Satisfies the assignment's explicit "cloud platform" requirement for the backend.

**Cons:**

- Scale-to-zero introduces a few seconds of cold start latency for demo traffic.
- Azure OpenAI quota/approval for student subscriptions can be limited (plain OpenAI API is the fallback).
- Requires Azure-specific tooling knowledge (Bicep, az CLI).

### 2. Railway

**Pros:** Very low startup cost (~$5/month), simple developer experience, `pgvector` support.

**Cons:** Not free for this workload; weaker built-in observability and IaC tooling; less compelling "cloud platform" and DevOps story for grading; no $100-credit equivalent.

### 3. Render

**Pros:** Easy free tier for web services and managed Postgres; simple deploys.

**Cons:** Free Postgres tier is limited and short-lived; hosting both the agent service and pgvector reliably on the free tier is awkward; no equivalent to the student credit.

### 4. Vercel + Supabase + external (the "best-of-breed" split)

**Pros:** Excellent DX for the React dashboard and Flutter clients.

**Cons:** Splits the stack across multiple vendors; weakens the unified DevOps/CD pipeline demonstration; does not satisfy a backend hosted on a "cloud platform" as cleanly; more moving parts to secure and cost-manage.

## Decision

Adopt **Microsoft Azure** as the single cloud platform for the demo phase:

| Component | Azure service | Tier / sizing |
|---|---|---|
| `Aveline.Api` | **Azure Container Apps** | Consumption, scale-to-zero (min replicas = 0) |
| `agnet-service` | **Azure Container Apps** (separate app) | Consumption, scale-to-zero |
| Database | **Azure Database for PostgreSQL Flexible Server** | Burstable **B1ms**, `pgvector` enabled |
| React dashboard | **Azure Static Web Apps** | Free tier |
| Container images | **Azure Container Registry** (Standard, 12-mo free) — GHCR as zero-cost fallback | Standard |
| Secrets | **Azure Key Vault** | Free; consumed via managed identity |
| Observability | **Application Insights + Log Analytics** | Free tier (5 GB logs/mo) |
| CI/CD | **GitHub Actions** with OIDC + **Bicep** IaC | Existing repo workflow |
| Flutter APK | CI `flutter build apk` → GitHub Release / Firebase App Distribution | $0 |
| Redis | **Omitted for demo** | Nice-to-have; not yet used in code |

**Funding:** use **Azure for Students** ($100 credit, no credit card) — the demo phase is effectively free, with the credit acting as a buffer for any overage or LLM usage.

## Consequences

**Positive:**

- Near-zero operating cost during the demo phase (well under the < $100 budget).
- A unified, Azure-native DevOps pipeline: Bicep IaC, OIDC passwordless auth, managed identity, revision-based deployments, Application Insights, and Cost Management budgets — all demonstrable for grading.
- One platform serves the entire backend + database + web surface; Flutter APK is produced by the same CI pipeline.

**Trade-offs and follow-on implications:**

- **Scale-to-zero cold starts**: endpoints must be warmed a few seconds before a live demo, or a single always-on replica enabled during the demo hour (at a small cost).
- **PostgreSQL free hours are finite (750 h/mo)**: the server should be stopped when not actively demoing.
- **Redis is omitted** from the demo footprint to save cost.
- **Bicep templates and a deploy workflow must be authored and validated** before the demo (tracked in `docs/deployment.md`).
- **LLM provider choice is deferred**: Azure OpenAI (preferred for the Azure narrative, subject to student quota) or plain OpenAI API (fallback) — key stored in Key Vault either way.
