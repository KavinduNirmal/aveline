# ADR-020: Multimodal Vision Provider Architecture & Usage Tracking

## Status
Accepted

## Context
The Aveline boutique concierge (specifically the Visual Intelligence & Sourcing Agent — Elle, Slice 2) requires automated extraction of structured fashion attributes (`category`, `primary_color`, `secondary_colors`, `pattern`, `style`, `fabric`, `suggested_keywords`) from product image URLs. These extracted visual attributes drive boutique inventory filtering, look composition, and supplier sourcing tickets.

Image analysis must satisfy three core constraints:
1. **Model Flexibility & Independence**: The vision client must support OpenAI-compatible multimodal endpoints (e.g. OpenAI `gpt-4o-mini`, self-hosted Ollama/vLLM, or Azure OpenAI).
2. **Cost & Usage Accountability (ADR-010)**: Real token spend for multimodal image processing must be tracked and billed via Blossom credits under the tenant's account.
3. **Resilience & Deterministic Offline Fallback**: In environments without active vision API credentials (local development, air-gapped CI, or provider outages), image analysis must fall back to deterministic attribute extraction rather than failing user interactions.

## Options Considered

### 1. Direct vision API calls from Python LangGraph sub-graph (`agnet-service`)
- **Pros**: Direct integration in the Python agent graph.
- **Cons**: Duplicates external credential management across services; bypasses the central .NET API client architecture; complicates integration tests and mocking across platforms.

### 2. Centralized OpenAI-compatible vision provider in .NET API (`VisionService`) with ADR-010 usage recording
- **Pros**:
  - Single point of outbound multimodal HTTP client configuration (`Vision:ApiKey`, `Vision:BaseUrl`, `Vision:Model`).
  - Native integration with `IUsageTrackerService.RecordWorkflowUsageAsync` for ADR-010 Blossom credit accounting.
  - Consistent with the architecture of `EmbeddingService` (ADR-017) and `WhatsAppService` (ADR-015).
  - Clean internal HTTP interface (`POST /internal/visual/analyze-image`, ADR-009) consumed by `agnet-service` tool registry.
- **Cons**: One internal HTTP hop between `agnet-service` and `Aveline.Api`.

## Decision

1. **Implement `IVisionService` / `VisionService` within the .NET `VisualIntelligence` module.**
   - Multimodal requests are dispatched to `{Vision:BaseUrl}/v1/chat/completions` with image URL content and `response_format: { type: "json_object" }`.
   - Configuration is resolved from `Vision:ApiKey`, `Vision:BaseUrl` (defaults to `https://api.openai.com`), and `Vision:Model` (defaults to `gpt-4o-mini`).

2. **Integrate ADR-010 Blossom Usage Tracking.**
   - Prompt and completion token counts are parsed from the response `usage` object (`prompt_tokens`, `completion_tokens`).
   - Token consumption is recorded under workflow ID `"visual-image-analysis"` via `IUsageTrackerService.RecordWorkflowUsageAsync`.
   - Usage recording is non-fatal: exceptions during usage logging are caught and logged without failing the image analysis response.

3. **Deterministic Heuristic Fallback.**
   - When no API key is configured or when the remote endpoint fails, `VisionService` executes `GenerateDeterministicAnalysis(imageUrl)` based on substring analysis (e.g., `saree`, `dress`, `silk`, `emerald`), ensuring reliable testing and offline operation.

## Consequences

- The Python agent service calls `analyze_product_image` via `ToolRegistry` through the secured `POST /internal/visual/analyze-image` endpoint.
- External vision API credentials are centrally managed in `appsettings.json`, `appsettings.Development.json`, `.env`, and `docker-compose.yml`.
- All multimodal API usage is auditable and tracked in the `ai_usage_records` table with calculated Blossom credits.
- Development and test suites remain 100% deterministic and offline-capable without requiring real OpenAI API keys.
