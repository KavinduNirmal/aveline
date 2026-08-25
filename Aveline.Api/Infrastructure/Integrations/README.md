# Infrastructure: Integrations

This folder contains typed HTTP client wrappers for all external third-party APIs.

## What belongs here

Each external service gets its own subfolder or file pair (interface + implementation):

| Service | Files | Used By |
|---|---|---|
| WhatsApp Business API | `IWhatsAppClient.cs` / `WhatsAppClient.cs` | CustomerConcierge |
| Payment Gateway (PayHere/Stripe) | `IPaymentGatewayClient.cs` / `PaymentGatewayClient.cs` | Commerce |
| Courier API (PickMe/Uber) | `ICourierClient.cs` / `CourierClient.cs` | Commerce |
| Image Recognition API | `IImageRecognitionClient.cs` / `ImageRecognitionClient.cs` | VisualIntelligence |
| Agent Service (internal) | `IAgentServiceClient.cs` / `AgentServiceClient.cs` | All modules |

## Registration

All integration clients are registered in `Program.cs` (or via `Common/Extensions/`) using
`services.AddHttpClient<IWhatsAppClient, WhatsAppClient>()`.

## Rules

- All base URLs and API keys must come from `IConfiguration` / environment variables
- Never hardcode credentials or base URLs in this folder
- Wrap all outbound HTTP calls in try/catch — map HTTP failures to domain exceptions
- Use `Polly` for retry and circuit-breaker policies on third-party calls

## What does NOT belong here

- Business logic (that belongs in module Services)
- Database access
- Any code that maps business entities — clients return raw response DTOs only
