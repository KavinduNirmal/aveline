# ADR-012: Invitation Code Lifecycle (Redis store + Postgres audit log)

## Status
Accepted

## Context
Boutique owners invite staff with a one-time code (see `docs/architecture/onboarding-flow.md`).
Aveline already stores the code as a SHA-256 hash in `OrganizationInvitations`. We want codes to
be **short-lived and centrally revocable via TTL**, while keeping a durable **audit log** of who
was invited, the role granted, and the accept/revoke lifecycle.

Aveline already provisions Redis through `AddAvelineCache` (`IDistributedCache`, StackExchange
Redis with an `aveline:` instance prefix; in-memory distributed cache in dev/tests). This makes a
transient code store easy to add without new infrastructure.

## Options Considered

### Where the code lives

1. **Hybrid: transient mapping in Redis (primary) + SHA-256 hash in Postgres (fallback).**
   - Invitation create writes both: Postgres `TokenHash` (durable) and Redis `invite:code:{code}`
     → `invitationId` with a 24h TTL.
   - Accept resolves via Redis first; on a miss (flush/TTL) it falls back to the Postgres hash.
   - Pros: Fast lookups; self-expiring codes; resilient to a Redis flush because the hash fallback
     still redeems within the log's `ExpiresAt`. Postgres remains the source of truth for state.
   - Cons: Codes are (transiently) stored as plaintext keys in Redis, so Redis access must be
     protected. Code redemption must still guard against reuse via the Postgres log.

2. **Hash only in Postgres (status quo).**
   - Pros: No Redis dependency for invitations.
   - Cons: Codes never auto-expire at the cache layer; no fast transient store; owner revocation
     relies purely on DB rows.

3. **Plaintext code only in Redis, Postgres is a pure audit log (no hash).**
   - Pros: Smallest DB footprint.
   - Cons: A Redis flush silently invalidates every pending invitation with no fallback.

**Decision:** hybrid (option 1). It keeps the existing durable hash as a safety net while adding a
self-expiring Redis fast path.

### Handling a Redis outage at invite creation

Invitation codes are redeemed on the fast path through Redis. Issuing an invitation whose code
cannot be stored (and therefore cannot be redeemed promptly) is undesirable. **Decision:** fail
fast — if the Redis write throws, the service deletes the just-inserted `OrganizationInvitations`
row (compensation) and surfaces an error to the caller, who may retry.

### Brute-force protection on acceptance

Codes are 12 chars over a 32-char alphabet (unambiguous, uppercase, no I/O/0/1), so guessing is
already impractical, but rate limiting is cheap hygiene. **Decision:** add a lightweight sliding-
window limiter on `POST /invitations/accept` keyed by client IP (default 10 attempts/minute),
implemented over `IDistributedCache`. It fails open if the cache is unavailable so legitimate
sign-ups are never blocked.

## Consequences
- Invitations created today and their codes are redeemable for **24 hours** (`ExpiresAt` and the
  Redis TTL both use the same constant so they never drift).
- Revocation is **lazy**: setting `RevokedAt` on the log row is authoritative; a revoked code is
  rejected on its next accept attempt (which re-checks the log), and the Redis key is best-effort
  removed.
- Redeeming a code removes the transient mapping (one-time use); the durable log's
  `(OrganizationId, UserId)` unique membership index prevents duplicate memberships under a race.

## Related
- [ADR-003](ADR-003-database-strategy.md) — Postgres is the EF migration owner.
- [pricing_plan.md](../architecture/pricing_plan.md) — staff seats that invitations populate.
- `docs/architecture/onboarding-flow.md` §4.6 — invitation endpoints & UI.
