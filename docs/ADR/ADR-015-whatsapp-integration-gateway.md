# ADR-015: WhatsApp Integration Gateway & Webhook Handling

## Status
Accepted

## Context

Boutique owners connect their own WhatsApp Business accounts so Aveline can act on their
behalf. ADR-011 established per-tenant encrypted credential storage, and ADR-014 added a
Redis Pub/Sub event bus for API–agent decoupling. What remained was the *live* integration
surface: validating credentials against Meta, receiving inbound messages, sending outbound
messages, and keeping connections healthy as tokens expire.

We need to decide how the ASP.NET Core API acts as the "integration gateway" for WhatsApp
without coupling clients to Meta, without storing plaintext secrets, and without blocking on
the agent workflow.

## Options Considered

### 1. WhatsApp provider behind an interface + typed HttpClient (chosen)
A dedicated `IWhatsAppService` (test/send/validate) backed by a typed `HttpClient` pointed at
the Meta Graph API. Clients never call Meta directly; all outbound traffic flows through the
API, which decrypts the tenant's own credentials transiently.

- Pros: Single gateway; credentials never leave the backend; easy to fake in tests; matches
  ADR-001 (monolith API owns integrations) and ADR-011.
- Cons: The API must handle Meta's token lifecycle.

### 2. Clients call Meta directly with the tenant token
- Cons: Exposes tenant secrets to clients; no central audit; breaks the "gateway" model.

### 3. Webhook "auto-configuration" via the Meta API
The original plan assumed the API could programmatically subscribe Meta to a webhook URL.
- Cons: The WhatsApp Cloud API does **not** expose a webhook-subscription endpoint; webhook
  subscription is configured manually in the Meta App dashboard. **Decision:** Aveline builds
  the *receiving* side (GET verification challenge + POST HMAC verification) and documents the
  one-time manual Meta step. This is the honest, correct approach.

### 4. Status derived from credential presence (status quo)
- Cons: Cannot distinguish "connected" from "expired"/"error"; the health service and UI need
  an explicit state machine.

## Decision

1. **Additive status lifecycle.** Extend the existing `IntegrationCredentials` table (do not
   rename) with `Status` (`Pending`/`Connected`/`Error`/`Expired`/`Disconnected`),
   `LastConnectedAt`, and `LastError`. `IntegrationService` transitions state; the DTO keeps a
   computed `Connected` convenience for backward compatibility.

2. **`IWhatsAppService` behind a typed `HttpClient`.** `WhatsAppService` implements
   `TestConnectionAsync`, `SendMessageAsync`, and token validation against the Meta Graph API.
   Secrets and message bodies are masked in logs. Registered via `IHttpClientFactory` with
   `WhatsApp:BaseUrl`/`WhatsApp:ApiVersion`.

3. **WhatsApp required keys extended** to `accessToken`, `phoneNumberId`, `appSecret`, and
   `webhookVerifyToken` so the webhook can verify signatures and answer Meta's challenge.

4. **Public webhook endpoints** at `/api/v1/webhooks/whatsapp/{organizationId}`:
   - `GET` answers Meta's verification challenge by comparing `hub.verify_token` to the stored
     `webhookVerifyToken`.
   - `POST` verifies `X-Hub-Signature-256` (HMAC-SHA256 of the raw body against the stored
     `appSecret`, constant-time), persists a minimal `InboundMessageLog`, and publishes a
     `message.received` event on the Redis event bus (ADR-014) for the agent service. Returns
     `200` immediately (non-blocking).
   - Webhook subscription itself is a documented manual Meta step.

5. **Minimal audit log.** A tenant-scoped `InboundMessageLog` records inbound/outbound messages
   for audit and the inbound pipeline. Richer customer/memory modelling is deferred to the
   Customer Concierge slice.

6. **Background health service.** `IntegrationHealthService` periodically validates connected
   WhatsApp tokens; on failure it marks the integration `Expired` and dispatches an
   `IntegrationExpired` notification to the boutique owner via the existing notification
   gateway (ADR-013).

7. **WhatsApp-first scope.** Instagram and payment-gateway providers remain encrypt-only stubs
   wired in later slices; only WhatsApp has a live provider check today.

## Consequences

- Adding a provider only requires implementing its interface and registering required keys; no
  schema change (the encrypted JSON blob stays integration-agnostic).
- Webhook endpoints are public (no JWT) but protected by HMAC signature verification, an
  optional Meta IP allow-list (`Webhook:AllowedIps`), and per-org rate limiting.
- Inbound messages are fire-and-forget on the bus; critical processing must tolerate loss
  (consistent with ADR-014).
- Operators must set `Credentials:EncryptionKey` (already required by ADR-011) and, for real
  delivery, `WhatsApp:BaseUrl`/`WhatsApp:ApiVersion` and the Meta webhook callback URL.

## Related
- [ADR-011](ADR-011-credential-encryption.md) — tenant credential encryption & isolation.
- [ADR-013](ADR-013-notification-service-architecture.md) — notification gateway used for expiry alerts.
- [ADR-014](ADR-014-redis-pubsub-event-bus.md) — event bus used for `message.received`.
- [ADR-001](ADR-001-monolith-vs-microservices.md) — single API owns integrations.
