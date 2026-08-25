# Common — Shared Cross-Module Utilities

## Purpose

This folder contains code that is **genuinely shared** across two or more modules. It is NOT a
dumping ground. If something is only used by one module, it belongs inside that module.

---

## Folder Structure

```
Common/
├── Exceptions/     # Custom exception types and a global exception handler
├── Extensions/     # Extension methods (IServiceCollection, IApplicationBuilder, etc.)
└── Middleware/     # ASP.NET Core middleware (auth, logging, error handling)
```

---

## Exceptions/

**Include:**
- `NotFoundException.cs` — thrown when a resource doesn't exist (maps to 404)
- `ValidationException.cs` — thrown on bad input (maps to 400)
- `ConflictException.cs` — thrown on duplicate resource (maps to 409)
- `ForbiddenException.cs` — thrown on authorization failure (maps to 403)
- `GlobalExceptionHandler.cs` — `IExceptionHandler` implementation registered in `Program.cs`

**Do not include:**
- Module-specific exceptions (those live inside the module)
- Exception types that are never thrown outside one module

---

## Extensions/

**Include:**
- `ServiceCollectionExtensions.cs` — helper methods for registering all module services in `Program.cs`
- `WebApplicationExtensions.cs` — helper methods for registering middleware pipelines
- `ClaimsPrincipalExtensions.cs` — helpers to extract user ID/role from JWT claims

**Do not include:**
- Module-specific registration logic — those should be `RegisterXModule()` methods called from here

---

## Middleware/

**Include:**
- `RequestLoggingMiddleware.cs` — structured request/response logging (timing, status)
- `ClerkAuthenticationMiddleware.cs` — Clerk JWT validation and user context injection

**Do not include:**
- Module-specific business logic
- Any middleware that only applies to one route group (use filters instead)
