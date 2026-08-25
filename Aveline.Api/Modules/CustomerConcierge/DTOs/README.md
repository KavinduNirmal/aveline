# DTOs — Customer Concierge

Place all Data Transfer Object classes for this module here.

DTOs define the **public API contract** — what clients send and what they receive.
They should:
- Use `record` types (preferred for immutability) or classes with init-only setters
- Use `DataAnnotations` for validation (`[Required]`, `[MaxLength]`, `[EmailAddress]`, etc.)
- Never expose internal EF Core models directly to the API layer
- Be named by intent: `CreateCustomerDto`, `CustomerResponseDto`, `MemorySearchRequestDto`

**Expected DTOs:**
- `CreateCustomerDto.cs`, `UpdateCustomerDto.cs`, `CustomerResponseDto.cs`
- `CustomerInteractionDto.cs`, `ParsedIntentDto.cs`
- `SaveMemoryDto.cs`, `CustomerMemoryResponseDto.cs`, `MemorySearchRequestDto.cs`
- `InteractionBriefResponseDto.cs`
- `WhatsAppWebhookPayloadDto.cs`, `SendWhatsAppMessageDto.cs`

Do not put EF Core navigation properties or business logic in DTOs.
