# Integration Gateway — Architecture

> **Status:** Approved (see [ADR-015](../ADR/ADR-015-whatsapp-integration-gateway.md)).

This document describes how Aveline connects to each boutique's own WhatsApp credentials and
acts as the "integration gateway". It covers the credential lifecycle, the outbound provider,
the inbound webhook path, and the background health service.

---

## 1. Core principle

> Aveline never stores or handles third-party credentials in plaintext. Credentials are
> encrypted at rest (AES-256-GCM, ADR-011) and scoped per-organization. All integration
> traffic flows through the ASP.NET Core backend, which acts as the gateway. Clients never
> call Meta directly.

---

## 2. Components

| Component | Responsibility |
|---|---|
| `IntegrationCredentials` (table) | Per-org, per-type encrypted credential blob + lifecycle `Status`/`LastConnectedAt`/`LastError` |
| `CredentialEncryptionService` | AES-256-GCM, AAD-bound to `org:type` |
| `IntegrationService` | Save/list/test/delete + status transitions |
| `IWhatsAppService` / `WhatsAppService` | Meta outbound provider (test/send/validate) behind a typed `HttpClient` |
| `WebhookEndpoints` | Public GET challenge + POST HMAC verification, audit log, `message.received` publish |
| `InboundMessageLog` | Minimal tenant-scoped message audit log |
| `IntegrationHealthService` | Background token-expiry detection + `IntegrationExpired` notification |
| `IntegrationEndpoints` | `settings:manage`-gated save/test/delete API |

---

## 3. Connect flow (owner provides credentials)

```
Owner → Settings → Integrations (React)
        │  PUT /orgs/{org}/integrations/whatsapp  { accessToken, phoneNumberId, appSecret, webhookVerifyToken }
        ▼
ASP.NET Core
  1. Validate required keys
  2. Encrypt (AES-256-GCM, AAD org:type) and store → Status = Pending
  3. Auto-connect: IWhatsAppService.TestConnectionAsync
        ├─ success → Status = Connected, LastConnectedAt = now
        └─ failure → Status = Error, LastError = <message>
        ▼
Owner sees Connected / Error badge
```

> **Note:** Meta webhook *subscription* is configured manually in the Meta App dashboard. The
> owner pastes the callback URL (`https://<api>/api/v1/webhooks/whatsapp/{orgId}`) and the
> verify token into Meta once. Aveline builds the receiving side.

---

## 4. Inbound message flow

```
[Meta WhatsApp Cloud API]
        │  POST /api/v1/webhooks/whatsapp/{orgId}   (X-Hub-Signature-256)
        ▼
WebhookEndpoints
  1. (optional) IP allow-list check
  2. Rate limit per org + IP
  3. Verify X-Hub-Signature-256 (HMAC-SHA256 of raw body vs stored appSecret, constant-time)
  4. Parse entry[].changes[].value.messages[]
  5. Persist InboundMessageLog (audit)
  6. Publish message.received on the Redis event bus (ADR-014)
  7. Return 200 immediately (non-blocking)
        │
        ▼
[Python Agent Service]  (subscribes to aveline:*:message.received)
  - parses message, identifies customer, triggers agents, drafts response
```

---

## 5. Outbound message flow

```
[Flutter / React] (staff sends)
        │  POST /orgs/{org}/integrations/whatsapp/send   (future)
        ▼
ASP.NET Core
  1. Decrypt WhatsApp credentials
  2. IWhatsAppService.SendMessageAsync → Meta Cloud API
  3. Record outbound message
  4. Broadcast via SignalR
        ▼
[WhatsApp Cloud API] → Customer's phone
```

> Outbound send + the draft/approval "outbox" are deferred to a later slice; the provider
> method (`SendMessageAsync`) is already in place.

---

## 6. Expiry / health flow

```
IntegrationHealthService (BackgroundService, default every 6h)
        │
        ▼
for each Connected WhatsApp credential:
  1. Decrypt credentials
  2. IWhatsAppService.TestConnectionAsync
        ├─ valid   → leave Connected
        └─ invalid → Status = Expired, LastError = <message>
                     dispatch IntegrationExpired notification (SignalR + push) to owner
```

---

## 7. Configuration

| Setting | Purpose |
|---|---|
| `Credentials:EncryptionKey` | Base64 32-byte AES key (ADR-011) |
| `WhatsApp:BaseUrl` | Meta Graph base URL (default `https://graph.facebook.com`) |
| `WhatsApp:ApiVersion` | Meta Graph API version (default `v21.0`) |
| `IntegrationHealth:IntervalHours` | Health-check interval (default 6) |
| `Webhook:AllowedIps` | Optional Meta webhook IP allow-list |

---

## 8. Security

See [integration-security-review.md](../security/integration-security-review.md). Highlights:
encryption at rest + AAD binding, constant-time HMAC webhook verification, optional IP
allow-list, per-org rate limiting, tenant-isolated audit log, and secret masking in logs.
