# Common: Middleware

Place all ASP.NET Core middleware classes here.

## What belongs here

- **`RequestLoggingMiddleware.cs`** — Logs each incoming request and outgoing response with
  method, path, status code, and duration. Registered early in the pipeline.
- **`ClerkAuthenticationMiddleware.cs`** — Validates the Clerk JWT bearer token, extracts the
  user ID and role, and populates `HttpContext.User`. All protected endpoints depend on this.

## What does NOT belong here

- Authorization filters (use `[Authorize(Roles = "Owner")]` attribute directly on controllers)
- Route-specific logic (use ASP.NET Core endpoint filters instead)
- Business logic of any kind

## Registration order in Program.cs

Middleware order matters in ASP.NET Core. The recommended order is:

1. `UseExceptionHandler` (global error handling)
2. `UseHttpsRedirection`
3. `UseRequestLogging` (timing before auth so all requests are logged)
4. `UseAuthentication` + `UseAuthorization` (Clerk JWT)
5. `MapControllers`
