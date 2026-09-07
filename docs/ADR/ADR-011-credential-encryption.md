# ADR-011: Tenant Integration Credential Encryption & Isolation

## Status
Accepted

## Context
Boutique owners connect their own third-party accounts (WhatsApp Business, Instagram/Meta,
payment gateway) to Aveline so the platform can act on their behalf. Each tenant supplies
their own credentials, so Aveline must:

1. **Store credentials per-organization** (tenant isolation) — Boutique A must never read or
   overwrite Boutique B's secrets.
2. **Never persist or log plaintext secrets** — the database and logs must never leak an
   access token.
3. **Encrypt at rest** with a well-understood algorithm, using a single static master secret
   managed by Aveline operators.

Integration credentials are distinct from the platform's own global API keys (Clerk, agent
internal token) and therefore need a dedicated, tenant-scoped storage mechanism.

## Options Considered

### Encryption approach

1. **AES-256-GCM via a dedicated `CredentialEncryptionService`.**
   - Random 12-byte nonce per secret; authenticates ciphertext (detects tampering); output is
     `nonce:tag:ciphertext` hex.
   - Pros: Fast, standard, authenticated. No external key-management dependency to run locally.
   - Cons: Aveline manages key rotation itself; keys must be derived from a well-guarded master
     secret (base64 32-byte `Credentials:EncryptionKey`).

2. **ASP.NET Core Data Protection API.**
   - Pros: Built in, handles key ring + rotation on disk.
   - Cons: Designed for cookies/tokens and ephemeral payloads; key ring persisted to disk is an
     awkward fit for long-lived tenant secrets and multi-instance deployments. More implicit.

3. **External KMS (AWS KMS / Azure Key Vault) with envelope encryption.**
   - Pros: Best-in-class key handling and rotation.
   - Cons: Adds a cloud dependency and provisioning burden before the platform is production
     hosted; cannot run in the current local/demo stack.

**Decision:** AES-256-GCM with a locally configured 32-byte master key. This matches the
security requirements without a cloud dependency, and can be layered onto a KMS envelope later
by swapping the single `ICredentialEncryptionService` implementation.

### Storage model

1. **One row per `(OrganizationId, IntegrationType)` holding an encrypted secret JSON blob**
   plus optional non-sensitive `Metadata` (JSONB) such as a WhatsApp phone number.
   - The secret JSON holds whatever fields an integration needs (e.g. Instagram
     `clientId`/`clientSecret`/`accessToken`), so the schema stays integration-agnostic.
   - Every repository method filters by `OrganizationId`, enforcing tenant isolation in the
     data layer.

2. **Separate column per secret field.**
   - Cons: Each new integration or field requires a migration; more plumbing.

**Decision:** One encrypted JSON blob per `(OrganizationId, IntegrationType)`.

### Where integration secrets are surfaced

- Status endpoints return a **masked preview only** (`****Wxyz`) — never plaintext.
- Only the backend `IntegrationService.GetCredentialsAsync` decrypts, and only transiently for
  outbound calls / the agent workflow. It is never returned over the API.

## Consequences

- Adding an integration only requires registering its required-key schema in
  `IntegrationService`; no schema change.
- Operators must set `Credentials:EncryptionKey` (base64, 32 bytes) in every environment.
  Rotation means re-encrypting stored blobs with a new key.
- Credentials are routed through the .NET API only — Flutter/React never call third parties
  directly, consistent with ADR-001 (monolith API) and the internal-service-auth model.

## Related
- [ADR-001](ADR-001-monolith-vs-microservices.md) — single API owns the business data & EF migrations.
- [ADR-009](ADR-009-internal-service-authentication.md) — internal service auth pattern.
- [pricing_plan.md](../architecture/pricing_plan.md) — plan features (WhatsApp etc.) that consume these credentials.
