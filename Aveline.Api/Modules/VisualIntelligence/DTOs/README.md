# DTOs — Visual Intelligence & Sourcing

Data Transfer Objects for this module's public API surface.

**Expected DTOs:**
- `CreateInventoryItemDto.cs`, `UpdateInventoryItemDto.cs`, `InventoryItemResponseDto.cs`
- `ImageUploadDto.cs`, `ImageAnalysisResultDto.cs`
- `CustomerMatchResponseDto.cs`, `NotifyCustomerMatchDto.cs`
- `OutfitComposeRequestDto.cs`, `OutfitResponseDto.cs`
- `CreateSourcingRequestDto.cs`, `SourcingRequestResponseDto.cs`, `SupplierOptionDto.cs`
- `SupplierDto.cs`, `CreateSupplierDto.cs`, `SupplierCatalogItemDto.cs`

Use `record` types with validation annotations. Never expose EF Core entities directly.
