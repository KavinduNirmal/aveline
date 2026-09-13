# Common: Exceptions

This folder holds the shared global exception handler.

## Current state (verified)

`GlobalExceptionHandler.cs` implements `IExceptionHandler` and turns every
unhandled exception into a stable `{ status, message, traceId }` `500` body. It is
registered at `Program.cs:40` (`AddExceptionHandler<GlobalExceptionHandler>()`) and
installed as the **outermost** middleware at `Program.cs:99`
(`app.UseExceptionHandler()`), so it catches every downstream failure, including the
security-header middleware.

The handler logs the exception server-side and never serialises the exception
message, type or stack trace. The client receives only the constant
`"An unexpected error occurred while processing the request."` and a `traceId`
(`Activity.Current?.Id`, falling back to the request's trace identifier), which
correlates the response with the server log.

> **Security headers must be re-applied inside the handler.** `UseExceptionHandler`
> sits *outside* `UseHsts()` and `UseAvelineSecurityHeaders()` (`Program.cs:101,109`),
> and the framework clears the response — status, body **and headers** — before it
> invokes an `IExceptionHandler`, so any hardening applied only by those middlewares
> is absent on a handled `500`. `GlobalExceptionHandler` therefore calls
> `SecurityConfiguration.ApplyHardeningHeaders` and `ApplyHstsHeader` itself before it
> writes the envelope. See finding §3.6 in
> `docs/reports/admin-backend-api-reconciliation.md`.

Module-specific exceptions still live inside their module:

- `Modules/Billing/Models/BillingDomainExceptions.cs`
- `Modules/Organizations/Models/OrganizationDomainExceptions.cs`
- `Modules/Integrations/Models/IntegrationDomainExceptions.cs`

## What belongs here

- `GlobalExceptionHandler.cs` — the `IExceptionHandler` registered via
  `app.UseExceptionHandler()`, mapping any unhandled exception to the stable
  `{ status, message, traceId }` envelope
- Shared exception types that more than one module needs (for example
  `NotFoundException`, `ValidationException`, `ConflictException`,
  `ForbiddenException`) if and when an endpoint stops translating them itself

## What does NOT belong here

- Module-specific exceptions (they live inside the module's folder)
- Exceptions that are only ever thrown and caught within a single method

> This README previously claimed, in its "Current state" section, that no
> `GlobalExceptionHandler.cs`, no `IExceptionHandler` implementation and no
> `UseExceptionHandler` registration existed, while simultaneously describing the
> handler as implemented. The contradiction was corrected in the #243 documentation
> pass; the handler was already live.
