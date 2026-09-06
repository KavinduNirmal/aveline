# Admin Access & Tenant Governance

Aveline provides multi-tenant administration tools to oversee boutique accounts, inspect system diagnostics, and maintain compliance standards.

---

## Requesting Administrator Privileges

Administrator access confers cross-boutique diagnostic capabilities and is governed by strict review:

1. **Submit Admin Registration:** Prospective administrators submit credentials through the dedicated admin verification portal (`/sign-up/admin`).
2. **Review & Verification:** The Aveline core security council reviews organizational credentials and domain ownership.
3. **Activation & Auditing:** Once approved, administrators obtain access to the centralized administrative portal with comprehensive audit logging enabled.

---

## Privileged Operations

Administrators have access to:
- **Tenant Health & Quotas:** Monitor Blossom token consumption, vector storage limits, and API request throughput.
- **Support Impersonation (Zero-Knowledge):** Debug workflow failures with explicit associate authorization tokens.
- **Audit Trails:** Immutable event logs recording every role elevation, authorization grant, and system configuration adjustment.

---

## Security Policies

- Multi-factor authentication (MFA) is strictly mandatory for all administrative accounts.
- Sessions automatically expire after 15 minutes of inactivity.
- Administrative operations are signed and logged to cryptographic append-only logs.
