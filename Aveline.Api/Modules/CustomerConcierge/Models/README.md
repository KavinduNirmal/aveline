# Models — Customer Concierge

Place all EF Core entity classes for this module here.

Models map directly to PostgreSQL tables. They should:
- Use data annotations or Fluent API configurations (in `Infrastructure/Data/`)
- Represent the database structure — no business logic
- Use nullable reference types correctly (`string?` for nullable columns)

**Expected models (one class per table):**
- `Customer.cs` → `Customers` table
- `CustomerPreference.cs` → `Customer_Preferences` table
- `CustomerEvent.cs` → `Customer_Events` table
- `CustomerMemory.cs` → `Customer_Memory` table (has `float[]` vector column via pgvector)
- `CustomerInteraction.cs` → `Customer_Interactions` table

Do not put DTOs, ViewModels, or business logic here.
