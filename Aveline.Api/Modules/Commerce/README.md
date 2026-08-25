# Module: Commerce Validation & Optimization (Slice 3)

> **Owner:** Student 3  
> **Domain:** Pricing, payments, approvals, and delivery.

## Responsibility

This module owns everything related to **making the deal work**. It handles order creation,
margin calculation, discount application, payment request generation and validation, the
human-in-the-loop approval queue, and delivery planning and booking via courier APIs.

---

## Folder Structure

```
Commerce/
├── Controllers/        # ASP.NET Core API controllers for this slice
├── Services/           # Business logic and orchestration
├── Repositories/       # Data access layer — EF Core queries for this domain
├── Models/             # EF Core entity classes mapped to DB tables
└── DTOs/               # Request/response shapes
```

---

## Controllers/

**Include:**
- `OrdersController.cs` — create, list, retrieve, update, validate orders
- `PaymentsController.cs` — generate payment requests, validate, refund
- `ApprovalsController.cs` — list pending approvals, approve/reject/revise
- `DeliveriesController.cs` — plan, book, and optimize delivery routes
- `BusinessRulesController.cs` — manage dynamic business rules (discount thresholds, etc.)

**Do not include:**
- Customer profile or inventory logic (other modules)
- Direct LangGraph calls (go through a Service)

---

## Services/

**Include:**
- `OrderService.cs` — order lifecycle management, margin calculation, discount application
- `PaymentService.cs` — generate payment links, validate payment webhooks, handle refunds
- `ApprovalService.cs` — trigger approval queue, pause/resume workflow, broadcast status
- `DeliveryService.cs` — route optimization, courier API integration, status tracking
- `BusinessRulesService.cs` — load and evaluate active business rules

**Do not include:**
- Direct EF Core queries (delegate to Repositories)
- Inventory stock checks (query VisualIntelligence module or its service)

---

## Repositories/

**Include:**
- `OrderRepository.cs`
- `PaymentRepository.cs`
- `ApprovalRepository.cs`
- `DeliveryRepository.cs`
- `BusinessRulesRepository.cs`

**Do not include:**
- Business logic — only data access
- Repositories for tables owned by other modules

---

## Models/

Maps directly to the database tables owned by this slice:

| Model | Table |
|---|---|
| `Order` | `Orders` |
| `OrderItem` | `Order_Items` |
| `Payment` | `Payments` |
| `ApprovalQueueEntry` | `Approval_Queue` |
| `DeliveryPlan` | `Delivery_Plans` |
| `BusinessRule` | `Business_Rules` |

**Do not include:**
- Models from other modules
- DTO shapes

---

## DTOs/

**Include:**
- `CreateOrderDto.cs`, `UpdateOrderDto.cs`, `OrderResponseDto.cs`
- `OrderItemDto.cs`
- `GeneratePaymentRequestDto.cs`, `PaymentResponseDto.cs`
- `ApprovalDecisionDto.cs`, `ApprovalQueueResponseDto.cs`
- `CreateDeliveryDto.cs`, `DeliveryPlanResponseDto.cs`, `DeliveryOptimizeRequestDto.cs`
- `BusinessRuleDto.cs`

**Do not include:**
- EF Core entity classes (those go in Models/)
- DTOs for other modules' domains
