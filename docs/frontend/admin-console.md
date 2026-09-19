# Aveline administrator console (`/admin/:userId/*`)

**Status:** overhaul in progress. This page is the general documentation for the admin console
rebuild and is updated at the end of every slice. The authoritative plans are:

- `.agents/plans/admin-dashboard-overhaul-implementation-strategy.md` — revision 2 (FINAL), the
  design record and the source of decisions **C1**–**C8** and answers **Q1**–**Q11**.
- `.agents/plans/✅ admin-dashboard-overhaul-implementation.ignore.md` — the executable plan. Where
  the two disagree, the strategy wins.

The console is the **Aveline team's internal tool** (Q2). Boutique tenants use `app/b/{slug}`; no
`org:boutique_*` role reaches `/admin/*`, and there is no `billing:view:self` surface here.

## What the console is for

An on-call Aveline administrator answers *"is anything wrong, and what do I do about it?"* in under
ten seconds. **Grafana owns the platform time series** (four dashboards, 64 panels, a 15-day
Prometheus window); the console owns **decisions and actions** — the current state, the queue, the
exception, and the way to act on it. Where the two would show the same number over time, the console
shows the number once and links out.

## Slices

| # | Slice | GitHub issue | Status |
|---|---|---|---|
| **A0** | Harness, contracts, third-party traps | [#325](https://github.com/KavinduNirmal/aveline/issues/325) | **delivered** |
| **A1** | Truthfulness (no new feature) | [#326](https://github.com/KavinduNirmal/aveline/issues/326) | **delivered** |
| **A2** | Identity: guard chain, scope, `Gate` overlay | [#327](https://github.com/KavinduNirmal/aveline/issues/327) | **delivered** |
| **A3** | Shell: layout, registry, conformance lint | [#328](https://github.com/KavinduNirmal/aveline/issues/328) | **delivered** |
| **A4** | Dashboard: the triage surface V1–V11 | [#329](https://github.com/KavinduNirmal/aveline/issues/329) | **delivered** |
| **A5** | Core management: users, orgs, requests | [#330](https://github.com/KavinduNirmal/aveline/issues/330) | **delivered** |
| **A6** | Operations: pricing and Blossom idempotency | [#331](https://github.com/KavinduNirmal/aveline/issues/331) | **delivered** |
| **A7** | Observability: logs, audit, system, statistics | [#332](https://github.com/KavinduNirmal/aveline/issues/332) | **delivered** |
| **A8** | Edge cases, a11y, E2E, documentation | [#333](https://github.com/KavinduNirmal/aveline/issues/333) | **partially delivered** |
| **A9** | Backend defects unlocked by C7 | [#334](https://github.com/KavinduNirmal/aveline/issues/334) | **delivered** |

**Ordering rule.** A slice may merge only when the console is **strictly better than before it** —
never fiction replaced by an error card, and never a working page replaced by a placeholder.

## A0 — harness, contracts, third-party traps

### Test harness

`frontend/web/vite.config.ts` now defines **two Vitest projects**:

| Project | Environment | Files |
|---|---|---|
| `node` | `node` | `src/**/*.test.{ts,tsx}` (except `*.dom.test.*`) |
| `dom` | `jsdom` | `src/**/*.dom.test.{ts,tsx}` |

The `dom` project loads `src/test/setup-dom.ts` (jest-dom matchers plus `cleanup()`), because the
overhaul's acceptance tests turn on **absence** assertions — `queryByText(/Healthy/)` must be `null`
— and a string-rendering test cannot make them.

New dependencies, matching the existing `@tanstack/query-core@5.103.1` already present
transitively: `@tanstack/react-query`, `jsdom`, `@testing-library/react`, `@testing-library/jest-dom`,
`@testing-library/user-event`. All were added with Bun; `bun.lock` is the only lockfile.
`frontend/web/pnpm-lock.yaml` was **deleted** (Q6) and the hygiene job in `ci.yml` enforces that.

### The permission drift test

`src/lib/admin/permissions.sync.test.ts` reads `Aveline.Api/Authorization/Permissions.cs` and
`Roles.cs` **from source** (via `src/test/permissions-catalog.ts`) and asserts the client mirror in
`src/lib/admin/permissions.ts` is exact: 24 permissions, 9 role grant sets. It is generated from the
C#, not transcribed, so a change to `Permissions.cs` fails the test until the mirror follows.

The same test records, as executable fact, that `audit:view`, `stats:system` and `pricing:view` are
held **only** by `admin` and `owner` — the audit's claim that a `moderator` holds all three is false
(`Permissions.cs:104-106`). Those three strings are *dead policy*: registered for every entry in
`Permissions.All` by `AuthorizationConfiguration.cs:265-268` and referenced by no endpoint, while the
routes that own them use `RequireRole(Owner, Admin)`. That is why the console needs the role overlay
(`Gate`/`ROLE_POLICIES`, slice A2) rather than a permission-only gate.

### Corrected contracts (`src/types/admin/index.ts`)

| Type | Correction | Authority |
|---|---|---|
| `AdminOrganizationDto` | dropped the invented `ownerEmail`/`memberCount`; added `clerkOrgId: string \| null` and `ownerUserId: string` | `AdminOrganizationDtos.cs:6-15` |
| `EntitlementOverrideInput` | `value` is `number \| boolean \| string` behind `valueType`; `effectiveFrom`/`effectiveTo` are optional because the server defaults them | `EntitlementOverrideDtos.cs:12-18` |
| `SystemAlert` → `SystemAlertDto` + `SystemAlertAckResponse` | the alerts-list row carries `ruleName`; the acknowledge response is the EF entity and does not, and carries six extra mutable fields | `SystemStatisticsDtos.cs:16`; `SystemStatisticsEndpoints.cs:140` |
| `executeBlossomOperation` → `creditBlossoms` / `debitBlossoms` / `revokeBlossoms` | three request records, one per verb; the `Idempotency-Key` is a **required argument** on all three | `BlossomDtos.cs:69-78`; `IdempotencyEndpointFilter.cs:44-52` |

`revoke` now sends `{ ledgerEntryId, reason }`. It previously sent `{ amount, reason, allowNegative }`,
which the backend cannot bind — the operation was dead code.

### shadcn primitives

`chart`, `sidebar`, `empty`, `pagination`, `item`, `spinner`, `breadcrumb` and the `use-mobile` hook
were added from the shadcn registry (`new-york-v4`), with the internal module paths rewritten from
`@/registry/new-york-v4/**` to `@/components/ui/**` and `@/hooks/**`. The registry already emits `cn`
from the `"cn"` package and Radix from the `radix-ui` umbrella, so no dependency fix-up was needed and
**no second Radix copy and no Recharts downgrade was introduced**. `src/test/admin-install.test.ts`
asserts all of this mechanically: `recharts` stays `^3.10.1`, no `src/components/ui/*` file mentions
`@radix-ui/react-`, every added primitive imports `cn` from `"cn"`, and only `bun.lock` exists.

### Coverage policy (C3)

The tenant surface's coverage gate is **unchanged**: `bun run test:coverage` still measures
`src/lib/**`, `src/hooks/**`, `src/types/**` and the entrypoints at lines 80 / functions 70 /
branches 70 / statements 80, now expressed as glob-scoped thresholds so a number for the admin
subtree can never move it.

The admin subtree is measured by its **own** run:

```bash
cd frontend/web
bun run test:coverage         # the tenant surface, unchanged floor
bun run test:coverage:admin   # the admin subtree, floor of 0 at A0 (the ratchet)
```

`vitest.admin-coverage.config.ts` includes `src/routes/admin/**`, `src/components/admin/**` and
`src/contexts/AdminSessionContext.tsx` — including files no test has loaded yet, so the number is
honest about what is untested. The floor starts at 0 and each slice raises it to the value that slice
achieved; A8 writes the final number.

### Q1 — the `managed`-scope resolver probe

The strategy's Q1 asked whether `GET /admin/users?q=<GUID>` returns an **exact-`id` hit**, which the
whole `managed` scope depends on. The probe was attempted at A0 against the local API:

```
GET http://localhost:5091/api/v1/admin/users?q=01a0ba0b-485f-780e-b465-ae11670539e9&pageSize=5
→ 401
```

No bearer token was available to this session, so the probe is **still unproven** — exactly the state
Q1 recorded. The binding consequence is kept: A2 builds the four-way scope union with the exact-`id`
rule, and **a fuzzy hit is `unknown`, never `managed`**; if the resolver cannot be confirmed, the
console stays `self`-only.

## A1 — truthfulness

Slice A1 adds **no feature**. It deletes the fiction:

- The fabricated admin session in `AdminSessionContext` (a literal user id with `roles: ["Admin"]`),
  which is what made `AdminRouteGuard` admit a signed-out visitor and what let the dashboard render
  ten sections against six `401`s. A signed-out session is now `idle` with no identity, and issues
  **zero** admin requests.
- Both fabricated system fallbacks (`AdminSystem.tsx`, `AdminDashboard.tsx`), which substituted a
  whole healthy system — `readiness.status: "Healthy"`, three invented probe rows,
  `uptimeSeconds: 84200`, `requestsPerSecond: 12.4`, `gitSha: "a542a5e"` — on any error. A failed call
  now renders the failure.
- The three synthetic audit rows and both hand-rolled inline vector charts (pure literal data).
- The five literal user-id occurrences across `App.tsx`, `AdminSessionContext.tsx`,
  `AdminLayout.tsx` and `AdminDashboard.tsx`.
- `components/RequireAdmin.tsx`, which had zero importers.

It also unregisters the global `403` handler when the provider unmounts, and renders a **"not
measured"** label wherever the server returns a `null` metric, instead of coercing it to `0`.

`src/test/admin-truthfulness.test.ts` enforces the two mechanical rules: no inline `<svg>` in the admin
tree, and no literal user id anywhere in `src`.

## A2 — identity: the guard chain, the scope, the `Gate` overlay

### The guard chain

The `/admin/*` tree is now nested inside `ProtectedRoute` → `RequireAccountState` in `App.tsx`,
alongside `/app`. Previously it sat outside both, and the only gate was inside `AdminLayout`, so an
unauthenticated visitor reached the console.

`AdminRouteGuard` applies two checks in order:

1. **Who may open the console** — `hasConsoleRole` from `lib/admin-signup.ts`, whose `CONSOLE_ROLES`
   is now exactly `{ owner, admin }` (C2). The guard does not re-list roles, so the comment that
   claimed the two lists were "kept in sync" is now a mechanism. A `moderator` is refused with the
   stated reason *"limited to the owner and admin roles"*; a boutique role is refused too; a pending
   administrator sign-up is parked on `/admin/pending`.
2. **What `{user_Id}` means** — the resolved scope (§3.6 of the plan).

### `useAdminScope` and the exact-`id` rule

`lib/admin/scope.ts` resolves the segment to a four-way union:

| Kind | Meaning |
|---|---|
| `self` | the segment equals the caller's id — no request |
| `managed` | the segment resolved to **another user, by exact id** |
| `unknown` | not a GUID, or nothing resolved to it — never a guessed user |
| `forbidden` | well-formed and resolvable, but the caller may not look |

Two bindings worth stating in code, not just in a plan: **equality is checked before the GUID-shape
check** (a Clerk subject id is not a GUID, so shape-first would make a caller's own console
unresolvable), and **a fuzzy hit is `unknown`, never `managed`** (`q` is matched against email, name,
username and `clerkId` server-side, so an exact-id hit must be confirmed).

**Q1 is now settled by inspection, and it is a negative.** `GET /admin/users?q=<GUID>` cannot return
an exact-`id` hit, because `UserRepository.SearchAsync` filters on `Email`, `FirstName`, `LastName`,
`Username` and `ClerkId` (`UserRepository.cs:61-70`) and has **no `Id` predicate at all**. A GUID is
not any of those fields, so the resolver has no server-side path to a user by id. (The `401` the
probe returned was a second, independent reason the probe was inconclusive.)

That is C1 option (c): the console is **`self`-only**, and an unresolvable segment is a restatement
of the caller rather than a guessed user. Two rules implement it:

1. **`self` is matched against every id that means "the caller".** The console's URLs are built by
   `AdminRootRedirect` from the application user's **database id** — a UUIDv7 `Guid`
   (`User.cs:11`, e.g. `01a0ba0b-485f-780e-b465-ae11670539e9`) — while `/auth/claims` returns the
   **Clerk subject** (`ClaimTypes.NameIdentifier ?? "sub"`, i.e. `user_…`). Comparing the segment
   against only the Clerk subject could never match a real console URL; `useAdminScope` now supplies
   both ids as `selfUserIds`.
2. **Everything else falls back.** `AdminRouteGuard` redirects an `unknown` scope to the caller's own
   console instead of showing a dead-end card, and a resolver that throws (`401`, `403` on the users
   route, a network failure) resolves to `unknown` for the same reason, so the console can never get
   stuck on a loader.

`MANAGED_SCOPE_ENABLED` remains `true` so that a future `id`-aware lookup can be wired without
touching the union, but with the current endpoint no segment can ever resolve to `managed`.

**What this corrects.** An earlier revision shipped `MANAGED_SCOPE_ENABLED = false` with a hard
"Console scope not available" card and compared only against the Clerk subject. That made
`/admin/<database-id>/dashboard` — the URL the console itself generates — unusable for its own
administrator.

### The `Gate` / `ROLE_POLICIES` overlay

`lib/admin/role-policies.ts` mirrors the four `RequireRole(...)` registrations in
`AuthorizationConfiguration.cs` **by name**, and `role-policies.sync.test.ts` parses the C# to keep it
true. `Gate` renders its children only when the gate opens, and renders **nothing** otherwise — a
denied section is absent from the DOM, not hidden, because a greyed-out control is indistinguishable
from a broken one.

### `QueryClientProvider`

Mounted in `AdminLayout`, **inside the admin tree**, never at the app root (C4 + Q8). The tenant
dashboard shares no query cache with the console.

### The provisional role hint

`AdminSessionProvider` decodes the `jwt-aveline-v1` token for a **provisional** role hint so a
signed-in owner is recognised on first paint rather than flashing a forbidden state. The hint is never
authoritative: `state.roles` replaces it the moment `/auth/claims` settles.

## A3 — the shell

### One registry

`lib/admin/routes.ts` is the single source of truth for the panel, the router and the registry
invariants. It replaces `lib/admin-routes.ts`, which had drifted from the route list. `routes.test.ts`
enforces: unique ids and sub-paths, a `Gate` on every non-Overview entry, every declared permission
present in the catalogue, every domain with at least two entries, and no link to a route that is not
enabled.

Six domains — `overview`, `people`, `organizations`, `operations`, `observability`, `statistics` —
each with at least two registered entries. Routes the plan specifies but no slice has built yet are
registered with `enabled: false`, so the navigation's shape and the invariants are settled before the
page lands.

### One geometry rule

`.admin-container` (in `index.css`) is applied **exactly once**, wrapping the header and the page
content together in `AdminShell`, so both share the same centred `max-w-[1600px]` column by
construction. That is the fix for the measured 192 px misalignment. `AdminShell.dom.test.tsx` pins the
structure; the pixel measurement belongs to A8's Playwright walk.

The panel is built on the shadcn `Sidebar` primitives (collapsing to a `Sheet` below `lg`), with a
skip link to `#admin-main`, a registry-derived breadcrumb, an `AdminScopeChip` that names the active
scope, and an `AdminErrorBoundary` so one failing page cannot take the console down. The delivered
hard-coded "Live Connection" green dot is gone: it reflected nothing.

### The two blocking conformance rules (C6)

| Rule | Scope |
|---|---|
| Raw palette utilities (`emerald-*`, `amber-*`, …) and bare hex colours | the admin tree, `src/components/ui/**` exempt |
| Raw `<select>` / `<input>` | the admin tree |

Enforcement is `src/test/admin-conformance.test.ts` — a blocking test, which CI runs on every push.
oxlint 1.79 in this repository has no custom-JS-plugin API, so the test suite is where the rule binds
just as hard.

The rules required two supporting changes. `--warning` and `--success` (plus their foregrounds and
`@theme inline` mappings) were added to `index.css`, so state colours come from semantic tokens rather
than a raw palette; and the raw `<select>`/`<input>` violations in `AdminUsers`, `AdminOrgs`,
`AdminLogs` and `AdminBlossoms` were converted to the shadcn `Select` and `Switch` primitives.

The remaining two rules — raw `<button>`/`<table>`/`<hr>`, and `space-x-*`/`space-y-*` — land before A8.

## A4 — the dashboard: the triage surface (V1–V11)

The dashboard is re-specified against Grafana, not against the audit's W1–W11. The rule:

> **Grafana owns the platform's time series; the console owns decisions and actions.** Where the two
> would show the same number over time, the console shows the number once, points at Grafana for the
> series, and never builds a second copy of the chart.

`lib/admin/metrics.ts` declares the eleven widgets — id, title, kind, the endpoint it reads, the
Grafana dashboard it links to, and why it is not simply a link. `metrics.test.ts` asserts the
catalogue is exactly V1–V11 in order, that every source names a real admin endpoint and never a
Prometheus series name, and that the two largest chart specs (W5 5xx volume, W6 latency percentiles)
are absent as duplicates.

### The chart layer

`components/admin/charts/` provides `ChartFrame` (over `ChartContainer`, with the explicit height
token that stops `ResponsiveContainer` collapsing in an auto-height parent), `TimeSeriesChart`
(`connectNulls` from the shared `CONNECT_NULLS`, which is `false`), `KpiTile`, `DataQualityNotice` and
`GrafanaCard`.

`lib/admin/data-quality.ts` implements the three vocabularies and **never coerces one into another**:

| Family | Shape | Rendered as |
|---|---|---|
| system | `omitted[]` + `dataQuality{points,measured,omitted[]}` | each name as "not measured on this host" |
| agent | five booleans | each false flag named; `totalRuns === 0` is a **different** message — "no runs recorded yet" |
| api | three booleans | each false flag named on the chart that depends on it |

`formatMetricValue(null, …)` is `"not measured"` — never `0.00%`.

### Grafana deep links

`lib/admin/grafana.ts` builds links from `VITE_GRAFANA_BASE_URL` and the four **provisioned** UIDs
(`aveline-overview`, `aveline-business`, `aveline-database`, `aveline-notifications`). It never
hard-codes a host; when `VITE_GRAFANA_ENABLED` is not exactly `"true"`, or no base URL is set, every
link renders **disabled with a stated reason**, because the published Grafana port is dev-only and no
production route exists in the repository. Both variables are recorded in
`frontend/web/.env.example`.

### The mechanical boundary

`src/test/admin-prometheus-boundary.test.ts` asserts that no file in `routes/admin`, `components/admin`
or `lib/admin` contains a string matching `aveline_` or `pg_`. If a number comes from Prometheus it
arrives through the sibling workstream's proxy and its two-name contract, never from a second
derivation here.

### The ratchet

Admin-subtree coverage at A4: **27.69 % lines** (from 0 % at A0). The floors in
`vitest.admin-coverage.config.ts` are raised to the achieved values.

## A5 — core management: users, orgs, requests

### URL-backed list state

`lib/admin/query-params.ts` reads and writes `page`, `pageSize` and the surface's own filters
through the query string, so a filtered view survives the back button, is shareable, and a refresh
does not silently reset the operator's context. A page size the surface does not offer is **not
forwarded** — `readListParams` falls back to the default rather than letting the server's clamp make
the pager lie. Page sizes are `25 | 50 | 100 | 200`.

### One table, one set of states

`components/admin/data/DataTable.tsx` owns the five states (skeleton, empty, error with a working
retry, and rows) plus the `aria-sort` announcement, so a page cannot invent its own. `Pagination`
renders the range, the page-size choice and the prev/next controls.

### The self-approval guard

`AdminRequestsView` keys self-approval on **`clerkUserId` vs the caller's `sub`**, not on email, and
the admin session now exposes `clerkUserId` (from the raw `sub` claim). The captured live payloads
return `email: null` on `/auth/claims` and `""` on every request row, so the delivered email
comparison was `undefined === undefined` and admitted the approval it existed to block. The pending
count is computed on `status === "Pending"`, not on row count.

`GET /admin/requests` is a bare, unpaginated array, so the whole queue is one read.

### Entitlement overrides

`lib/admin/overrides.ts` separates the two failures that look alike: a `400` is a **field-level**
error, and a `409 override-overlap` renders a **reload-and-retry** affordance inside the dialog —
not one toast for both. It also infers the `valueType` from the key and coerces the form string into
the JSON shape the key expects; the delivered form hard-coded `valueType: "boolean"` and sent the
raw string, which the server rejects with a `400` for every non-boolean key.

The account-state dialog cannot be submitted without a reason, and the override dialog cannot be
submitted without one either.

## A6 — operations: pricing and Blossom

### The idempotency key lifecycle

`lib/admin/idempotency.ts` implements the plan's §5.1 table as pure functions. The one rule that
makes it testable: **the key is a property of the operation the user is trying to perform**, derived
from the payload fingerprint, so it does not change across a retry of the same payload.

| Event | Key behaviour |
|---|---|
| Form opens / payload changes | one key per payload; a changed payload mints a new one |
| network error, timeout, `5xx` | reuse — the operation may have applied |
| `400` | reuse — nothing applied |
| `409 idempotency-key-reuse` | mint a new key (a different operation) |
| `409 idempotency-key-in-flight` | keep, disable the button, never issue a second request |
| `503 idempotency-unavailable` | keep, offer an explicit retry, **never auto-retry** |
| `201` | discard |
| `Idempotency-Replayed` | render *"this operation was already applied"* |

`IdempotentActionButton` enforces two guarantees: **single-flight** (the guard is a ref read
synchronously before the first `await`, not a `disabled` attribute React has not re-rendered yet),
and one key per logical operation. The Blossom client functions now return the
`Idempotency-Replayed` signal alongside the body, so a replay is surfaced rather than mistaken for a
fresh application.

### The three verbs

`revoke` sends `{ ledgerEntryId, reason }`. The delivered page sent
`{ amount, reason, allowNegative }` for all three verbs, so the operation could never be bound by
`RevokeBlossomsRequest(Guid LedgerEntryId, string Reason)` — it was dead code. Every Blossom POST
now carries a mandatory `Idempotency-Key`.

The statement view fetches fresh every time (the balance **must not be cached**) and renders the
**reconciliation banner**: consistent, or drift detected with the projected and ledger-derived
balances. An unavailable statement reads *"reconciliation status unknown"*, never "consistent".

### The recompute capability

`lib/admin/pricing.ts` gates recompute on **`pricing:backdate`**, not on the page's
`pricing:manage`. `admin` holds 23 of the 24 permissions and is deliberately denied backdate, so
the control renders **disabled with the stated reason** for an admin and issues **no request**; for
an owner it runs and renders the `PricingRecomputeResult`, so a zero-effect run is visible. The
input plan's *"returns `501`, disable it"* is false — it returns `200`.

The legacy-formula banner is stated on every pricing view and comes from one shared constant.

## A8 — edge cases, a11y, E2E, documentation

**Delivered.**

- **The support handle.** `lib/admin/errors.ts` reads the `traceId` from the API's `500` envelope and
  from the `X-Request-Id`/`X-Trace-Id` headers. `components/admin/data/states/ErrorState.tsx` renders
  it **with a copy button**, because the exception itself is never serialised and the `traceId` is
  the only thing that ties a user-visible failure to a server log line. The dashboard's failed-read
  card now uses it.
- **`frontend/web/src/docs/admin-access.md` corrected (Q9).** It previously promised support
  impersonation, enforced MFA, 15-minute idle expiry and cryptographic append-only logs; none has a
  backend counterpart. The page now states what is enforced and lists those four explicitly under
  **"Not implemented — do not rely on these"**, so nobody plans around them.
- **Deployment requirements recorded** in `docs/deployment.md` §10: the SPA fallback rewrite that
  stops `/admin/<id>/users` returning `404` on refresh, the two `VITE_GRAFANA_*` variables and their
  disabled-link behaviour, and the A9 dependency on a non-null `/auth/claims` email.
- **The coverage ratchet** is written into `vitest.admin-coverage.config.ts` at the value each slice
  achieved.

**Deferred, and stated rather than implied.**

- **The remaining two conformance rules** (raw `<button>`/`<table>`/`<hr>`, and
  `space-x-*`/`space-y-*`). The two rules C6 scoped to A3 — raw palette/hex and raw
  `<select>`/`<input>` — are delivered and **blocking**; these two are not yet enforced, so the tree
  still contains `space-y-*` layout spacing that rule 4 would flag.
- **Playwright end-to-end and the axe sweep.** No `e2e/**` harness was added, so "axe zero
  serious/critical", "every route walked E2E" and the six-viewport overflow measurement are not
  executed. The geometry invariant is pinned structurally by `AdminShell.dom.test.tsx` (one
  `.admin-container` wrapping header and content) rather than measured in a browser.
- **The keyboard walkthrough and the contrast audit** of `text-muted-foreground` were not performed.

These are the honest gaps in this workstream; the plan's A8 row is not fully closed.

## A9 — the three backend defects unlocked by C7

**B1.** `GET /auth/claims` read `FindFirstValue("email")` against a principal whose email arrives
under the mapped `ClaimTypes.Email`; the endpoint now tries the mapped type and falls back to the raw
`email` claim, so a real bearer token yields a non-null email.

**B2.** `audit:view`, `stats:system` and `pricing:view` were **dead policy** — registered for every
entry in `Permissions.All` and referenced by no endpoint, while the routes that own them used
`RequireRole(Owner, Admin)`. The permission requirement is now added **alongside** the role
requirement at those three registrations. The change is additive and inert for the current role set,
because `owner` and `admin` already hold all three strings; the catalogue and the wire now agree,
which is what lets `Gate`'s role branch be belt-and-braces.

**B3.** `AcknowledgeAsync` had no state guard and would acknowledge an already-`Resolved` alert. It
now rejects the transition.

The client keeps its own defences regardless: self-approval stays keyed on `clerkUserId` vs `sub`, and
the acknowledge path still patches only `status`/`acknowledgedAt` by `id` and never reads `ruleName`.

`docs/api/README.md`, `docs/api/openapi.yaml` and `docs/architecture/authorization.md` were updated in
the same change.

## Documentation and API surface

A0–A3 change **no server route, request shape or response shape**. The client was corrected to match
what the API already documents — `docs/api/README.md` already specifies `revoke` as
`{ ledgerEntryId, reason }` and marks the four admin Blossom routes `Idempotency-Key: required`.

A9 **does** change the server: the `/auth/claims` email field, the three policy registrations and the
acknowledge state guard. Those are recorded in `docs/api/README.md`, `docs/api/openapi.yaml` and
`docs/architecture/authorization.md`.

## Local verification

```bash
cd frontend/web
bun run test              # node + jsdom projects
bunx tsc -b               # type check
bun run lint              # oxlint, 0 errors
bun run test:coverage     # tenant floor
bun run test:coverage:admin

# backend (A9)
dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj
```
