# Feature: Commerce (Slice 3)

> **Owner:** Student 3  
> **Domain:** Commerce Validation & Optimization

## data/

**Include:**
- `orders_api_client.dart` — calls to `/api/orders`
- `payments_api_client.dart` — calls to `/api/payments`
- `approvals_api_client.dart` — calls to `/api/approvals`
- `deliveries_api_client.dart` — calls to `/api/deliveries`
- `order_repository_impl.dart` — implements `OrderRepository`
- `order_dto.dart`, `payment_dto.dart`, `approval_dto.dart` — JSON response models

## domain/

**Include:**
- `order.dart`, `payment.dart`, `approval_queue_entry.dart` — Core entities
- `order_repository.dart` — Abstract repository interface
- `create_order_use_case.dart`, `check_approval_status_use_case.dart` etc.

## presentation/

**Include:**
- `screens/` — `OrderListScreen`, `OrderDetailScreen`, `ApprovalPendingScreen`
- `widgets/` — `OrderCard`, `PaymentStatusBadge`, `ApprovalStatusWidget`
- `state/` — State management for orders, approvals, deliveries

Do not import from other feature folders' `presentation/` or `domain/` layers.
