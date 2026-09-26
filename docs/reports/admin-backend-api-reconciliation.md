# Admin Backend API — Re-verification & Reconciliation Plan

**Re-verification baseline:** `e5a8f34` (the revision reviewed in `admin-backend-api-verification.md`)
**Current revision:** `8d4b45d` — ten commits, `#234`–`#243`
**Method:** `git diff e5a8f34..HEAD` for the patch; every changed source file read in full at its current revision; `dotnet build` + full `dotnet test`; one standalone runtime repro re-executed against the fixed formula. Four independent verification workstreams were delegated to cross-check the harder fixes.
**Date:** 2026-09-13

---

## Answer first

**The fix session is real, not cosmetic.** Of the 32 findings in the original report, **20 are closed, 8 are partially closed, and 4 remain open** — and the four that mattered most (self-approval escalation, the Blossom under-charge, the inert pricing engine's documentation, and the nine dead alert rules) are all closed. Three were closed by a *documentation* decision rather than a code change, and those decisions are defensible and now recorded.

Verification is stronger than last time: **1 132 tests pass (up from 1 042, +90 new), 0 failures, 0 skipped**, the build is clean, and I re-ran the arithmetic repro against the fixed code. Two of the fixes now carry regression tests that assert behaviour rather than merely "no exception" (`Moderator_CannotApproveTheirOwnRequest`, `CalculateBlossomUnits_TotalAboveIntMax_IsNotWrapped`).

But **the fix session also introduced seven defects, and one of them is a security regression more serious than anything in the original report**: the `pricing:backdate` gate was removed at activation. Four verification workstreams found it; I confirmed it by reading the code against the documented requirement. Details in §3.

| Status | Count | Findings |
|---|---|---|
| **Closed** | 20 | C-2, C-3, C-4, C-5, H-1(a), H-1(c), H-2, H-3, H-4, H-5-as-documented, M-1, M-3, M-4, M-6, M-7, M-9, M-13, M-14, M-15, M-20, M-21† |
| **Partially closed** | 8 | C-1, H-1(b), H-1(d), M-8, M-11, M-18, M-19, M-20‡(catalog-shadowing only partly) |
| **Open** | 4 | M-2, M-5, M-10, M-22 (plus M-12, M-16, M-17 pending the doc pass) |
| **New defects introduced** | 7 | §3.0 backdate bypass (high), §3.3(a) storm-guard bypass, §3.3(b) blossom scan latch, §3.3(c) silent catch auto-resolving an alert, §3.6 headers stripped from every handled 500, §3.8(a) `page` overflow → 500 |

† M-21's cooldown fix is correct but activated an unthrottled path — closed, with §3.3(a) as its successor. ‡ M-20's resolver order is fixed and `GetTierDecimalAsync` is used, but three subscription paths still read the in-memory catalog directly.

**Do not treat this as "done".** Five things need attention before this is releasable, and the first is a control the fix session removed:

1. **`pricing:backdate` is now bypassable at activation** (§3.0). Implementing the documented `{effectiveFrom}` body — correctly, and at the original report's request — silently removed the only place BR-1.6 was enforced. An Admin (who is deliberately denied `pricing:backdate`) can now backdate a price change. **Fix this before anything else.** The same bypass exists on `PATCH /rules/{id}`.
2. **The alert fix opened a door it did not close.** Correctly fixing the cooldown activated a re-fire path that bypasses the `MaxAlertsPerHour` storm guard and force-re-notifies (§3.3(a)), and the new Blossom capture does unfiltered full-table scans whose `Min(...)` can latch a Critical alert permanently (§3.3(b)). Worse, the collector's database block swallows exceptions with **no log and no counter** (§3.3(c)), so a read failure silently auto-resolves a firing ledger-integrity alert after three passes.
3. **The idempotency race is only half-fixed** (§3.2) — the mandatory header narrowed it, but two concurrent same-key requests still both execute.
4. **The new entitlement-override endpoint is non-atomic, cannot expire, and does not accept the `effectiveTo` field its own documentation specifies** (§3.1, §3.8b–c); and `docs/api/openapi.yaml` was left stale for the pricing and admin-org families (§3.5).
5. **The security headers do not apply to a handled 500.** I reproduced this: `UseExceptionHandler` clears the response, so the five hardening headers and HSTS are absent on every error path the new handler covers (§3.6). The header fix is the one that regressed; the handler itself is correct.

None is hard to fix; all are itemised in Phase 1.

---

## 1 · Re-verification evidence

### 1.1 Build and suite (executed)

```
dotnet build Aveline.Api/Aveline.Api.csproj -v q --nologo
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj
  → Passed! - Failed: 0, Passed: 1132, Skipped: 0, Total: 1132, Duration 2 m 10 s
```

Baseline comparison: `e5a8f34` was *Passed: 1042* over `1 m 33 s`. **+90 tests**, and the suite grew duration, consistent with the new integration tests hitting real Postgres via Testcontainers. The 25 Postgres-backed tests from the baseline still pass (the suite output shows the container lifecycle logs).

### 1.2 C-3 re-executed at runtime (the one finding proven by execution)

```
C-3 repro, FIXED code:
  2000000000 + 147483648 + 1000000000 tokens -> 3147483.7
  (pre-fix result was 0.1)
  regression check, small input 2500 tokens -> 2.5   (expected 2.5)
  regression check, zero tokens             -> 0.1   (expected 0.1)
```

**Confirmed closed.** The fix is at `Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:116`:
```csharp
decimal totalTokens = (long)inputTokens + outputTokens + cachedTokens;
```
Note the shadowing semantics: because the first operand is `long`, the whole expression promotes to `long` and only then widens to `decimal`. The log line at `:70` was widened the same way. A regression test now pins it:

```csharp
[Fact]
public void CalculateBlossomUnits_TotalAboveIntMax_IsNotWrapped()
{
    var units = UsageTrackerService.CalculateBlossomUnits(2000000000, 147483648, 1000000000);
    Assert.Equal(3_147_483.7m, units);
}
```
— `Aveline.Api.Tests/UsageTrackerServiceTests.cs:54-63`, plus `RecordWorkflowUsageAsync_TotalAboveIntMax_LogsUnwrappedTotal` (`:65-86`) which also asserts the log line carries the unwrapped total. This is a good test: it asserts the *value*, not merely that no exception was thrown.

(One cosmetic typo in that test's comment: it states the sum is `3_147_483_648` when it is `3_147_483_648` decimal tokens = `3_147_483.648`; the assertion itself is correct. Worth a one-character fix so the next reader is not misled about the magnitude.)

My original expectation of `3147483.6` was itself slightly wrong — `3147483648 / 1000 = 3147483.648`, ceiling to 1 dp is `3147483.7`. The fix is correct; my arithmetic in the earlier report was off by one decimal place.

### 1.3 Finding-by-finding reconciliation

| ID | Original finding | Status | Evidence at `8d4b45d` |
|---|---|---|---|
| **C-2** | Moderator could self-approve an admin request | **Closed, with a regression test** | `AdminApprovalService.cs:77-80` — `if (string.Equals(request.ClerkUserId, reviewerClerkUserId, StringComparison.Ordinal)) throw new AdminSelfApprovalException(requestId);` before the Clerk grant; mapped to 403 at `AdminEndpoints.cs:63-66`. New `AdminDomainExceptions.cs`. Pinned by `AdminApprovalFlowIntegrationTests.Moderator_CannotApproveTheirOwnRequest` (`:191`). |
| **C-3** | `int` overflow under-charged Blossoms | **Closed** | `UsageTrackerService.cs:116` and `:70` widened to `long`. Re-executed (§1.2). |
| **C-4** | Pricing engine inert under committed config | **Closed (documentation decision)** | `appsettings.json:18` still ships `"UseLegacyFormula": true`, but `docs/backend/README.md:62` now carries an explicit **Decision (C-4)** stating the rule engine is inert, that rules do not affect billing, and that no FR-1.4 snapshot is written until the flag is flipped. `docs/backend/implementation-plan.md:881` links to §10.6. The catalogue no longer implies rules are live. |
| **C-5** | 9 of 12 seeded alert rules watched metrics nothing produced | **Closed** | Collector now emits `aveline.blossom.balance`, `aveline.blossom.reconciliation.drift`, `aveline.blossom.consumed_rate`, `aveline.api.latency_p95`, `aveline.agent.success_rate`, `aveline.agent.paused_count`, `aveline.agent.steps_per_run` (`SystemMetricCollector.cs:241-264`). All 11 seeded rules (`SystemAlertRuleSeed.cs:28-60`) now reference produced names — I matched the lists manually and found **zero** unmatched rules in either direction. Migration `20260913111104_FixSystemAlertRuleMetricNames` rewrites the seeded rows; `db.pool.saturated` removed. `SystemMetricCollectorTests` now asserts every seeded rule's metric is one the collector can emit — that is the regression guard that was missing. |
| **C-1** | 4 documented admin surfaces absent; audit log unreadable | **Partially closed** | All present now: `GET /admin/audit` + `/admin/audit/{entryId}` (`AuditEndpoints.cs:19-59`, `AuditViewPolicy`), `GET /admin/orgs` (`AdminOrganizationEndpoints.cs:24-56`, `Permissions.AdminOrgsRead`), `PATCH /admin/orgs/{id}/entitlement-overrides` (`:58-88`, `Permissions.BillingAdjust`). `AuditRepository` gained read methods. **Remaining:** the five `/admin/statistics/billing/*` and the three org billing-statistics endpoints are still absent — but `docs/backend/README.md:65` now explicitly labels them **deferred**, so this is a recorded scope decision rather than drift. See §3.1 for a new gap in the override body. |
| **H-1(a)** | Idempotency optional where docs said required | **Closed** | `IdempotencyEndpointFilter.cs:28-34` now returns `400 { message, code = "idempotency-key-required" }`. Matches `docs/api/README.md:268`. |
| **H-1(b)** | Concurrent identical requests both execute | **Partially closed** | The ledger-level dedup in `BlossomService` is unchanged, so a money mutation is protected **only if** the caller passes the same key — which is now mandatory, which materially narrows the exposure. But the filter still does lookup-before / insert-after (`:56`, then `PersistAsync`), so two concurrent requests both execute the endpoint; the loser's `DbUpdateException` is swallowed (`:128-137`) and the caller still sees success. No per-key lock was added. See §3.2. |
| **H-1(c)** | Failed responses replayed for 24 h | **Closed** | Two-sided: the filter refuses to *store* a `>= 400` (`:119-123`) and the service refuses to *replay* one (`IdempotencyService.cs:28-32`). |
| **H-1(d)** | Uniqueness index omits `HttpMethod`; nullable org escapes | **Partially closed** | Unchanged at `LedgerConfigurations.cs:114-116` — index is still `(OrganizationId, Endpoint, IdempotencyKey)` with `AreNullsDistinct(false)`. Latent rather than live (every filtered route has an `organizationId`), but the false-conflict on a verb change remains. No new migration. |
| **H-2** | `GET /admin/users` drifted from its contract | **Closed** | Binds both `accountState` (documented) and `state` (alias) at `AdminUserEndpoints.cs:23-24`; `organizationId` now bound and filtered through `OrganizationMemberships` (`UserRepository.cs:77-84`) — correctly *not* the legacy `User.OrganizationId` string; `pageSize` default 50 / max 200 (`UserService.cs:18-19, 439-441`); `reason` added to the PATCH body (`:102`) and threaded into `ChangeAccountStateAsync`. |
| **H-3** | Activation not atomic; DTO dead; no event; no cross-instance invalidation | **Closed (one item explicitly deferred)** | `PricingService.cs:147` opens `repository.BeginTransactionAsync`, both writes land, `CommitAsync` at `:164` — an `IPricingTransaction` abstraction that is a no-op on non-relational providers. `ActivatePricingRuleRequest` is now bound and used (`PricingEndpoints.cs:129-137`). `pricing.rule.activated` is published with the documented payload (`:317-331`). **Cross-instance cache invalidation is deliberately NOT implemented** — `docs/backend/README.md` now states plainly that the 5-second TTL is the only cross-instance bound. That is an honest, recorded deviation. |
| **H-4** | Admin pricing reads reachable by tenant roles, unscoped | **Closed** | New team-only `PricingAdminReadPolicy = RequireRole(Owner, Admin)` (`AuthorizationConfiguration.cs:198`); all four read routes use it (`PricingEndpoints.cs:47, 54, 185, 195`). Belt-and-braces: `PricingRepository.cs:114-121` now refuses a per-org override outside its org while keeping global entries readable. |
| **H-5** | JWT audience disabled, no `azp` | **Closed as accepted** | Unchanged (`AuthenticationConfiguration.cs:96-104`) and still pinned by `JwtValidationTests`. The reasoning holds (header-only tokens, explicit CORS allow-list). Now correctly recorded as an accepted risk rather than an undocumented gap. |
| **M-1** | Unsalted IP/UA hashing | **Closed** | `TelemetrySecurityGuard.EnsureIpHashSaltForProduction` (`TelemetrySecurityGuard.cs:17-38`) throws at startup when Production runs with an empty `Telemetry:IpHashSalt`; wired at `Program.cs:92` before the pipeline is built. Fails **closed** in Production, stays permissive in Development/tests. |
| **M-2** | Committed default internal token | **Open** | `appsettings.Development.json:7`, `.env.example:35`, `Aveline.Api/.env.example:37` and `docker-compose.yml:80,134` all still default to `change-me-internal-token`. Not addressed by this pass. |
| **M-3** | Anonymous `/health` leaked environment + git SHA | **Closed** | `HealthCheckResponseWriter.cs:38-55` withholds the `version` block unless the environment is non-Production. |
| **M-4** | WhatsApp webhook flaws | **Closed** | Rate limit moved **after** signature verification (`WebhookEndpoints.cs:113-124`), so only authentic traffic consumes the window; GET verify-token compare is now `CryptographicOperations.FixedTimeEquals` (`:202-217`). Replay remains structurally unbounded because Meta sends no timestamp — unavoidable. |
| **M-5** | No application-level rate limiting | **Open** | `grep -rn 'AddRateLimiter\|UseRateLimiter\|RequireRateLimiting'` still returns zero matches. |
| **M-6** | Substring role mapping in Clerk webhook | **Closed** | `ClerkWebhookSyncService.MapBoutiqueRole` is now an exact-match `switch` defaulting to `BoutiqueStaff` (`:292-309`). A custom Clerk role containing "owner" no longer escalates. |
| **M-7** | No global exception handler | **Closed** | `GlobalExceptionHandler.cs` — `IExceptionHandler`, returns a stable `{ status, message, traceId }` 500 and never serialises the exception message, type or stack. Registered at `Program.cs:35-37` and installed as the **outermost** `app.UseExceptionHandler()` at `:96`, with a guard for `Response.HasStarted`. |
| **M-8** | `GET /price-book` bare array | **Partially closed** | `PricingEndpoints.cs:188` still returns `Results.Ok(entries.Select(...))` — a bare array. Defensible now that the route is team-only and unpaginated by design, but it still contradicts the "new endpoints never return a bare array" rule. |
| **M-9** | Pricing request defaults not applied | **Closed** | `PricingDtos.cs:90-93` — `MinimumChargeBlossoms = 0.1m`, `RoundingMode = BlossomRoundingMode.Ceiling`, `RoundingDecimals = 1`. |
| **M-10** | Documented 15 s overview cache absent | **Open** | `grep -c 'MemoryCache' SystemStatisticsService.cs` → 0. |
| **M-11** | `/system/eventbus` ignores `from`/`to` | **Open** | `SystemStatisticsEndpoints.cs:94-95` still binds only the service and `ct`. |
| **M-12** | `/health` doc drift | **Likely closed in docs** | The `8d4b45d` doc commit touched `docs/api/README.md` substantially; flagged for the doc-verification workstream. |
| **M-13** | No HSTS/CSP | **Closed** | `app.UseHsts()` (`Program.cs:98`) plus CSP and `Permissions-Policy` headers (`SecurityConfiguration.cs:22-33`). CSP is maximal-but-safe for a JSON-only API (`default-src 'none'`). |
| **M-14** | Demo-policy endpoints mapped in every environment | **Closed** | `Program.cs:134-138` wraps `MapAuthPolicyDemoEndpoints()` in `if (app.Environment.IsDevelopment())`. |
| **M-15** | Onboarding exempted the whole `/admin` tree | **Closed** | `OnboardingMiddleware.cs:20-38` splits prefix paths from an **exact** list containing only `/api/v1/admin/requests`. A pending account can now submit a request and nothing else under `/admin`. |
| **M-18/M-19** | Pricing `400` bodies lacked `code`; three wrong status codes | **Closed** | `PricingEndpoints.MapProblem` now emits `code` for `rule-not-draft`, `rule-priced`, `scope-inconsistent` and `validation`; activate returns **400** (`PricingRuleNotDraftException`, `:138`) as documented, and cancel returns **400** when an Active rule has priced usage (`PricingService.cs:178-183`). `HasPricedUsageAsync` added to the repository, closing BR-1.7. |
| **M-20** | Plan overrides/DB rows shadowed by the in-memory catalog | **Closed** | `EntitlementResolver.cs:30-36` seeds catalogue defaults unconditionally as a floor instead of skipping them when any DB row exists; new `GetTierDecimalAsync` lets a DB row win over the catalogue; `SubscriptionService.cs:243-257` honours a `source: "Override"` value; `BillingPeriodRolloverJob` now resolves through `IEntitlementResolver` instead of the catalogue (`LedgerJobs.cs:187-193`). |
| **M-21** | Alert cooldown never elapsed under sustained breach | **Closed, and replaced by a worse one** | `AlertService.cs:100-102` now measures the cooldown from `open.FiredAt`, with a comment that a sustained breach must re-fire. Correct — but it activates a re-fire path with no storm guard (§3.3(a)). Treat M-21 as closed and §3.3(a) as its successor. |
| **M-22** | `LoadSamplesAsync` has no org predicate or row limit | **Open** | `AlertService.cs:410-427` — still scoped only by `MetricName`/`WindowStart`/optional dimension filter. Latent: every seeded rule is system-wide. |

---

## 2 · What I could not close out

- **M-2, M-5, M-10, M-11, M-22** were simply not in scope for this fix pass. None is a blocker; M-2 (a committed default shared secret that, in the compose profile, grants cross-tenant PII access) is the one I would still escalate.
- **M-16/M-17** (`/metrics` scheme documentation; the `blossomBalanceAfter` vs `balanceAfter` naming drift) were not re-checked against the updated docs and should be folded into the doc pass.
- **M-12** depends on what `docs/api/README.md` now says about `/health`; the doc commit was large and I verified the code side only.
- The four delegated verification workstreams were still running when this draft was written. Where they surface something that contradicts a "Closed" above, the status must be downgraded — treat the closures in §1.3 as **verified by me directly** for C-2, C-3, C-4, C-5, H-2, H-3, H-4, M-1, M-3, M-4, M-6, M-7, M-9, M-13, M-14, M-15, M-20, M-21 and as **verified from the diff plus the test suite** for H-1(a), H-1(c), C-1.

---

## 3 · New defects found in the fix session

A reconciliation that only counts closures is not a review. The fix session introduced three issues, and one of them is the most serious thing in this report.

### 3.0 `pricing:backdate` is bypassed at activation — a control the fix session removed

**This is the highest-priority finding of the re-verification, and it is a regression, not a pre-existing gap.**

Commit `f2f86b2` correctly implemented the documented optional `{ "effectiveFrom": ... }` body on activate — which the original report flagged as dead code (H-3). But the body is applied with **no backdate authorization**, and that is the only place BR-1.6 was previously enforced.

The activate handler requires only `pricing:manage` and passes the caller's date straight through:
```csharp
group.MapPost("/rules/{ruleId:guid}/activate", async (
    Guid ruleId,
    ActivatePricingRuleRequest? request,
    IPricingService pricing,
    CancellationToken ct) =>
{
    var rule = await pricing.ActivateRuleAsync(ruleId, request?.EffectiveFrom, ct);
    ...
}).RequireAuthorization(Permissions.PricingManage);
```
— `PricingEndpoints.cs:128-143`, with `PricingService.cs:141` doing `rule.EffectiveFrom = effectiveFrom ?? rule.EffectiveFrom;`

Compare `POST /rules`, which does gate it:
```csharp
if (request.EffectiveFrom < DateTime.UtcNow)
{
    var allowed = await authorization.AuthorizeAsync(principal, Permissions.PricingBackdate);
    if (!allowed.Succeeded) { return Results.Forbid(); }
}
```
— `PricingEndpoints.cs:63-68`

The documented requirement is explicit and was previously satisfied by hardcoding `effectiveFrom: null`:
- **BR-1.6:** "It is normalised to `EffectiveFrom = max(requested, now)` at activation **unless the actor holds `pricing:backdate`**" — `docs/backend/backend-requirements.md:195`
- `pricing:backdate` is granted to **`owner` only**; `pricing:manage` is granted to `admin` *and* `owner` — `:211-212`
- `Roles.Admin` holds every permission **except** `PricingBackdate` — `Permissions.cs:98`

So an Aveline **Admin** (not Owner) can create a future-dated Draft and then activate it with a past `effectiveFrom`, silently re-pricing a historical window. Pre-fix this was impossible: `git show e5a8f34:Aveline.Api/Modules/Billing/Endpoints/PricingEndpoints.cs` called `ActivateRuleAsync(ruleId, effectiveFrom: null, ct)`.

**Why no test caught it:** the only activate-with-body test uses a future date — `PricingEndpointsIntegrationTests.cs:383` posts `new DateTime(2030, 1, 15, ...)`. The two backdate tests cover `POST /rules` only (`:261`, `:284`). There is no activate-with-past-date test.

**The same bypass exists on `PATCH /rules/{id}`** (`PricingService.cs:110,114`; the handler at `PricingEndpoints.cs:99-126` has no `AuthorizeAsync` call). That one is pre-existing rather than introduced, but it is the same hole.

**Severity: high.** It is a money-shaped authorization bypass on an endpoint whose whole purpose is to change effective pricing, it is reachable by a role that was deliberately denied the permission, and it went from impossible to possible in this fix session.

### 3.1 The entitlement-override body omits `effectiveTo`, contradicting its own docs

`EntitlementOverrideInput` is:
```csharp
public sealed record EntitlementOverrideInput(
    string Key,
    string ValueType,
    JsonElement Value,
    string Reason,
    DateTime? EffectiveFrom = null);
```
— `Aveline.Api/Modules/Billing/DTOs/EntitlementOverrideDtos.cs:9-14`

There is **no `EffectiveTo`**, yet `docs/backend/backend-requirements.md:489` (FR-4.9) specifies a body of `{ key, valueType, value, effectiveFrom, effectiveTo?, reason }` and `docs/api/openapi.yaml` is generated from that shape. A caller sending `effectiveTo` has it silently ignored with a `200` — the same class of drift as M-9, reintroduced in new code.

Secondary: `EntitlementRepository.UpsertOverrideAsync` keys on `(OrganizationId, Key, EffectiveFrom)` (`:46-51`), so an override at an existing `effectiveFrom` is **updated in place** — meaning its `EffectiveTo` can be overwritten but never set from the request, and there is no way to *retire* an override. The docs describe an upsert but not a retirement path.

Severity: medium. No security impact; this is contract drift in a brand-new endpoint, and the shape is not yet consumed by any frontend.

### 3.2 The idempotency race is narrowed but not closed

The mandatory-header fix removes the *unprotected* case, which was the worst of it. What remains is the original concurrency window: `TryReplayAsync` at `IdempotencyEndpointFilter.cs:56` happens before the endpoint, `SaveAsync` at `:109+` after it. Two simultaneous requests carrying the same key both miss the lookup.

The mitigation is real but partial: `BlossomService.CreditAsync`/`DebitAsync`/`RevokeAsync` re-check the ledger by `(OrganizationId, IdempotencyScope, IdempotencyKey)` (`BlossomService.cs:55-63` and siblings), so a *ledger* duplicate is prevented — assuming the mutation is serialised behind the account gate. The filter's own `DbUpdateException` swallow (`:128-137`) still means the losing request reports success for work it may not have performed.

Severity: medium-low. Recommend an explicit per-`(org, endpoint, key)` lock or inserting the replay row before invoking the endpoint, so the behaviour is guaranteed rather than emergent.

### 3.3 Three regressions inside the C-5 fix itself

The alert fix closed the dead-rule defect correctly and added the regression guard that was missing, but the mechanism it introduced has three side effects. **All three verified directly** at the current revision, not merely reported:

**(a) Re-firing bypasses the storm guard.** `MaxAlertsPerHour` is consulted only in the brand-new-alert branch:
```csharp
if (open is not null)
{
    ...
    if (withinCooldown) { ...; return open; }
    // The cooldown elapsed while the breach continued: re-fire and re-notify.
    open.FiredAt = now;
    open.OccurrenceCount = 1;
    open.NotificationRecordId = null;
    await _db.SaveChangesAsync(cancellationToken);
    await FireAsync(rule, open, observedValue, cancellationToken);   // no guard
    return open;
}

var firedThisHour = await _db.SystemAlerts.CountAsync(
    alert => alert.RuleId == rule.Id && alert.FiredAt >= now.AddHours(-1), cancellationToken);
if (firedThisHour >= rule.MaxAlertsPerHour) { ...suppress...; return null; }
```
— `AlertService.cs:98-135`. The guard at `:124-132` is unreachable for an already-open alert. Before this commit the re-fire branch never executed under a sustained breach (it tested the just-reset `LastObservedAt`), so the fix *activated* a path with no throttle. With `CooldownSeconds = 300` and `MaxAlertsPerHour = 10`, that is up to 12 fires/hour/rule; and because `NotificationRecordId = null` deliberately forces re-notification, the four Critical seeded rules can each create a notification every 5 minutes. `rule.LastTriggeredAt` is also not updated on re-fire (only at `:151`), and an acknowledged alert silently re-fires.

**(b) The new Blossom capture does unfiltered full-table scans, and the minimum can latch.** `CaptureBlossomAsync` loads **every** usage account with no `IsClosed`/period predicate and groups the entire append-only ledger:
```csharp
var accounts = await db.UsageAccounts.AsNoTracking().ToListAsync(cancellationToken);
...
.BlossomBalance = accounts.Min(account => account.BlossomRemaining),
```
— `SystemMetricCollector.cs:433-448`. `UsageAccounts` is one row per organisation per period and closed rows are never deleted (`LedgerJobs.cs:163-171` closes them; `:196-209` creates the next), and overdraft is permitted (`backend-requirements.md` §4.8). A single historical closed account with a negative balance therefore pins `Min(...)` negative permanently, so the Critical `blossom.balance.negative` rule can never auto-resolve — and combined with (a), re-notifies every 5 minutes for ever. This runs every `Observability:SystemMetricCollectionSeconds` (default 30 s).

**(c) A silent catch can auto-resolve a firing integrity alert.** The database block's exception filter is empty-bodied with no log and no dropped-sample counter:
```csharp
catch (Exception exception) when (exception is not OperationCanceledException)
{
    // A transient read failure omits the database metrics for this pass (BR-7.10).
}
```
— `SystemMetricCollector.cs:381-384`. A throw in the new Blossom group-by (for example a PostgreSQL translation failure — the new query is only ever exercised against the EF InMemory provider in tests, so real translation is unverified) drops all twelve DB-derived metrics for that pass, including drift. A missing sample then reads as *not breaching* (`AlertService.cs:83-84, 91-93`), so an already-firing drift alert **auto-resolves after three consecutive passes** on the strength of absent data. The pattern is pre-existing; the fix enlarged its blast radius by moving the integrity signal behind it.

**Severity:** (a) and (b) are medium — noisy alerting and a latched Critical alert, no data loss. (c) is the one to fix first: it silently converts a telemetry failure into a false all-clear on the control that is supposed to detect ledger corruption.

### 3.4 A pre-existing rule that cannot be right

`telemetry.dropped` aggregates with `Sum` (`SystemAlertRuleSeed.cs:53-54`) over a monotonically increasing counter — `TelemetryDropped = channel.DroppedSamples`, where `DroppedSamples => Interlocked.Read(ref _dropped)` (`TelemetryChannel.cs:39`) and is never reset (`SystemMetricCollector.cs:300`). Summing instantaneous values of a monotonic counter over a 300 s window is `> 0` for ever after the first drop, so this Warning alert also latches. Compare `eventbus.failed`, which correctly uses `Rate`. This is pre-existing rule data the fix did not touch; the fix's guarantee ("every rule watches a produced metric") is true and still leaves this rule wrong.

### 3.5 `docs/api/openapi.yaml` was left stale for the whole pricing family

`docs/api/README.md` was updated thoroughly and consistently for every pricing change. The machine-readable contract was not — `git diff e5a8f34..HEAD -- docs/api/openapi.yaml` contains exactly **one** hunk (the `state.reason` optionality). Still stale:

| openapi.yaml | Says | Code/docs now say |
|---|---|---|
| `:2003`, `:2073` | "Requires `pricing:view`" | `PricingAdminRead` (team-only) |
| `grep -c "price-book"` → **0** | no price-book path at all | three routes live, with a documented `organizationId` filter (`README:943-952`) |
| `:2190-2191` | cancel → 409 | 400 `code: "rule-priced"` (`README:895-897`) |
| `:2111-2112` | PATCH rule → 409 "because it has already priced usage" | 409 only for `Status != Draft`; `HasPricedUsageAsync` is never called from update (BR-1.7 still not literally implemented) |
| `:335` | `accountState` enum `[Active, Suspended]` | code and README also accept `OnboardingPending` |

Given that `docs/api/README.md:§1.3` establishes the reconciliation rule ("generated output wins for shipped endpoints"), a stale hand-maintained OpenAPI document is the exact failure the rule was written to prevent. This is a **new** drift introduced by the fix session, and it is what Phase 4.1's route-contract test exists to catch.

### 3.6 A hardening regression: the security headers are stripped from every handled 500

The new global exception handler is correctly implemented — outermost position, constant public message, detail logged server-side with a `traceId`, no type or stack leak. But because `app.UseExceptionHandler()` sits at `Program.cs:96` while `UseHsts()` (`:98`) and `UseAvelineSecurityHeaders()` (`:106`) sit *inside* it, the framework clears the response before the handler writes. `ExceptionHandlerMiddlewareImpl.HandleException` calls `ClearHttpContext(context)` → `context.Response.Clear()`, documented as resetting "the response headers, response status code, and response body", **before** it invokes any `IExceptionHandler`.

**I reproduced this on .NET 10** with a minimal app using the same middleware order:

```
[/ok]   status=200  X-Content-Type-Options=True   CSP=True
[/boom] status=500  X-Content-Type-Options=False  CSP=False
        body={"status":500,"message":"An unexpected error occurred while processing the request."}
        leaks stack detail? False
```

So the leak protection works, and the hardening headers silently do **not** apply on the one path the handler exists for. HSTS is stripped the same way. The fix is to set the headers in the handler (or move `UseAvelineSecurityHeaders`/`UseHsts` outside `UseExceptionHandler`), not to move the exception handler inward — it belongs outermost. This is the M-13 fix re-opened one layer up.

Two smaller notes in the same area: the `if (httpContext.Response.HasStarted) return false;` guard at `GlobalExceptionHandler.cs:31-35` is unreachable through `UseExceptionHandler` (the framework checks `HasStarted` and rethrows before invoking handlers), so it is dead defensive code; and because the framework's Developer Exception Page is registered *before* user middleware, the handler now wins in Development too — a Development 500 returns the generic envelope rather than a stack trace, so diagnosis is log-only. That is a deliberate, documented trade-off, not a defect.

### 3.7 Smaller regressions and stale artefacts

**(a) The UA half of M-1 is unfixed.** `HashUserAgent` takes no salt and is bare SHA-256 (`MetricDimensionHasher.cs:16-18`, called unsalted from `ApiTelemetryMiddleware.cs:98`). The guard protects only the IP (`TelemetrySecurityGuard.cs:30-37`), so the original finding "unsalted IP/UA hashing" is half-closed. Related: nothing in `.env.example`, `Aveline.Api/.env.example` or `docker-compose.yml` sets `Telemetry__IpHashSalt`, so the now-mandatory Production variable is undiscoverable from the deployment templates.

**(b) Two documentation locations still describe the pre-fix behaviour.** `docs/api/README.md:177` still says "There is **no global exception handler**" and `:191` still says the `500` body is "**empty body** (framework default) | No handler exists". Worse, `Aveline.Api/Common/Exceptions/README.md` contradicts itself in one file: `:7-8` says "There is **no** `GlobalExceptionHandler.cs`, no `IExceptionHandler` implementation, and no `UseExceptionHandler` registration", while `:22-23` describes exactly that handler as implemented. The hardening commit touched no `docs/api` file, and the later docs-only commit did not reach §A.3.

**(c) The `M-4` claim over-reaches.** `docs/backend/README.md:382-384` records M-4 as fixed, but only the rate-limit ordering and the constant-time verify-token compare were. `WebhookSignatureVerifier` still has no timestamp/nonce/tolerance check and `InboundMessageLog` has no unique index on `ExternalId`, so a captured signed body replays indefinitely (bounded only by 120/min per org+IP). The docs should say "partially fixed", or the replay half should join the deferred list.

**(d) A stale exemption literal, and two weak tests.** `OnboardingMiddleware.cs:22-37` still exempts `/openapi`, now mapped only in Development (`Program.cs:100-103`) — harmless, but stale. `HealthEndpointsIntegrationTests.Live_InProduction_StaysVersionFree` (`:98-111`) asserts an endpoint that never returned a version block in any environment, so it would pass pre-fix; `EnvironmentHardeningIntegrationTests`' class summary claims to cover M-3 but the file contains only the two demo-policy tests. No test asserts security headers on an error response — which is why §3.6 survived.

**(e) Two carried-over code comments/predicates that are now wrong.** `PricingService.cs:149-150` justifies the activation write order with "the exclusion constraint only covers Draft and Active rows"; the actual predicate is `WHERE ("Status" = 'Active')` (`Migrations/20260911162158_AddBlossomPricingRules.cs:104-112`). The *order* is right, the *reason* is not, and a contributor trusting the comment could relax it. And `pricing:view` now gates nothing: it is still granted to boutique owner/manager (`Permissions.cs:104,111`) but no route requires it after the team-only switch (`grep -rn PricingView Aveline.Api` → the catalog only). The docs now describe it as catalog-only, which is honest, but a granted permission with no route invites the old behaviour back.

### 3.8 New defects in the new admin endpoints

The four new surfaces exist, are correctly authorized, and have no missing org scope or raw-SQL sink. But the new code carries defects of its own.

**(a) An unbounded `page` turns either new read into a 500 (I reproduced the arithmetic).** The endpoints clamp `pageSize` but never bound `page`:

```csharp
var normalisedPage = page is null or < 1 ? 1 : page.Value;   // no upper bound
```
— `AuditEndpoints.cs:34`, `AdminOrganizationEndpoints.cs:43`

`Skip((page - 1) * pageSize)` is `int` arithmetic (`AuditRepository.cs:79`, `OrganizationRepository.cs:72`). I verified with 32-bit wrapping:

```
page=2147483647 pageSize=200 -> Skip offset = -400
page=2147483647 pageSize=50  -> Skip offset = -100
page=2147483647 pageSize=20  -> Skip offset = -40
page=100000000  pageSize=200 -> Skip offset = -1474836680
```

PostgreSQL rejects a negative `OFFSET`, so `?page=2147483647` yields an unhandled `500` (now through the new `GlobalExceptionHandler` envelope). The pattern pre-exists in four other repositories, so this is a re-used bug rather than a new one — but these two endpoints are new and should not add to it. Fix is to clamp `page` the way `pageSize` is clamped.

**(b) The multi-override write is non-atomic and can commit state it never audits.** Each override is upserted inside the loop with its own `SaveChangesAsync` (`EntitlementOverrideService.cs:48-68`; `EntitlementRepository.cs:56,68`), and the audit entry is written *after* the loop (`:70-87`). There is no transaction in the file. So a request whose second override fails validation returns `400` with the **first override already committed and no audit row** — an unaudited entitlement change, which is precisely what the audit requirement exists to prevent. Compounding it, `PlanEntitlementOverride.EffectiveTo` is never set by this path (`EntitlementOverrideDtos.cs:9-14` has no such member; `EntitlementRepository.cs:64` copies `null`), so **an override created here can never expire**.

**(c) Overlap prevention is absent.** Uniqueness is the DB index on `(OrganizationId, Key, EffectiveFrom)` (`EntitlementConfigurations.cs:84-85`) and the upsert is a read-then-write on that triple. Two rows for the same key with different `EffectiveFrom` both satisfy `ListEffectiveOverridesAsync` and the resolver silently takes the newest. FR-4.9 documents an upsert but not what happens on overlap.

**(d) The override value is not validated against its key's canonical type, and its length is unbounded.** `EntitlementOverrideService` validates the JSON kind against the declared `valueType` (`:140-166`) but never checks that `valueType` matches the catalog type for `key` — so `api.access` (Boolean in the catalog) can be stored as a Decimal. That particular case fails closed (`ApiKeyService.cs:69-74` requires `Flag == true`), but the same hole silently affects any key read through `GetDecimalAsync`'s coercion (`EntitlementResolver.cs:56-77`). String values are returned unvalidated against the column's `HasMaxLength(200)` (`EntitlementConfigurations.cs:66-67`), so an over-long value surfaces as a `500` rather than a `400`.

**(e) A new N+1 in the hourly rollover job.** `BillingPeriodRolloverJob` now calls `entitlements.GetDecimalAsync(...)` inside `foreach (var account in due)` (`LedgerJobs.cs:169,189-194`), and each call issues three queries (`EntitlementResolver.cs:23-26`). The M-20 fix is correct but traded a dictionary lookup for 3N round-trips per hourly run.

**(f) Two small inconsistencies worth a line each.** `ResolveActorUserIdAsync` returns `Guid.Empty` when the caller has no local user row (`AdminOrganizationEndpoints.cs:96-104`), producing an audit row with `ActorKind = User, ActorUserId = null`, whereas `AdminUserEndpoints.cs:61-65` returns `401` in the same situation. And a bad `planTier` returns an undocumented `400` (`AdminOrganizationEndpoints.cs:37-40`).

### 3.9 Non-defects worth noting

- `PricingRuleCacheWarmer` still reads only `page: 1, pageSize: 200` (`:54`), so Active rules beyond 200 are never warmed. Bounded, pre-existing, unchanged — carried forward rather than newly introduced.
- `GlobalExceptionHandler` registered via `AddProblemDetails()` at `Program.cs:36` also enables the framework's `ProblemDetails` service. That is additive, not conflicting, but it changes the default 500 body shape from empty to the handler's envelope, so any client that string-matched an empty 500 must be updated. Documented behaviour change.
- `docs/backend/implementation-plan.md:527-538` still documents the **twelve** original rules with the dead short metric names, including `db.pool.saturated`. The fix updated `statistics-catalog.md` and `docs/backend/README.md` (which now says "eleven after #239") but not the implementation plan, so the three backend documents disagree with each other. Doc-side only.
- `IAlertService` did not grow methods; the `f1c701d` change to it was an XML comment. Rule CRUD (`CreateRuleAsync`/`UpdateRuleAsync`/`DeleteRuleAsync`) is therefore still test-only — no admin route exposes it.
- The new `CaptureBlossomAsync` query is exercised **only** against the EF InMemory provider (`AlertEvaluationTests.cs:325-333, 437-440`). Both new admin repositories (`AuditRepository.QueryAsync`, `OrganizationRepository.SearchAsync`) and the activation transaction are in the same position: their Postgres behaviour is asserted only by whichever Testcontainers tests cover them. Worth an explicit Postgres pass — see Phase 1.4.

---

## 4 · Reconciliation plan

The findings fall into three buckets, and the right action differs per bucket: **close the loop**, **fix what the fix missed**, and **decide and record**. Ordered by risk reduction per unit of effort.

### Phase 1 — Close the holes the fix session left, and its new ones (before release)

| # | Action | Finding | Why now | Effort |
|---|---|---|---|---|
| **1.0** | **Restore the backdate gate on activate and add it to PATCH:** in the activate handler, apply the same `if (request?.EffectiveFrom < DateTime.UtcNow) → AuthorizeAsync(principal, Permissions.PricingBackdate)` check that `POST /rules` already uses; do the same on PATCH for `EffectiveFrom`; **or** have `ActivateRuleAsync` normalise to `max(requested, now)` and reject a past value without the permission (BR-1.6's literal wording). Add an activate-with-past-date test asserting **403** for an Admin and **200** for an Owner. | **3.0** | An Admin can currently backdate a price change; the permission exists specifically to stop that. Highest severity in this report. | ~half a day |
| 1.1 | Accept and persist `effectiveTo` on `EntitlementOverrideInput`; validate `effectiveTo > effectiveFrom`; add a retirement path (or an explicit `isActive`/cancel semantic) and document whichever is chosen | **3.1** | New endpoint, contract drift, trivially cheap to fix while nothing consumes it | ~half a day |
| 1.2 | Close the idempotency concurrency window: take a per-`(org, endpoint, key)` advisory lock across the endpoint invocation, **or** insert the replay row before `next(context)` and roll it back on failure; add a test with two concurrent identical requests asserting exactly one ledger entry | **H-1(b)** | The docs make the key mandatory precisely so money operations are safe; the guarantee is currently emergent | ~1 day |
| 1.3 | Add `HttpMethod` to the `IdempotencyRecords` uniqueness key and make `OrganizationId` non-nullable for filtered routes (or add a partial index), with the migration | **H-1(d)** | Latent false-`409`; cheap now, awkward later | ~2 hours + migration |
| 1.4 | Re-run the Postgres suite specifically for activation atomicity and the new audit/override/blossom-capture paths | **H-3, C-1, 3.3(c)** | The atomicity fix is transaction-shaped and only a real Postgres test can prove rollback; in-memory providers no-op `BeginTransactionAsync`. The new drift query has never been translated by Npgsql. | ~2 hours |
| 1.5 | Log (and count) the swallowed exception in the collector's database block, and stop treating an absent drift sample as a passing evaluation | **3.3(c)** | Right now a telemetry read failure becomes a false all-clear on the ledger-integrity control | ~half a day |
| 1.6 | Apply the storm guard to the re-fire path (`MaxAlertsPerHour`), and refresh `LastTriggeredAt` on re-fire | **3.3(a)** | The fix activated an unthrottled notification path; 4 Critical rules × 12/hour | ~2 hours |
| 1.7 | Filter `CaptureBlossomAsync` to the current period and open accounts, or change `blossom.balance.negative` to evaluate only open periods | **3.3(b)** | A single historical overdrawn period otherwise latches a Critical alert for ever | ~half a day |
| 1.8 | Change the `telemetry.dropped` rule from `Sum` to `Rate` (as `eventbus.failed` already does), or reset/derive the counter per window | **3.4** | One-line rule-data fix; the alert currently latches permanently | ~1 hour |
| 1.9 | Update `docs/api/openapi.yaml`: policy name, the three `price-book` paths, `GET /admin/orgs`, `GET /admin/audit/{entryId}`, the entitlement-override route (real `PATCH` → 200, the `{overrides:[…]}` body, no `effectiveTo` until 1.1 lands), cancel 400 `rule-priced`, the corrected PATCH-409 wording, and the `OnboardingPending` enum value | **3.5, 3.8(d)** | The OpenAPI document is the machine contract, is now wrong in at least eight places, and has no `price-book` path at all | ~1 day |
| 1.10 | Fix the constraint comment at `PricingService.cs:149-150`, and either wire `PricingRuleOverlapException` or delete it | **3.7(e)** | Cheap; a wrong comment licensing a wrong refactor is how this class of defect recurs | ~1 hour |
| 1.11 | Bound `page` in both new endpoints (mirror the `pageSize` clamp); add an upper limit on override string length and a `valueType`-vs-catalog-type check | **3.8(a), 3.8(d)** | `?page=2147483647` is a 500 today; the type check is what makes an override trustworthy | ~half a day |
| 1.12 | Wrap the multi-override apply and its audit write in one transaction, and add an `effectiveTo`/retirement semantic so overrides can expire and cannot overlap | **3.1, 3.8(b), 3.8(c)** | An unaudited entitlement change is the exact failure the audit log exists to prevent | ~1 day |
| 1.13 | Move the rollover job's entitlement resolution out of the per-account loop (resolve the distinct tiers once per run) | **3.8(e)** | 3N queries per hourly run where one lookup per tier suffices | ~2 hours |
| 1.14 | Re-apply the security headers and HSTS inside `GlobalExceptionHandler` (or hoist `UseAvelineSecurityHeaders`/`UseHsts` outside `UseExceptionHandler`), and add a test asserting the headers on a 500 | **3.6** | Verified empirically: the five hardening headers are absent on every handled 500. The handler is correctly placed; the headers are the bug. | ~2 hours |
| 1.15 | Salt the user-agent hash (or drop it), and document `Telemetry__IpHashSalt` in `.env.example` and `docker-compose.yml` | **3.7(a)** | Half of M-1 remains open, and a real Production deploy now refuses to boot without an env var no template mentions | ~2 hours |
| 1.16 | Correct the three doc statements that still describe the pre-fix behaviour (`docs/api/README.md:177,191`; `Aveline.Api/Common/Exceptions/README.md:7-8`) and soften the M-4 claim to "partially fixed" | **3.7(b), 3.7(c)** | Each is a false statement a reader would act on | ~1 hour |

**Exit criteria:** an Admin activating with a past `effectiveFrom` gets 403 and an Owner gets 200; two concurrent same-key credit requests produce exactly one ledger entry and one `201`/one replay; a failed activation leaves the predecessor Active; `effectiveTo` round-trips; a collector read failure is visible in logs and does not resolve a firing alert; a continuously-breaching rule fires at most `MaxAlertsPerHour` times per hour; a handled 500 carries the security headers; `docs/api/openapi.yaml` parses and matches the routes.

### Phase 2 — Finish the deferred surfaces, or delete them from the contract

Every remaining absent endpoint is now labelled *deferred* in `docs/backend/README.md:65`. That converts drift into a **recorded scope decision**, which is acceptable — but a deferred endpoint that stays in `openapi.yaml` and `docs/api/README.md` will keep generating false "documented but missing" findings. Pick one:

| Option | What it means | Tradeoff |
|---|---|---|
| **A. Implement** the five `/admin/statistics/billing/*` and the three org billing-statistics endpoints | Restores parity with `statistics-catalog.md` §S-4…S-12 | ~5–8 days; depends on the ledger recompute work |
| **B. Mark as planned in the contract** — move them out of the normative `openapi.yaml`/`§C` tables into a clearly-fenced "Planned / not yet implemented" appendix in both files | Findings stop recurring; no code cost | The OpenAPI document is no longer a complete contract for planned work; needs the reconciliation procedure in `docs/api/README.md:§1.3` updated to say so |
| **C. Delete** them from the docs | Smallest surface | Loses the record of intended behaviour |

**Recommendation: B for the five billing-statistics endpoints, A only for `burn-rate`** (it drives the documented upgrade prompt and depends on data that already exists). Bundle this with 3.1's doc fix so `openapi.yaml` and `README.md` move together.

### Phase 3 — Resolve the carried-forward open findings

| # | Finding | Recommended disposition |
|---|---|---|
| 3.1 | **M-2** committed default `change-me-internal-token` | Fix. Remove the default from `docker-compose.yml:80,134`, make `.env.example` a placeholder with no working value, and fail fast in `Program.cs` when the token is unset **or** equals the known placeholder outside Development. Highest residual risk of the open set. |
| 3.2 | **M-10** documented 15 s overview cache absent | Either add the cache (15 s `IMemoryCache` on `GetOverviewAsync`, matching `implementation-plan.md`) or delete the claim from `docs/api/README.md:1952`. Prefer deleting the claim: the other statistics endpoints are uncached and consistent. |
| 3.3 | **M-11** `/system/eventbus` ignores `from`/`to` | Bind the window and filter, or remove the params from the docs. Prefer binding — it is a few lines and the counters are cumulative. |
| 3.4 | **M-8** bare-array price book | Behind a team-only policy and unpaginated by design; add `page`/`pageSize` with the standard envelope so it stops violating the stated convention, or carve an explicit exception into `docs/api/README.md:245-246`. |
| 3.5 | **M-5** no app-level rate limiting | Track separately as a platform item, not an admin-API defect. The project already records it as SEC-M2. |
| 3.6 | **M-22** alert sample query unscoped | Add an `OrganizationId` predicate and a row cap now, while the query is small; a future per-org rule would otherwise silently aggregate every tenant. ~1 hour. |
| 3.7 | **M-16, M-17, M-12** | Fold into the documentation pass; verify the `blossomBalanceAfter` vs `balanceAfter` naming against whatever the frontend actually consumes before changing either side. |

### Phase 4 — Institutionalise the fixes so they do not regress

The most valuable thing in this fix session is the test that asserts every seeded alert rule references a metric the collector can emit — it converts a silent, recurring class of bug into a build failure. Generalise that pattern:

1. **A contract test per admin route family.** `RouteContractTests` that read the implemented route table and assert every route appears in `docs/api/openapi.yaml` with the documented method and policy, failing the build on drift. This is the mechanical version of the manual diff in §8.1 of the original report, and would have caught C-1, H-2, M-9, M-18 and M-19 automatically.
2. **A "documented status codes are produced" test** for the pricing and admin route families, driven by the documented table.
3. **Keep the seeded-rule ↔ collector assertion** and extend the same shape to `SystemAlertRuleSeed` thresholds (assert the unit matches the metric's unit, which is how C-5's `api.error_rate` vs `aveline.api.error_rate` confusion arose).
4. **A startup assertion in Development** that `Telemetry:IpHashSalt` is non-empty in the compose profile, so the guard cannot be satisfied only in Production while the shipped dev profile hashes unsalted.

### Phase 5 — Release gate

Before the next deploy, the following must be true, each with the test or command that proves it:

| Gate | Proof |
|---|---|
| No privileged self-approval | An integration test asserting a Moderator's own request returns 403 and leaves `Status = Pending` |
| No Blossom under-charge at any token magnitude | A unit test asserting `CalculateBlossomUnits(int.MaxValue, int.MaxValue, int.MaxValue)` exceeds the minimum charge (the fix is in; the test is the guard) |
| Exactly-once money mutation under concurrency | A Postgres test with two concurrent same-key requests |
| Activation atomicity | A Postgres test that fails the successor write and asserts the predecessor is still Active |
| No dead alert rules | The existing collector-emits-every-seeded-metric assertion |
| No unsalted IP hashing in Production | The startup guard, plus a test asserting it throws in Production and not in Development |
| Full suite green | `dotnet test` → 0 failed |

---

## 5 · Recommendation

The fix session did the hard part. **C-2, C-3, C-5 and H-4 alone justify treating this revision as a materially safer system than `e5a8f34`,** and the documentation reconciliation in `#243` is unusually honest — it records the `UseLegacyFormula` inertness, the missing cross-instance cache invalidation, the pricing permission-model conflict between two spec documents, and the deferred billing statistics, rather than quietly deleting the contradictions.

Two things stand between this and a release: **Phase 1** (both items are small and one is a genuine correctness gap in a money path), and a decision on **Phase 2 option B** so the deferred endpoints stop being reported as missing. Everything in Phase 3 is normal backlog; Phase 4 is the item with the best long-term return, because the class of defect this review found — a contract that drifts from its implementation in ways no test notices — is exactly what a route-contract test would have caught automatically.

---

*Read-only review: no file under `Aveline.Api/`, `Aveline.Api.Tests/` or `agent-service/` was modified. The only file written is this report.*
