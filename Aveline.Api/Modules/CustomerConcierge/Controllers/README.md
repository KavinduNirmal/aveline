# Controllers — Customer Concierge

Place all ASP.NET Core controller classes for this module here.

Each controller should:
- Be decorated with `[ApiController]` and `[Route("api/...")]`
- Only handle HTTP concerns: routing, model binding, response status codes
- Delegate ALL business logic to the corresponding Service class
- Return `IActionResult` or `ActionResult<T>` with appropriate HTTP verbs

**Expected controllers:**
- `CustomersController.cs`
- `CustomerInteractionsController.cs`
- `CustomerMemoryController.cs`
- `WhatsAppController.cs`

Do not put business logic, EF Core queries, or AI calls directly in controllers.
