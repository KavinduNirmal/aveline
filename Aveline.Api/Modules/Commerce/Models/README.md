# Models — Commerce Validation & Optimization

EF Core entity classes that map to database tables owned by this slice:

| Model | Table |
|---|---|
| `Order.cs` | `Orders` |
| `OrderItem.cs` | `OrderItems` |
| `Payment.cs` | `Payments` |
| `ApprovalQueueEntry.cs` | `ApprovalQueue` |
| `DeliveryPlan.cs` | `DeliveryPlans` |
| `BusinessRule.cs` | `BusinessRules` |

Models contain no business logic — they are pure data structure definitions.
