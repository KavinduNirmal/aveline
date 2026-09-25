# Module: Privacy (consent transparency, the opt-out link, and the OTP opt-out flow)

> **Phase:** privacy/consent plan, Phases 3 and 4
> **Domain:** first-contact disclosure, opt-out link signing, OTP-proof opt-out, acknowledgement
> delivery.

## Responsibility

This module owns the customer-facing transparency surface that sits between the inbound webhook and
the outbound WhatsApp channel:

- **the permanent opt-out link** — a stateless, versioned HMAC-SHA256 link (`PrivacyLinkSigner`);
- **the first-contact disclosure body** — the versioned, four-element message (`DisclosureBodyBuilder`);
- **exactly-once delivery** — the conditional claim plus the outbound send (`DisclosureDispatchService`);
- **the background drain** — the bounded queue and worker that keep Meta off the request paths
  (`DisclosureDispatchQueue`, `DisclosureDispatchWorker`, which drains two channels);
- **the OTP proof** (Phase 4) — `OtpService` mints and verifies a six-digit code without ever storing
  it; `OtpDeliveryService` sends it; `OtpMetrics`/`PrivacyDeliveryMetrics` count it;
- **the revocation core** (Phase 4) — `ConsentRevoker` is the single writer behind both the anonymous
  OTP path and the staff path, with `IPhoneSubjectLocator` providing the cross-organisation read a
  global opt-out needs;
- **the acknowledgement** (Phase 4) — `OptOutAcknowledgementService` +
  `DistributedOptOutAcknowledgementGate`, once per 24 hours.

Export and erasure are **not** in this module (plan Phase 5).

## Folder structure

```
Privacy/
├── Jobs/        # DisclosureDispatchWorker (BackgroundService over both channels)
├── Metrics/     # DisclosureMetrics, OtpMetrics, PrivacyDeliveryMetrics
└── Services/    # link signing, body builder, queue, dispatch, OTP, revocation, acknowledgement
```

## Configuration

| Key | Meaning |
|---|---|
| `Privacy:LinkSigningKey` | base64 HMAC key, at least 32 bytes. A set-but-unusable value refuses startup (`PrivacyOptionsValidator`); absence only warns until the disclosure path is used. |
| `App:BaseUrl` | the web origin the opt-out and data-policy URLs are built from (shared with staff-invitation links). |

No new key was introduced by Phase 4.

## The links

- opt-out: `{App:BaseUrl}/privacy/opt-out?o={organizationId}&v=1&s={hmac}` — the HMAC covers
  `v1|{organizationId}`, so a tampered `o` or `v` is rejected. No phone number and no timestamp, so
  the link never expires (DR-2).
- data policy: `{App:BaseUrl}/privacy?org={slug}`.

The link proves *which boutique*; the OTP proves *which number*.

## The OTP parameters (Phase 4)

| Key | Value | Why |
|---|---|---|
| `otp:code:{handle}` | `base64(SHA-256(otp + ":" + handle + ":" + phone))`, TTL 300 s | the code itself is never stored |
| `otp:attempts:{handle}` | counter, TTL 300 s | hard cap **5**; the 6th attempt is refused even with the right code |
| `otp:send:{phoneFingerprint}` | counter, TTL 15 min | **3** sends per number |
| `otp:ip:{ipFingerprint}` | counter, TTL 1 h | **10** starts per address |

Six digits is ~19.9 bits. That is defensible **only** with the five-attempt cap, the five-minute TTL
and the send cap together; removing any one breaks the argument. Every counter fails **closed** — a
store outage is a `503`, never an allow (DR-6). `IRateLimiter` is deliberately not used; it fails
open.

## Endpoints

| Method | Path | Auth |
|---|---|---|
| `POST` | `/api/v1/privacy/opt-out/start` | anonymous; constant `202` on every path (anti-enumeration) |
| `POST` | `/api/v1/privacy/opt-out/verify` | anonymous; one `400 otp-invalid` for every failure |
| `GET`/`POST` | `/api/v1/orgs/{organizationId}/customers/{customerId}/consent` | tenant surface; the write needs `customers:manage` |

## Invariants

- The conditional `CustomerConsent.DisclosureShownAt IS NULL` update is the only thing that authorises
  a disclosure send; a failed send releases it so the next inbound message retries.
- A revoked customer is never disclosed to; the webhook consults `IConsentGateService` before
  enqueuing.
- **The customer path and the staff path must stay distinguishable by `ActorKind`** (`Customer` /
  `otp_link` vs `User` / `staff`) and by `Source`. That is the only reason two routes exist.
- A failed verification must not consume the code, and a successful one must delete it before the
  caller is told anything.
- The disclosure, revocation and acknowledgement services are **scoped** (they hold `AppDbContext`);
  the worker creates a DI scope per intent. Do not resolve them from the root scope.
- Never log a message body, an OTP, or an unmasked phone number. `ConsentAuditEntry.EvidenceJson`
  holds identifiers only, and the acknowledgement must never use `SendTemplateAsync` (policy Q-2).
