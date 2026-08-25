# Infrastructure — Technical Cross-Cutting Concerns

## Purpose

This folder contains the **technical plumbing** of the application: database access infrastructure
and third-party service clients. It has no business logic. It is shared by all modules.

---

## Folder Structure

```
Infrastructure/
├── Data/           # EF Core DbContext, migrations, seeding, pgvector setup
└── Integrations/   # HTTP clients and adapters for external third-party APIs
```

---

## Data/

See `Data/README.md` for details.

**Summary:** Contains the `AppDbContext`, EF Core entity type configurations,
database migrations, and optional seed data scripts.

---

## Integrations/

See `Integrations/README.md` for details.

**Summary:** Contains typed `HttpClient` wrappers for external services
(WhatsApp Business API, payment gateway, courier API, image recognition API).

---

## Rules

- Code in this folder must not contain business logic
- All database access by module repositories must go through `AppDbContext` defined here
- Third-party API keys and base URLs must be read from `IConfiguration` — never hardcoded
