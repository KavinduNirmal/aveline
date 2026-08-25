# Repositories — Customer Concierge

Place all repository classes for this module here.

Repositories are the ONLY place that touches EF Core and the database directly.
They should:
- Accept `AppDbContext` via constructor injection
- Expose clean, intent-revealing methods (`GetByPhoneNumberAsync`, `SearchByEmbeddingAsync`)
- Return domain Model objects or primitives — never raw `IQueryable` outside this layer
- Have a corresponding interface for testability

**Expected repositories:**
- `ICustomerRepository.cs` / `CustomerRepository.cs`
- `ICustomerMemoryRepository.cs` / `CustomerMemoryRepository.cs`
- `ICustomerInteractionRepository.cs` / `CustomerInteractionRepository.cs`

Do not put business logic or HTTP concerns here.
