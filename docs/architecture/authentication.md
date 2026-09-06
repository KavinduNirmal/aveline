# Authentication Flow — Architecture

> **Status:** Approved (see [ADR-007](../ADR/ADR-007-clerk-authentication.md)).
> Diagrams use the **C4 model** (context → container) plus UML-style sequence diagrams, rendered with Mermaid.

---

## 1. C4 Context Diagram

Shows the system boundary, the external actors, and the systems Aveline integrates with.

```mermaid
graph TB
    subgraph People
        Customer["Customer<br/>(via WhatsApp / Instagram)"]
        Associate["Boutique Associate<br/>(floor staff)"]
        Owner["Boutique Owner / Manager"]
    end

    subgraph ExternalSystems
        Clerk["Clerk<br/>(identity provider, JWT)"]
        WhatsApp["WhatsApp Business API"]
    end

    subgraph Aveline["Aveline — Boutique Concierge AI"]
        Flutter["Flutter Mobile App<br/>(associates)"]
        React["React Web Dashboard<br/>(owner / manager)"]
        API["Aveline.Api<br/>(ASP.NET Core)"]
        Agent["Agent Service<br/>(FastAPI + LangGraph)"]
        DB[("PostgreSQL 16 + pgvector")]
    end

    Customer -->|"WhatsApp messages"| WhatsApp
    WhatsApp --> API
    Associate --> Flutter
    Owner --> React
    Flutter -->|"REST / SignalR, JWT"| API
    React -->|"REST, JWT"| API
    Flutter -->|"sign-in / sign-up"| Clerk
    React -->|"sign-in / sign-up"| Clerk
    API -->|"verify JWT (JWKS)"| Clerk
    API -->|"internal HTTP (service token)"| Agent
    Agent --> DB
    API --> DB

    style Clerk fill:#f3e5f5
    style API fill:#f3e5f5
    style Agent fill:#fff3e0
    style DB fill:#e0f2f1
```

---

## 2. C4 Container Diagram

Decomposes the two runtime backends and shows where authentication is enforced.

```mermaid
graph TB
    subgraph Aveline.Api["Aveline.Api (ASP.NET Core 10)"]
        Controllers["Controllers<br/>(REST endpoints)"]
        Auth["JWT Bearer Authentication<br/>(JwtBearer + Clerk JWKS)"]
        Policies["Authorization Policies<br/>(RBAC via claims)"]
        Services["Services"]
        Repos["Repositories"]
        EF["EF Core / Npgsql"]
    end

    subgraph AgentSvc["agnet-service (FastAPI + LangGraph)"]
        Middleware["Internal-Token Middleware<br/>(service-to-service auth)"]
        Workflows["LangGraph Workflows"]
        Tools["Allow-listed Tools"]
        Checkpointer["PostgreSQL Checkpointer"]
    end

    subgraph ClerkSvc["Clerk"]
        JWKS["JWKS Endpoint"]
        Sessions["Sessions / JWT Templates<br/>(jwt-aveline-v1)"]
    end

    DB[("PostgreSQL 16<br/>+ pgvector")]

    Controllers --> Auth
    Controllers --> Policies
    Controllers --> Services
    Services --> Repos
    Repos --> EF
    EF --> DB

    Auth -->|"fetch JWKS"| JWKS
    Policies -->|"user_role / org_role claims"| Auth

    Services -->|"internal HTTP with X-Internal-Token"| Middleware
    Middleware --> Workflows
    Workflows --> Tools
    Workflows --> Checkpointer
    Checkpointer --> DB

    style Auth fill:#f3e5f5
    style Middleware fill:#fff3e0
```

**Where authentication lives:**
- **Client side (Flutter / React):** Clerk SDK manages sessions, sign-in/up UI, and mints JWTs from the `jwt-aveline-v1` template.
- **Aveline.Api:** validates every bearer token against Clerk's JWKS (signature, issuer, audience) and enforces role-based authorization from `user_role` / `org_role` claims.
- **agnet-service:** does **not** accept user tokens. It only trusts an internal service token issued by Aveline.Api (shared secret header), so the agent is never directly reachable by clients.

---

## 3. Sequence Diagram — Login & Session

```mermaid
sequenceDiagram
    autonumber
    actor U as User (Associate / Owner)
    participant C as Client (Flutter / React)<br/>+ Clerk SDK
    participant K as Clerk
    participant A as Aveline.Api

    U->>C: Open app → tap Sign in
    C->>K: Start sign-in (email / social)
    K-->>U: Sign-in UI
    U->>K: Credentials
    K-->>C: Session created<br/>(jwt-aveline-v1 token: user_role, org_role)
    C->>C: Persist session
    C->>A: GET /api/user/me<br/>Authorization: Bearer <JWT>
    A->>K: Fetch JWKS (cached)
    A->>A: Validate signature, iss, aud, exp
    A->>A: Extract claims → HttpContext.User
    A-->>C: 200 { userId, email, userRole, orgRole }
```

---

## 4. Sequence Diagram — Authenticated API Call (with agent)

```mermaid
sequenceDiagram
    autonumber
    actor U as User (Associate)
    participant C as Flutter App
    participant A as Aveline.Api
    participant G as agnet-service (LangGraph)
    participant D as PostgreSQL + pgvector

    U->>C: Trigger concierge action
    C->>A: POST /api/...<br/>Authorization: Bearer <JWT>
    A->>A: Validate JWT (JWKS) + authorize (roles)
    A->>G: POST /agents/...<br/>X-Internal-Token: <shared secret>
    G->>G: Verify internal token
    G->>D: Run workflow, pgvector search, checkpoint
    D-->>G: Result
    G-->>A: Structured result
    A->>A: Persist business data
    A-->>C: 200 response
    Note over A,G: 401/403 surfaces if token invalid<br/>or role insufficient
```

---

## 5. Role Model

| Claim | Source | Semantics |
|---|---|---|
| `user_role` | `{{user.public_metadata.role}}` | Aveline **team-level** role (`staff`, `customer_relations`, `moderator`, `admin`, `owner`) |
| `org_role` | `{{org.role}}` | **Per-boutique** role (`org:boutique_staff`, `org:boutique_manager`, `org:boutique_supervisor`, `org:boutique_owner`) |
| `org_id` / `org_slug` | `{{org.id}}` / `{{org.slug}}` | Current organization context |

Authorization policies in the backend consume `user_role` / `org_role`; see [the authorization model](authorization.md) and [ADR-007](../ADR/ADR-007-clerk-authentication.md).
