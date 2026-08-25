# Models — Commerce Validation & Optimization

EF Core entity classes that map to database tables owned by this slice:

| Model | Table |
|---|---|
| `Order.cs` | `Orders` |
| `OrderItem.cs` | `Order_Items` |
| `Payment.cs` | `Payments` |
| `ApprovalQueueEntry.cs` | `Approval_Queue` |
| `DeliveryPlan.cs` | `Delivery_Plans` |
| `BusinessRule.cs` | `Business_Rules` |

Models contain no business logic — they are pure data structure definitions.
