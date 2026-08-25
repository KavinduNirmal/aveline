# DTOs — Commerce Validation & Optimization

Data Transfer Objects for this module's public API surface.

**Expected DTOs:**
- `CreateOrderDto.cs`, `UpdateOrderDto.cs`, `OrderResponseDto.cs`, `OrderItemDto.cs`
- `MarginCalculationResultDto.cs`, `ApplyDiscountDto.cs`
- `GeneratePaymentRequestDto.cs`, `PaymentResponseDto.cs`, `RefundRequestDto.cs`
- `ApprovalDecisionDto.cs`, `RevisionRequestDto.cs`, `ApprovalQueueResponseDto.cs`
- `CreateDeliveryDto.cs`, `DeliveryPlanResponseDto.cs`, `DeliveryOptimizeRequestDto.cs`
- `BusinessRuleDto.cs`, `CreateBusinessRuleDto.cs`

Use `record` types with validation annotations. Never expose EF Core entities directly.
