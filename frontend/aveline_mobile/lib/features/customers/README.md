# Feature: Customers (Slice 1)

> **Owner:** Student 1  
> **Domain:** Customer Concierge & Memory

## data/

**Include:**
- `customer_api_client.dart` — `Dio`/`http` calls to `/api/customers` and `/api/customer-interactions`
- `customer_memory_api_client.dart` — calls to `/api/customer-memory`
- `customer_repository_impl.dart` — implements `CustomerRepository` interface from `domain/`
- `customer_dto.dart` — JSON-serializable models (use `json_serializable` or `freezed`)

## domain/

**Include:**
- `customer.dart` — The core `Customer` entity (pure Dart, no Flutter dependencies)
- `customer_repository.dart` — Abstract interface defining repository contract
- `get_customer_use_case.dart`, `save_customer_memory_use_case.dart` etc.

## presentation/

**Include:**
- `screens/` — Full-page screens (`CustomerListScreen`, `CustomerDetailScreen`)
- `widgets/` — Feature-specific widgets (`CustomerCard`, `InteractionTile`)
- `state/` — State management for this feature (provider/riverpod/bloc)

Do not import from other feature folders' `presentation/` or `domain/` layers.
