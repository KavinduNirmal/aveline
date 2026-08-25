# Infrastructure: Data

This folder contains the EF Core database infrastructure shared by all modules.

## What belongs here

- **`AppDbContext.cs`** — The single `DbContext` for the entire application. All module
  entities are registered here via `DbSet<T>` properties.
- **`Configurations/`** — Fluent API entity type configurations (one file per model, e.g.,
  `CustomerConfiguration.cs`, `InventoryItemConfiguration.cs`). This is where column types,
  constraints, indexes, and pgvector column types are defined.
- **`Migrations/`** — EF Core auto-generated migration files. Never hand-edit these.
- **`Seed/`** — Optional data seeding scripts for development (demo customers, inventory items,
  business rules). Never seed in production.

## What does NOT belong here

- Business logic of any kind
- Repository implementations (those live in each module's `Repositories/` folder)
- Raw SQL scripts (use EF Core Migrations instead)

## pgvector setup

The `Customer_Memory` table's `embedding` column uses the `vector` type from pgvector.
Configure this in `CustomerMemoryConfiguration.cs` using the `Pgvector.EntityFrameworkCore`
package. The `AppDbContext` must call `EnsureExtension("vector")` or run the SQL
`CREATE EXTENSION IF NOT EXISTS vector` in the initial migration.
