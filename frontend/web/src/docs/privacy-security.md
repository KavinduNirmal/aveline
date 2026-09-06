# Privacy & Security

In the world of high fashion and luxury retail, client confidentiality is paramount. Aveline is architected from the ground up with a zero-leakage, tenant-isolated privacy architecture.

---

## Data Segregation & Multitenancy

Every boutique on Aveline enjoys isolated logical partitions:
- **No Cross-Tenant Retrieval:** Ava's embedding spaces and vector indexes are strictly segmented by organizational ID (`org_id`). One boutique's client data cannot ever be surfaced by another boutique's queries.
- **Client Ownership:** Your clients belong solely to your brand. Export your entire customer roster and notes at any time in standardized JSON or CSV formats.

---

## Authentication & Identity

Aveline relies on **Clerk** for robust enterprise identity management:
- **Hardware Token & Passkey Support:** Native biometric passkeys (Touch ID, Face ID) and FIDO2 keys supported on mobile and web.
- **Session Revocation:** Instantly terminate active sessions for lost devices or departed staff members with one click.
- **Role-Based Token Claims:** JWT tokens include cryptographically signed boutique tenancy claims and permission scopes.

---

## Regulatory Compliance

- **GDPR & Right to be Forgotten:** Client profile erasure requests cascade through both relational records and semantic vector stores.
- **Encryption Standards:** All data in transit is enforced with TLS 1.3; data at rest is encrypted with AES-256 encryption.
- **Subprocessors:** We rigorously vet all cloud model and embedding infrastructure providers under strict Data Processing Agreements (DPAs).
