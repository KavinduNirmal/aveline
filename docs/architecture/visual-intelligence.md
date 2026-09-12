# Visual Intelligence & Sourcing Architecture (Slice 2 — Elle)

## Overview

The **Visual Intelligence & Sourcing** slice (owned by Student 2 — Elle) manages the boutique's catalog, inventory search, visual feature extraction, customer-to-inventory matching, occasion-based outfit composition, and supplier sourcing.

It follows the **single modular monolith** architecture in `.NET 10` (`Aveline.Api/Modules/VisualIntelligence/`) and a specialized **LangGraph state graph** in Python (`agnet-service/app/agents/visual_insight/`).

---

## 1. Domain Entities & Database Schema

All entities are managed in `AppDbContext` and mapped with explicit EF Core configurations:

| Entity | Table | Description |
|---|---|---|
| `InventoryItem` | `Inventory_Items` | Core boutique inventory item with SKU, color, category, sizes, stock, price, and soft delete. |
| `InventoryImage` | `Inventory_Images` | Product image metadata, dominant colors, fabric attributes, and detection confidence. |
| `CustomerMatch` | `Customer_Matches` | Computed match between an inventory item and a customer based on preferences. |
| `OutfitComposition` | `Outfit_Compositions` | Curated multi-item ensemble for specific occasions. |
| `OutfitItem` | `Outfit_Items` | Individual items and styling roles within an outfit composition. |
| `SourcingRequest` | `Sourcing_Requests` | Sourcing tickets created when an item is unavailable in local stock. |
| `Supplier` | `Suppliers` | Supplier metadata, API endpoints, and wholesale catalogs. |

---

## 2. Service-to-Service API Endpoints

All agent tooling communicates over authenticated internal Minimal APIs guarded by `InternalServicePolicy` and requiring the `X-Internal-Token` header (ADR-009):

- `POST /internal/visual/inventory/search` — Filtered multi-tenant inventory search
- `GET /internal/visual/inventory/{itemId}` — Fetch item details
- `POST /internal/visual/inventory` — Create inventory item
- `PUT /internal/visual/inventory/{itemId}` — Update inventory details
- `PATCH /internal/visual/inventory/{itemId}/status` — Status transition (available / reserved / archived)
- `GET /internal/visual/inventory/low-stock` — Threshold-based low-stock query
- `POST /internal/visual/analyze-image` — Extract visual features from reference image URLs
- `GET /internal/visual/customer-matches/{itemId}` — Query matched customers
- `POST /internal/visual/customer-matches/{itemId}/generate` — Compute and persist new customer matches
- `POST /internal/visual/outfits/compose` — Generate and persist styled outfit compositions
- `POST /internal/visual/sourcing-requests` — Create and track sourcing tickets
- `GET /internal/visual/suppliers/{supplierId}/catalog` — Query supplier catalogs

---

## 3. Python LangGraph Agent Sub-Graph (`Elle`)

The Visual Insight Agent is structured as a deterministic LangGraph sub-graph:

```
                  +---------------------+
                  |  VisualIntentGate   |
                  +----------+----------+
                             |
         +-------------------+-------------------+
         | (item_search)     | (image_analysis)  | (outfit_composition)
         v                   v                   v
+-----------------+ +-----------------+ +-------------------+
| InventorySearch | |  ImageAnalyzer  | |  OutfitComposer   |
+--------+--------+ +--------+--------+ +---------+---------+
         |                   |                    |
         +-------------------+--------------------+
                             |
                             v
                  +---------------------+
                  |   FormatResponse    |
                  +----------+----------+
                             |
                             v
                  +---------------------+
                  |  route_after_visual |
                  +---------------------+
```

### Multi-Tenant Caching Lifecycles:
1. **`InventorySearchCache`**: Hashes `organizationId` into cache keys to prevent cross-tenant leakage. Automatically invalidated on item create, update, stock change, status change, and soft delete.
2. **`ProductAnalysisCache`**: 1-hour TTL (3600s) on image feature extraction results.

---

## 4. Contract Enforcement & Schema

Outputs strictly adhere to the `VisualAgentOutput` schema with typed `FoundItem`, `PieceItem`, and `SourcingRequestDto` components.
