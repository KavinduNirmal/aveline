# Owner Onboarding Flow — Architecture

> **Status:** Implemented (feature branch `feature/69-owner-account-creation-flow`, PR #70).
> **Related:** [Pricing Plan](pricing_plan.md) · [Authentication](authentication.md) · [Authorization](authorization.md)
> Diagrams use the **C4 model** plus UML-style **sequence diagrams**, rendered with Mermaid.

---

## 1. Purpose

This document describes the **Owner Account Creation Flow** — the luxury, multi-step
onboarding experience a boutique owner completes before entering the Aveline dashboard.

The flow:

1. Confirms the account holder's role (Owner vs Staff).
2. Captures boutique identity and contact details.
3. Selects a subscription plan (demo mode — no payment).
4. Customizes the AI concierge context, with fields unlocking by plan tier.
5. Finalizes: activates the organization, provisions the Blossom allowance, and warms up the agents.

It is intentionally **resumable** — a partially completed wizard is hydrated from the backend
so the owner can continue where they left off.

---

## 2. System Context

```mermaid
graph TB
    subgraph People
        Owner["Boutique Owner"]
        StaffMember["Staff Member<br/>(accepts invite)"]
    end

    subgraph ExternalSystems
        Clerk["Clerk<br/>(identity provider, JWT)"]
    end

    subgraph Aveline["Aveline"]
        React["React Web App<br/>(OnboardingWizard)"]
        API["Aveline.Api<br/>(ASP.NET Core)"]
        Agent["Agent Service<br/>(FastAPI)"]
        DB[("PostgreSQL")]
    end

    Owner -->|"sign-in / sign-up"| Clerk
    Owner -->|"REST, JWT"| React
    StaffMember -->|"sign-in / sign-up"| Clerk
    StaffMember -->|"REST, JWT"| React
    React -->|"/api/v1/onboarding/*"| API
    API -->|"activate / hydrate draft org"| DB
    API -->|"POST /agents/warmup (internal token)"| Agent
```

The flow lives primarily in the **web React app**, orchestrated by the **Aveline.Api**
onboarding module, with the **Agent Service** handling the final warm-up call.

---

## 3. The 6-Step Flow

```mermaid
sequenceDiagram
    autonumber
    participant U as Owner (Browser)
    participant C as Clerk
    participant W as React OnboardingWizard
    participant A as Aveline.Api (/api/v1/onboarding)
    participant DB as PostgreSQL
    participant G as Agent Service (/agents/warmup)

    U->>C: Authenticate
    C-->>W: Clerk JWT
    W->>A: GET /status
    A-->>W: CurrentStep + draft org (or empty)

    rect rgb(250, 250, 250)
    Note over W: Step 2 — Account Type
    W->>W: Owner or Staff (invite code)
    end

    rect rgb(250, 250, 250)
    Note over W: Step 3 — Boutique Details
    W->>A: POST /owner { name, address, phone, ... }
    A->>DB: create draft Organization (OnboardingStep=3)
    A-->>W: org dto
    end

    rect rgb(250, 250, 250)
    Note over W: Step 4 — Plan Selection (demo mode)
    W->>A: POST /plan { planTier }
    A->>DB: set PlanTier (OnboardingStep=4)
    A-->>W: org dto
    end

    rect rgb(250, 250, 250)
    Note over W: Step 5 — Customize AI (tier-gated)
    W->>A: POST /customize { brandVoice, businessRules, ... }
    A->>DB: validate per tier, persist context (OnboardingStep=5)
    A-->>W: org dto
    end

    rect rgb(250, 250, 250)
    Note over W: Step 6 — Finalize
    W->>A: POST /complete
    A->>DB: activate org + owner (org:principal)
    A->>DB: provision UsageAccount Blossoms
    A->>G: POST /agents/warmup (resilient)
    A-->>W: BlossomAllocation + AgentWarmedUp
    W-->>U: Success screen → Dashboard
    end
```

---

## 4. Backend (Aveline.Api)

### 4.1 Endpoints

Mapped under the `/api/v1` group as `/api/v1/onboarding/*`. All require an authenticated
Clerk JWT. `RequireAuthorization()` is applied per endpoint.

| Method | Path                    | Purpose                                                        |
| ------ | ----------------------- | -------------------------------------------------------------- |
| GET    | `/api/v1/onboarding/status`    | Resume progress; returns current step + draft organization.    |
| POST   | `/api/v1/onboarding/owner`     | Step 3 — create/update the draft organization (boutique).      |
| POST   | `/api/v1/onboarding/plan`      | Step 4 — set the selected `PlanTier`.                          |
| POST   | `/api/v1/onboarding/customize` | Step 5 — validate & persist AI context (tier-gated).           |
| POST   | `/api/v1/onboarding/complete`  | Step 6 — activate org/user, provision Blossoms, warm up agents.|

**Files:** `Endpoints/OnboardingEndpoints.cs`,
`Modules/Organizations/Services/{IOnboardingService,OnboardingService}.cs`,
`Modules/Organizations/DTOs/OnboardingDtos.cs`.

### 4.2 Data model additions

`Modules/Organizations/Models/Organization.cs` gained boutique + onboarding metadata:

- **Identity:** `Address`, `PhoneNumber`, `Description`, `LogoUrl`
- **Plan:** `PlanTier` (default `Seed`)
- **AI context:** `BrandVoice`, `BusinessRules`, `PreferredColorsFabrics`, `CustomerPreferences`
- **Progress:** `OnboardingStep` (default 2), `HasCompletedOnboarding` (default false)

Column lengths, defaults, and the string conversion for `PlanTier` are configured in
`OrganizationConfiguration.cs`. A `GetByOwnerUserIdAsync` query was added to the repository
to find the owner's single draft organization.

> **Note:** A new EF Core migration for these columns is outstanding and will be generated when
> a database is accessible. See Open Questions.

### 4.3 Service responsibilities (`OnboardingService`)

| Method                          | Behaviour                                                                                                                                                |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GetStatusAsync`                | Reads the user and their draft org; derives the current wizard step (defaults to 2).                                                                     |
| `SaveBoutiqueDetailsAsync`      | Creates (or updates) the draft org with the user as owner, slugifies the name, and adds an `org:boutique_owner` membership. Advances to step 3.           |
| `SelectPlanAsync`               | Persists `PlanTier`; requires the org to exist first. Advances to step 4.                                                                                |
| `SaveAiCustomizationAsync`      | Validates input against the tier (below), persists AI context, advances to step 5.                                                                       |
| `CompleteOnboardingAsync`       | Activates org (`HasCompletedOnboarding=true`), sets the user to `owner` / `org:principal` / `Active`, provisions the `UsageAccount` Blossom allowance, invalidates user cache, and (resiliently) warms up agents. |

### 4.4 Tier-gated AI context

Enforcement aligns with `pricing_plan.md`:

| Tier     | Custom AI context                                                                        |
| -------- | ---------------------------------------------------------------------------------------- |
| Seed     | **Locked.** Custom fields rejected — the boutique runs on standard concierge presets.    |
| Bloom    | **Basic.** Brand voice, business rules, and store preferences. Deep memory rules locked. |
| Orchid   | **Full.** Brand voice, business rules, fabrics/colors, and customer memory rules.        |
| Rose     | **Full** (everything in Orchid).                                                         |
| Enterprise | Full (highest Blossom allowance).                                                       |

The React wizard mirrors this by sending only the permitted fields per tier; the backend
re-validates independently.

### 4.5 Demo-mode Blossom provisioning

Plans currently run in **demo mode — no payment**. On completion the service provisions the
monthly allowance for the chosen plan into a `UsageAccount` for the current billing period:

| PlanTier | Monthly Blossom limit |
| -------- | ---------------------:|
| Seed     | 150                   |
| Bloom    | 750                   |
| Orchid   | 2,000                 |
| Rose     | 5,000                 |
| Enterprise | 9,999               |

The owner is informed with a luxury notice: _"Your account is in demo mode. You'll be
contacted for payment."_

---

## 5. Agent Service integration

After the organization is finalized, `OnboardingService` calls
`POST /agents/warmup` through `IAgentServiceClient`, passing the boutique seed context
(`organizationId`, `boutiqueName`, `planTier`, `brandVoice`, `businessRules`,
`preferredColorsFabrics`, `customerPreferences`).

- The call is **resilient / non-fatal** — a warm-up failure logs a warning but does not fail
  onboarding.
- The Agent Service endpoint (`agnet-service/app/api/agents.py`) is protected by
  `require_internal_token` and logs a structured `agent_warmup` event, returning
  `{ "status": "warmed", "ready": true, ... }`.

---

## 6. Frontend (React Web App)

### 6.1 Routing

- `/onboarding` (`routes/OnboardingPage.tsx`) renders `OwnerOnboardingWizard`.
- `RequireAccountState` redirects any **OnboardingPending** account to `/onboarding`, where the
  unified wizard handles both owner setup and staff invitation acceptance.
- Once `POST /complete` succeeds the account becomes **Active** and the owner may enter `/app`.

### 6.2 Wizard architecture

`components/onboarding/` was split into modular pieces:

- **`OwnerOnboardingWizard.tsx`** — shell: supplies the provider and renders the shared chrome
  (animated background, brand header, progress stepper, error alert) plus the active step page.
- **`wizard-context.tsx`** — owns all shared state (`WizardDraft`), the on-mount status
  hydration, and every submission handler; exposed via `useOwnerOnboardingWizard()`.
- **`plans.ts`** — shared plan data (`PLANS`) and helpers used by the plan, review, and success
  screens.
- **`steps/`** — one page per stage: `AccountTypeStep`, `BoutiqueDetailsStep`,
  `PlanSelectionStep`, `AiCustomizationStep`, `ReviewStep`, `SuccessStep`.

### 6.3 Step pages

| Step | Component                | Notes                                                                  |
| ---- | ------------------------ | ---------------------------------------------------------------------- |
| 2    | `AccountTypeStep`        | Owner vs Staff; staff path accepts an invitation code to join an org.  |
| 3    | `BoutiqueDetailsStep`    | Boutique name (with live slug preview), address, phone, description, logo. |
| 4    | `PlanSelectionStep`      | Seed / Bloom / Orchid / Rose cards; demo-mode banner.                  |
| 5    | `AiCustomizationStep`    | Fields unlock per tier (Seed = locked; Bloom basic; Orchid/Rose full). |
| 6    | `ReviewStep` + `SuccessStep` | Summary, agent warm-up state, Blossom allocation, dashboard CTA.   |

### 6.4 Visual treatment

The onboarding page reuses the marketing site's **`AuroraField`** animated background
(drifting gradient blobs and blossoms) layered with a soft radial "paper" vignette so the
wizard remains legible — consistent with the **Quiet Luxury** brand system.

### 6.5 API client

`lib/onboarding.ts` provides typed wrappers (`fetchOnboardingStatus`,
`saveBoutiqueDetails`, `selectPlan`, `saveAiCustomization`, `completeOnboarding`) matching the
backend DTO contracts.

---

## 7. Security notes

- Every onboarding endpoint requires a valid **Clerk JWT**.
- **OnboardingPending** accounts are only allowed to reach onboarding, profile, and
  organization/invitation endpoints (see `OnboardingMiddleware`); they cannot reach business
  endpoints until the flow completes.
- The `/agents/warmup` call is a service-to-service request authenticated by the internal
  token (see [ADR-009](../ADR/ADR-009-internal-service-authentication.md)).
- The owner is the `org:principal`; staff join only via an invitation code accepted on their
  side.

---

## 8. Verification

- **.NET:** `Aveline.Api.Tests/OnboardingServiceTests.cs` and
  `OnboardingEndpointsIntegrationTests.cs` exercise the full 6-step flow end-to-end
  (step transitions, Seed/Bloom/Orchid tier gating, completion, Blossom provisioning,
  status resumption). `dotnet test Aveline.Api.Tests` passes (161 tests).
- **Agent Service:** `agnet-service/tests/test_agents_warmup.py` covers warmup auth and payload.
- **Frontend:** `vitest run` (onboarding contracts), `tsc -b`, `oxlint`, and `vite build`.

---

## 9. Open questions / follow-ups

1. **EF Core migration** for the new `Organization` columns is outstanding (database not
   available locally during implementation).
2. The plan document described dual-prefix mapping (`/api/onboarding` and `/api/v1/onboarding`);
   only the `/api/v1/onboarding` group is currently mapped, consistent with the React client.
3. Python warm-up tests were authored to mirror the existing internal-auth tests but were not
   executed locally (no Python environment); they should run in CI.
4. Demo-mode banner copy and Blossom figures should be confirmed against `pricing_plan.md` before
   billing is enabled.
