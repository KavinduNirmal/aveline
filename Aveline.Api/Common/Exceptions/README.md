# Common: Exceptions

Place all custom exception types and the global exception handler here.

## What belongs here

- `NotFoundException.cs` — Thrown when a requested resource doesn't exist. Maps to **HTTP 404**.
- `ValidationException.cs` — Thrown when input fails business validation. Maps to **HTTP 400**.
- `ConflictException.cs` — Thrown when a duplicate resource is created. Maps to **HTTP 409**.
- `ForbiddenException.cs` — Thrown when the authenticated user lacks permission. Maps to **HTTP 403**.
- `GlobalExceptionHandler.cs` — Implements `IExceptionHandler`. Registered in `Program.cs` via
  `app.UseExceptionHandler()`. Maps exception types to standardized `ProblemDetails` responses.

## What does NOT belong here

- Module-specific exceptions (e.g., `PaymentFailedException`) — those live inside the module's folder
- Exceptions that are only ever thrown and caught within a single method

## Pattern

```csharp
// In a service:
throw new NotFoundException($"Customer with ID {id} was not found.");

// GlobalExceptionHandler maps this → 404 ProblemDetails response
```
