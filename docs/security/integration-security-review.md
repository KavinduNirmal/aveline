# Integration Security Review — Aveline

> **Issue:** #119 · **Date:** 2026-09-08 · **Scope:** WhatsApp integration gateway — tenant
> credential lifecycle, the Meta outbound provider, the public webhook surface, and the
> background health service (`Aveline.Api`).

## Methodology

- **Manual code review** of the credential encryption + AAD binding, the WhatsApp provider
  (masked logging), the webhook signature verification + verify-token challenge, the audit
  log, and the health service.
- **Automated scans** (wired into CI, see `.github/workflows/ci.yml`): `dotnet list package
  --vulnerable`, `bun audit`, Trivy, Dependabot, OWASP ZAP baseline.
- **Integration tests** exercising the real webhook endpoints
  (`Aveline.Api.Tests/WebhookEndpointsIntegrationTests.cs`) and the signature verifier
  (`WebhookSignatureVerifierTests.cs`).

## Scope Reviewed

| Component | Surface |
|---|---|
| `IntegrationCredential` + `CredentialEncryptionService` | AES-256-GCM at rest, AAD-bound to `org:type`, masked previews only |
| `WhatsAppService` | Outbound Meta calls; secrets + message bodies masked in logs |
| `WebhookEndpoints` | Public GET challenge + POST HMAC verification, audit log, `message.received` publish |
| `IntegrationHealthService` | Token expiry detection + `IntegrationExpired` notification |
| `IntegrationEndpoints` | `settings:manage`-gated save/test/delete, tenant isolation |

## Findings

### High / Critical

None identified.

### Medium (accepted trade-offs)

| ID | Finding | Rationale / Mitigation |
|----|---------|------------------------|
| SEC-M3 | Webhook endpoints are **public** (no JWT) — Meta has no Aveline token. | Authenticated by Meta's `X-Hub-Signature-256` HMAC (constant-time) against the per-org `appSecret`, plus an optional Meta IP allow-list (`Webhook:AllowedIps`) and per-org rate limiting. The GET challenge requires the per-org `webhookVerifyToken`. |
| SEC-M4 | WhatsApp **app secret** and **verify token** are stored encrypted alongside the access token. | They are encrypted at rest with the same AES-256-GCM + AAD scheme as the access token (ADR-011) and only decrypted transiently inside the webhook handler. Never returned over the API. |

### Low (documented)

| ID | Finding | Mitigation |
|----|---------|------------|
| SEC-L4 | Webhook subscription is configured manually in the Meta App dashboard (not API-automatable). | Documented in ADR-015 and the architecture doc; Aveline builds the receiving side and the owner pastes the callback URL + verify token into Meta. |
| SEC-L5 | `InboundMessageLog` stores message text in plaintext. | It is a minimal audit log for the inbound pipeline; it does not store secrets. Richer customer/memory data is a separate Customer Concierge concern. |
| SEC-L6 | Rate limiter fails open if the cache is unavailable. | Consistent with the existing `DistributedRateLimiter` behaviour; the HMAC signature check remains the primary gate. |

## Mitigations Applied During This Review

1. **Constant-time webhook signature verification** (`WebhookSignatureVerifier`) using
   `CryptographicOperations.FixedTimeEquals`; malformed/empty signatures rejected.
2. **Raw-body buffering** (`Request.EnableBuffering`) so the HMAC is computed over the exact
   bytes Meta signed.
3. **Optional Meta IP allow-list** (`Webhook:AllowedIps`) — when configured, only listed IPs
   may POST.
4. **Per-org + per-IP rate limiting** on the webhook POST (120/min) to blunt replay/abuse.
5. **Secret masking** in all WhatsApp provider logs (tokens, message bodies, phone numbers).
6. **Tenant isolation** — every credential/audit lookup filters by `OrganizationId`; the
   webhook binds to the org via the route and its own stored secret.
7. **Status state machine** so expired/error integrations are surfaced and the owner is
   notified (`IntegrationExpired`) rather than silently failing.

## Verification

- `dotnet test Aveline.Api/Aveline.Api.sln` passes (integration + unit suites).
- Webhook integration tests cover: valid/invalid verify token, valid/invalid HMAC signature,
  audit-log persistence, and non-message events.
- `dotnet list package --vulnerable`: clean for `Aveline.Api` and `Aveline.Api.Tests`.

## Residual Risk

- Meta's webhook IP ranges change; the allow-list is optional and off by default to avoid
  blocking legitimate traffic. Operators should keep it current if enabled.
- The health service runs on a 6-hour interval by default; a token can be invalid for up to
  that window before the owner is notified.

## Recommendation

No high-risk vulnerabilities remain. Keep the webhook HMAC verification as the primary gate and
treat the IP allow-list as defense-in-depth.
