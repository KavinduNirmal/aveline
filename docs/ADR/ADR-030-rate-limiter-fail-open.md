# ADR-030: The rate limiter fails open

## Status
Accepted (documented retrospectively — security assessment F-6.3, 2026-10-03)

## Context

`DistributedRateLimiter` (Aveline.Api/Infrastructure/RateLimiting) guards the endpoints where a
client can spend someone else's resource or guess a secret:

| Endpoint | Budget |
|----------|--------|
| `POST /api/v1/orgs/{orgId}/invitations` | invitation creation per caller |
| `POST /api/v1/invitations/accept` | invitation acceptance attempts |
| Privacy OTP request / verify | per address and per phone number |

The counter lives in Redis (`IDistributedCache`) so the budget is shared across API replicas; an
in-process counter would multiply every limit by the replica count.

The implementation wraps its cache read and write in `try/catch` and **returns `true` (allow) on any
exception**, logging a warning:

```csharp
catch (Exception ex)
{
    RateLimiterMetrics.RecordFailOpen(scopeKey);
    _logger.LogWarning(ex, "Rate limiter unavailable for scope {Scope}. Failing open.", scopeKey);
    return true;
}
```

## Decision

Keep failing open. A Redis outage must not take invitations, onboarding and OTP verification down
with it. Those flows are the product's front door; refusing every request because the *throttle* is
unavailable converts a degraded cache into a total outage.

## Consequences

**Accepted cost.** A Redis outage disables every throttle simultaneously and silently. Traffic
continues, so nothing in the HTTP metrics reveals that abuse protection is off. This is an
availability-coupled weakness: the control's strength depends on a component whose failure removes
it.

**Why this is not currently a practical brute-force path.** The secrets behind each budget are
high-entropy and independently capped, so removing the IP throttle does not make guessing feasible:

| Secret | Entropy | Independent cap |
|--------|---------|-----------------|
| Privacy OTP | 6 digits from `RandomNumberGenerator` | Hard 5 attempts per code, 300 s TTL → success ≤ 5/10⁶ |
| Invitation code | 12 chars over a 32-char alphabet ≈ **60 bits** | Single use, hashed at rest |
| Device / media tokens | random, bounded TTL | Single-use nonce (media) |

Removing any one of those caps *would* make this decision dangerous. The OTP service's own comment
makes the same point: its three caps are load-bearing together.

**Observability.** Because the condition is otherwise invisible, every fail-open decision increments
`aveline.rate_limiter.fail_open`, labelled by the bounded scope prefix (never by user, phone or IP —
those would mint one series per client; see `MetricsCatalog.ForbiddenLabelKeys`). Alert on any
non-zero rate. The existing warning log is retained.

**Not atomic.** The read-modify-write counter can under-count concurrent requests. Accepted for a
brute-force guard, where a rare under-count is better than blocking legitimate traffic. This limiter
must **not** be reused for billing or quota enforcement, where an under-count is a revenue or
entitlement defect; that needs an atomic `INCR`+`EXPIRE`.

## Alternatives considered

1. **Fail closed** (refuse when the store is unavailable). Rejected: couples front-door
   availability to Redis, and a cache outage becomes a product outage.
2. **In-process fallback budget.** Rejected as the primary mechanism: per-replica budgets multiply
   the effective limit by the replica count, which is worse than a known outage because it is
   invisible in the happy path. Worth revisiting if a coarse per-replica cap is ever wanted during
   an outage.
3. **Atomic Redis `INCR`+`EXPIRE`.** Does not change the fail-open question; relevant only to the
   non-atomicity, and the current call sites do not need it.

## Revisit trigger

Reconsider if any of the following becomes true:

- a rate-limited secret drops below ~40 bits of entropy, or loses one of its independent caps;
- a billing, quota or entitlement path adopts this limiter (it must not, as written);
- Redis availability falls below the level at which an outage is an exceptional event.
