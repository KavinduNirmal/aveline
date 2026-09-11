# Common: Exceptions

This folder is a placeholder for **shared** exception types and a global exception handler.

## Current state (verified)

There is **no** `GlobalExceptionHandler.cs`, no `IExceptionHandler` implementation, and no
`UseExceptionHandler` registration. Endpoints currently translate failures themselves and
return the project's `Results.*` envelope (`{ "message": "..." }`, `Results.ValidationProblem`
for field validation). Module-specific exceptions live inside their module:

- `Modules/Billing/Models/BillingDomainExceptions.cs`
- `Modules/Organizations/Models/OrganizationDomainExceptions.cs`
- `Modules/Integrations/Models/IntegrationDomainExceptions.cs`

## What belongs here if a global handler is introduced

- `NotFoundException.cs` — maps to **HTTP 404**
- `ValidationException.cs` — maps to **HTTP 400**
- `ConflictException.cs` — maps to **HTTP 409**
- `ForbiddenException.cs` — maps to **HTTP 403**
- `GlobalExceptionHandler.cs` — implements `IExceptionHandler`, registered via
  `app.UseExceptionHandler()`, mapping exception types to a consistent error envelope

## What does NOT belong here

- Module-specific exceptions (they live inside the module's folder)
- Exceptions that are only ever thrown and caught within a single method

> This README previously documented the files above as if they existed. It was corrected
> while fixing defect D-9 in `docs/backend/backend-requirements.md`.
