# Common: Extensions

Place all extension methods that are shared across the application here.

## What belongs here

- **`ServiceCollectionExtensions.cs`** — `IServiceCollection` extension methods. Each module
  registers its own services via a method like `services.AddCustomerConciergeModule()` called
  from here, which is called once in `Program.cs`.
- **`WebApplicationExtensions.cs`** — `IApplicationBuilder` / `WebApplication` extensions for
  configuring middleware pipelines (e.g., `app.UseAvelineMiddleware()`).
- **`ClaimsPrincipalExtensions.cs`** — Helpers to extract typed values from the JWT claims
  principal (e.g., `GetUserId()`, `GetUserRole()`).

## What does NOT belong here

- Module-specific registration code (each module provides its own `AddXModule()` method that
  is called from here, but implemented inside the module folder)
- Generic LINQ or string utility extensions (those go in a `Utils/` folder if needed)
