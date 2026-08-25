# Feature: Inventory (Slice 2)

> **Owner:** Student 2  
> **Domain:** Visual Intelligence & Sourcing

## data/

**Include:**
- `inventory_api_client.dart` — calls to `/api/inventory` and `/api/inventory-images`
- `sourcing_api_client.dart` — calls to `/api/sourcing-requests` and `/api/suppliers`
- `inventory_repository_impl.dart` — implements `InventoryRepository`
- `inventory_item_dto.dart` — JSON-serializable response models

## domain/

**Include:**
- `inventory_item.dart` — Core `InventoryItem` entity
- `inventory_repository.dart` — Abstract repository interface
- `search_inventory_use_case.dart`, `create_sourcing_request_use_case.dart` etc.

## presentation/

**Include:**
- `screens/` — `InventoryListScreen`, `InventoryDetailScreen`, `SourcingScreen`
- `widgets/` — `InventoryItemCard`, `OutfitSuggestionCard`, `SourcingRequestTile`
- `state/` — State management for inventory and sourcing

Do not import from other feature folders' `presentation/` or `domain/` layers.
