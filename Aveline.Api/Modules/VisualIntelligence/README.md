# Module: Visual Intelligence & Sourcing (Slice 2)

> **Owner:** Student 2  
> **Domain:** Catalog, inventory, visual analysis, and sourcing.

## Responsibility

This module owns everything related to **what the boutique has and can get**. It handles product
image analysis, inventory management, customer-to-item matching, outfit composition, and
sourcing requests to suppliers when items are not in stock.

---

## Folder Structure

```
VisualIntelligence/
├── Controllers/        # ASP.NET Core API controllers for this slice
├── Services/           # Business logic and orchestration
├── Repositories/       # Data access layer — EF Core queries for this domain
├── Models/             # EF Core entity classes mapped to DB tables
└── DTOs/               # Request/response shapes
```

---

## Controllers/

**Include:**
- `InventoryController.cs` — CRUD, status updates, low-stock queries
- `InventoryImagesController.cs` — image upload, trigger analysis
- `CustomerMatchingController.cs` — match customers to items, send notifications
- `OutfitsController.cs` — compose and retrieve outfit suggestions
- `SourcingRequestsController.cs` — create/update sourcing requests
- `SuppliersController.cs` — manage supplier records, browse supplier catalog

**Do not include:**
- Payment, order, or customer profile logic (other modules)
- Direct AI model calls (go through a Service)

---

## Services/

**Include:**
- `InventoryService.cs` — inventory CRUD, stock checks
- `ImageAnalysisService.cs` — delegates image URLs to the Python agent, stores results
- `CustomerMatchingService.cs` — triggers matching logic, records `Customer_Matches`
- `OutfitService.cs` — calls agent to compose outfits, persists compositions
- `SourcingService.cs` — create sourcing requests, query supplier API
- `SupplierService.cs` — manage supplier data

**Do not include:**
- Direct EF Core queries (delegate to Repositories)
- Payment or messaging logic

---

## Repositories/

**Include:**
- `InventoryRepository.cs`
- `InventoryImageRepository.cs`
- `CustomerMatchRepository.cs`
- `OutfitRepository.cs`
- `SourcingRequestRepository.cs`
- `SupplierRepository.cs`

**Do not include:**
- Business logic — only data access
- Repositories for tables owned by other modules

---

## Models/

Maps directly to the database tables owned by this slice:

| Model | Table |
|---|---|
| `InventoryItem` | `Inventory_Items` |
| `InventoryImage` | `Inventory_Images` |
| `OutfitComposition` | `Outfit_Compositions` |
| `OutfitItem` | `Outfit_Items` |
| `SourcingRequest` | `Sourcing_Requests` |
| `Supplier` | `Suppliers` |
| `CustomerMatch` | `Customer_Matches` |

**Do not include:**
- Models from other modules
- DTO shapes

---

## DTOs/

**Include:**
- `CreateInventoryItemDto.cs`, `UpdateInventoryItemDto.cs`, `InventoryItemResponseDto.cs`
- `ImageUploadDto.cs`, `ImageAnalysisResultDto.cs`
- `CustomerMatchResponseDto.cs`
- `OutfitComposeRequestDto.cs`, `OutfitResponseDto.cs`
- `CreateSourcingRequestDto.cs`, `SourcingRequestResponseDto.cs`
- `SupplierDto.cs`, `SupplierCatalogItemDto.cs`

**Do not include:**
- EF Core entity classes (those go in Models/)
- DTOs for other modules' domains
