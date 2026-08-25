# Services — Customer Concierge

Place all service classes for this module here.

Services orchestrate business logic. They:
- Are registered with DI as scoped (`services.AddScoped<ICustomerService, CustomerService>()`)
- Depend on Repositories for data access (never use DbContext directly)
- Call `AgentBridgeService` to trigger AI agent workflows via HTTP
- Should have a corresponding interface (`ICustomerService.cs`)

**Expected services:**
- `ICustomerService.cs` / `CustomerService.cs`
- `ICustomerMemoryService.cs` / `CustomerMemoryService.cs`
- `ICustomerInteractionService.cs` / `CustomerInteractionService.cs`
- `IAgentBridgeService.cs` / `AgentBridgeService.cs` — HTTP client for Python agent service

Do not put EF Core queries or HTTP routing logic here.
