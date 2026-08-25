# Module: Customer Concierge & Memory (Slice 1)

> **Owner:** Student 1  
> **Domain:** Customer relationships, communication, and semantic memory.

## Responsibility

This module owns everything related to knowing **who the customer is** and **what they want**.
It handles inbound WhatsApp messages, maintains customer profiles and preference history, stores
semantic memory via pgvector, and drafts personalized staff-facing briefs and outbound responses.

---

## Folder Structure

```
CustomerConcierge/
├── Controllers/        # ASP.NET Core API controllers for this slice
├── Services/           # Business logic and orchestration (calls AI agent, DB)
├── Repositories/       # Data access layer — EF Core queries for this domain
├── Models/             # EF Core entity classes mapped to DB tables
└── DTOs/               # Request/response shapes (input validation + output contracts)
```

---

## Controllers/

**Include:**
- `CustomersController.cs` — CRUD for customer records
- `CustomerInteractionsController.cs` — logging/retrieving WhatsApp interactions
- `CustomerMemoryController.cs` — storing and searching semantic memories
- `WhatsAppController.cs` — webhook receiver and outbound message sender

**Do not include:**
- Any inventory, order, or payment logic (those belong in other modules)
- Direct LangGraph/agent calls (go through a Service)

---

## Services/

**Include:**
- `CustomerService.cs` — customer profile CRUD, loyalty lookups
- `CustomerMemoryService.cs` — save/retrieve/search semantic memories via pgvector
- `CustomerInteractionService.cs` — log interactions, extract parsed intent
- `AgentBridgeService.cs` — HTTP client that calls the Python agent service endpoint

**Do not include:**
- Raw SQL or EF Core queries (delegate to Repositories)
- Controller-level request parsing

---

## Repositories/

**Include:**
- `CustomerRepository.cs`
- `CustomerMemoryRepository.cs`
- `CustomerInteractionRepository.cs`

**Do not include:**
- Any service or business logic — only data access
- Repositories for other modules' tables

---

## Models/

Maps directly to the database tables owned by this slice:

| Model | Table |
|---|---|
| `Customer` | `Customers` |
| `CustomerPreference` | `Customer_Preferences` |
| `CustomerEvent` | `Customer_Events` |
| `CustomerMemory` | `Customer_Memory` (has `vector` column) |
| `CustomerInteraction` | `Customer_Interactions` |

**Do not include:**
- Models for tables owned by other slices
- ViewModels or DTO shapes (those go in DTOs/)

---

## DTOs/

**Include:**
- `CreateCustomerDto.cs`, `UpdateCustomerDto.cs`, `CustomerResponseDto.cs`
- `CustomerInteractionDto.cs`
- `CustomerMemoryDto.cs`, `MemorySearchRequestDto.cs`
- `WhatsAppWebhookPayloadDto.cs`, `SendMessageDto.cs`

**Do not include:**
- EF Core entity classes (those go in Models/)
- Any DTO for another module's domain
