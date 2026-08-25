# Services — Commerce Validation & Optimization

Place all service classes for this module here.

**Expected services:**
- `IOrderService.cs` / `OrderService.cs`
- `IPaymentService.cs` / `PaymentService.cs`
- `IApprovalService.cs` / `ApprovalService.cs`
- `IDeliveryService.cs` / `DeliveryService.cs`
- `IBusinessRulesService.cs` / `BusinessRulesService.cs`

Services delegate to Repositories for data access. ApprovalService manages the
pause/resume workflow state machine in coordination with the LangGraph agent.
