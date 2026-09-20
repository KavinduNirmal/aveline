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

| Rule | Scope | Enforced from |
|---|---|---|
| 1. Raw palette utilities (`emerald-*`, `amber-*`, …) and bare hex colours | the admin tree, `src/components/ui/**` exempt | A3 |
| 2. Raw `<select>` / `<input>` | the admin tree | A3 |
| 3. Raw `<button>` / `<table>` / `<hr>` | the admin tree | A8 |
| 4. `space-x-*` / `space-y-*` (use `gap-*`) | the admin tree | A8 |
| 5. Every Recharts `<Line>`/`<Area>` sets `connectNulls={CONNECT_NULLS}` | the admin tree | A4 |

Enforcement is `src/test/admin-conformance.test.ts` — a blocking test, which CI runs on every push.
oxlint 1.79 in this repository has no custom-JS-plugin API, so the test suite is where the rule binds
just as hard.

The rules required three supporting changes. `--warning` and `--success` (plus their foregrounds and
`@theme inline` mappings) were added to `index.css`, so state colours come from semantic tokens rather
than a raw palette; the raw `<select>`/`<input>` violations in `AdminUsers`, `AdminOrgs`, `AdminLogs`
and `AdminBlossoms` were converted to the shadcn `Select` and `Switch` primitives; and the 22 files
using `space-y-*` were converted to `flex flex-col gap-*`, which is equivalent for a block stack.

**The allow-list.** A bespoke component may carry `conformance-allow: <rule> — <why>` beside the
exception, as C6 requires. There is exactly one:

| File | Rule | Why |
|---|---|---|
| `components/admin/logs/LogRow.tsx` | raw `<button>` | the virtualised log row's two inline controls: a shadcn `Button` per row would add a primitive instance and its focus machinery to a list that re-renders every 1.5 s |

`admin-conformance.test.ts` asserts the allow-list itself is exactly that one entry, so an exception
cannot be added silently.

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
(`aveline-overview`, `aveline-business`, `aveline-database`, `aveline-notifications`).

| Configuration | Behaviour |
|---|---|
| `VITE_GRAFANA_ENABLED` set | it wins, exactly: only `"true"` enables (`"false"` disables everywhere) |
| nothing set, **development build** | enabled against `http://localhost:3000` — the compose-published port (`docker-compose.yml:274`, marked **DEV ONLY**) |
| nothing set, **production build** | every link **disabled with a stated reason**; a production bundle never invents a Grafana host |
| `VITE_GRAFANA_ENABLED=true`, no base URL, production | disabled, with the same stated reason |

`VITE_GRAFANA_BASE_URL` always beats the dev default, so a dev build can point elsewhere. Both
variables are recorded in `frontend/web/.env.example`.

**If `GRAFANA_PORT` was overridden in the compose environment**, set `VITE_GRAFANA_BASE_URL` to
match — the default assumes 3000. Because the link is a plain outbound anchor, the **browser** (not
the dev server) must be able to reach that host.

### The mechanical boundary

`src/test/admin-prometheus-boundary.test.ts` asserts that no file in `routes/admin`, `components/admin`
or `lib/admin` contains a string matching `aveline_` or `pg_`. If a number comes from Prometheus it
arrives through the sibling workstream's proxy and its two-name contract, never from a second
derivation here.

### The ratchet

Admin-subtree coverage at A4: **27.69 % lines** (from 0 % at A0). The floors in
`vitest.admin-coverage.config.ts` are raised to the achieved values.

**Final, with the business-KPI surface (P1–P6) and the landing-page KPIs:** the admin subtree is at
**78.55 % lines / 66.77 % branches**; `routes/admin` at **78.21 % / 65.14 %**; `components/admin` at
**29.41 % / 19.35 %**; `components/admin/shell` is still the drag on that last aggregate, by design.
The floors are set just below each achieved value, and `bun run test:coverage:admin` passes.

## A5 — core management: users, orgs, requests

### URL-backed list state

`lib/admin/query-params.ts` reads and writes `page`, `pageSize` and the surface's own filters
through the query string, so a filtered view survives the back button, is shareable, and a refresh
does not silently reset the operator's context. A page size the surface does not offer is **not
forwarded** — `readListParams` falls back to the default rather than letting the server's clamp make
the pager lie. Page sizes are `25 | 50 | 100 | 200`.

### Feedback round: the console's own ergonomics

Four defects reported from the running console, each fixed with a test:

| Report | Cause | Fix |
|---|---|---|
| "Go to dashboard" from `/` opened the **boutique** dashboard | `DashboardRedirect` resolved a boutique membership for everyone and sent a boutique-less operator to `/forbidden` | a console role goes to `/admin/<their id>/dashboard`; everyone else keeps the tenant resolution |
| "Back to Boutique" in the header landed on `/forbidden` | an Aveline-team operator has no boutique | the control is removed |
| The audit drawer had **two** close buttons | `SheetContent` renders its own close control and the panel added a second | the custom one is removed; a test asserts exactly one |
| The log status strip and the readiness banner were mostly empty space | both were full-width cards carrying one line of facts | both are now compact strips |
| The header read **"unknown account"** | `/auth/claims` returns `email: null` for every real bearer token, and the header fell back to a placeholder even though the console already holds the account record | the header prefers the claims email and falls back to `GET /users/me`'s email, then the username, then a neutral label — correct on an API build either side of the A9 fix |

### The dashboard has a chart again

The delivered dashboard's animated charts were **fabricated literals**, so they could not come back
as they were. The chart is rebuilt on data the console actually owns: `GET /admin/audit` — Postgres,
which Grafana does not render — bucketed by day (Weekly) or month (Monthly), drawn as animated bars
with a diagonal hatch and the peak bucket filled solid.

That keeps it inside the Q11 boundary: the console charts *business actions*, and still links out for
the platform's time series. `lib/admin/activity-series.ts` is pure and unit-tested; a bucket outside
the window is ignored rather than clamped, `peakIndex` is `-1` for an all-zero window, and `deltaPct`
is `null` rather than `Infinity` when the previous bucket was zero. Animation is gated on
`prefers-reduced-motion`.

### The reads go through TanStack Query (C4)

The `QueryClientProvider` alone is not the adoption; the reads are. `AdminUsers` and `AdminOrgs` use
`useQuery` with `placeholderData: keepPreviousData` and `staleTime: 30_000`; `AdminRequests` uses
`staleTime: 0` with a 60 s `refetchInterval`. That is §3.7 exactly:

| Surface | `staleTime` | `refetchInterval` | `keepPreviousData` | Why |
|---|---|---|---|---|
| `admin/users`, `admin/orgs` | 30 s | none | **yes** | operator-driven paging and filtering; polling a list someone is reading is noise |
| `admin/requests` | 0 | 60 s | n/a | the queue is small and the action is the point |

`keepPreviousData` is the specific reason the library was adopted: a page change keeps the previous
rows on screen while the next page is in flight, instead of blanking the table. The delivered page
held rows in component state and cleared them on every fetch. A test pins it by making page 2 never
settle and asserting page 1's rows are still rendered.

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

### `OrgPicker`

The ledger and entitlement flows are org-scoped, and the delivered console made an operator **paste
an organization GUID by hand** — error-prone, and in practice impossible. `components/admin/orgs/OrgPicker.tsx`
replaces it with a search over `GET /admin/orgs` (from two characters), reporting the selection as a
whole `AdminOrganizationDto` so the caller never re-parses an id. `AdminBlossoms` uses it for its
target organization.

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

`AdminPricingRules` renders the paged rule list with a status filter, a real pager (this endpoint
**is** paged, unlike the price book), and `RuleTimeline` — one bar per rule on a shared axis, so
overlapping or adjacent effective windows are visible at a glance. A rule with a null `effectiveTo`
is drawn to *now* and labelled **"in force"**, never as a zero-width bar that would read as expired.
`computeTimelineLayout` is pure, so the geometry is unit-testable without a browser. Each rule carries
its own `RecomputeButton`, gated on the capability.

`AdminPriceBook` renders the resolved price entries. `GET /admin/pricing/price-book` is a **bare,
unpaginated array** (rule 9), so the page sends no paging parameters and renders no pager; a null
`effectiveTo` reads *"in force"* rather than a blank cell.

### Two routes the plan names are not buildable, and why

`AdminUserDetail` and `AdminOrgDetail` are in the plan's target tree and are **not** built. The
reason is the API, not the slice:

| Route | Needed | Delivered |
|---|---|---|
| a user by id | `GET /admin/users/{id}` | only `GET /admin/users` (search) and `PATCH /admin/users/{id}/state` |
| an organization by id | `GET /admin/orgs/{id}` | only `GET /admin/orgs` and `PATCH /admin/orgs/{id}/entitlement-overrides` |

There is no by-id read for either, and the search cannot substitute for one: `UserRepository`
matches `q` against email, name, username and `clerkId` but **not** `id` (the Q1 finding above). A
detail route would therefore have to guess its subject. Adding the two missing reads would be a
**fourth item** in A9, which C7 explicitly forbids ("anything added is a new decision, not an
implication of this one").

The registry keeps both entries with `enabled: false`, so the navigation does not link to a page that
cannot be built, and `routes.test.ts` still counts them for the domain invariants. The operator flow
is the one the delivered endpoints support: search the list and act inline.

## A7 — observability: logs, audit, system, statistics

### The pure log engine

`lib/admin/log-stream.ts` is the engine, with **no React and no fetch**: `mergeEntries`,
`computeCursor`, `isNewer`, `shouldPoll`, `deriveAdaptiveInterval`, plus a `createLogStream` factory.

| Concern | Behaviour |
|---|---|
| Cursor | **`id`-based** (UUIDv7), not an offset. UUIDv7 is monotonic, so `id` is the only cursor that is stable under concurrent inserts |
| Buffer | a ring of **2000** with a **visible drop count**, never an unbounded `seenIds` set |
| Cadence | adaptive **1500 → 10 000 ms**, dropping ticks rather than queueing them |
| Single-flight | a **synchronous** gate checked before the first `await`, so a stalled response cannot accumulate overlap |
| Visibility | no requests while the document is hidden; resumes on `visibilitychange` |
| Cancellation | an `AbortSignal` per request, aborted on stop |

The cursor is proven by draining **250 rows with a server page size of 100**: two reads, 250
entries buffered, cursor at the newest id, zero dropped. `/admin/audit` can filter by instant but not
by id, so a continuation re-reads the boundary instant and skips the boundary row **by `id`**
(`LogPageRequest.cursorId`); a stall guard breaks the chain if the bound stops advancing, and
`MAX_PAGES_PER_POLL` caps a runaway.

### Derived severity, labelled

`lib/admin/log-level.ts` derives severity and **says so** (Q4). The fourth level is `other`, not the
delivered `debug` default, and every tone maps to a **semantic token**
(`destructive | warning | primary | muted`) rather than a raw palette class, which also satisfies A3's
blocking conformance rule.

### The views

`components/admin/logs/` (viewer, row, filters, connection chip) and `components/admin/system/`
(readiness table, alert list, acknowledge dialog, omitted metrics, Grafana link) feed rewritten
`AdminLogs`, `AdminAudit`, `AdminSystem` and two new statistics routes
(`AdminStatisticsAgents`, `AdminStatisticsApi`), registered in `lib/admin/routes.ts` and routed in
`App.tsx`.

Three contract details worth stating:

- **No live region on the feed.** The log feed has no `aria-live` and no `role="log"`; the
  connection chip is the only `role="status"`. A live region on a 1.5 s feed reads every entry aloud
  and is unusable.
- **No motion to reduce.** The connection chip is a static `size-2` dot with `bg-current` — the
  colour carries the state. A grep for `animate-*` across `routes/admin` and `components/admin`
  returns nothing, so `prefers-reduced-motion` has no animation to suppress. The delivered
  `animate-pulse` dot labelled *"Live Connection"* was removed in A3: it reflected nothing.
- **`status=Firing` is sent explicitly** to `/admin/statistics/system/alerts` in both the system page
  and the API statistics page, so a `Resolved` row is never counted as active.
- **Acknowledge patches by `id`.** The row is updated from `SystemAlertAckResponse`, reading only
  `status` and `acknowledgedAt` (the entity has no `ruleName`); the rule label comes from the list
  row and survives the patch. A test asserts exactly that.

### Honesty copy

The system vocabulary's `omitted[]` renders as *"not measured on this host"*; the agent family's five
booleans each name their flag, with `totalRuns === 0` reading *"no runs recorded yet"* — a different
message from "not instrumented"; the API family's three booleans each name the chart that depends on
them. The log window defaults to **24 h, capped at 7 days**, and the UI states it
(`data-testid="log-retention-window"`).

### Coverage

The admin subtree reached **67.98 % lines / 53.4 % branches** (routes/admin **68.2 % / 50.87 %**), and
the ratchet in `vitest.admin-coverage.config.ts` was raised to match.

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

- **All four C6 conformance rules are delivered and blocking** (rules 1–4 of the A3 table above,
  plus the chart `connectNulls` rule), with a single documented allow-list entry.
- **The Playwright suite covers the signed-out path only.** The harness is delivered —
  `tests/e2e/admin-console/console-access.spec.ts` with `frontend/web/playwright.config.ts` and
  `bun run test:e2e` — and it asserts the defect A1 exists to remove: signed out, `/admin/<id>/dashboard`
  and `/admin` both land on `/sign-in`, the console chrome is absent, **zero** `/api/v1/admin/*`
  requests are issued, and the page a visitor does land on has no horizontal overflow at
  1920/1440/1280/1024/768/390 px.
  The **authenticated** walk (every console route, the operator flows, the 2-second log freshness)
  needs a Clerk test session, which this environment has no credentials for. **The suite was not
  executed here**: `playwright install chromium` stalled against the CDN, and no system browser is
  present. `bun run test:e2e:install` is the one prerequisite.
- **The axe sweep** (zero `serious`/`critical`) was not run, for the same browser reason.
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

## Business KPIs — the `business` domain (P1–P6)

A second, deliberately separate surface answers the question the triage dashboard does not: **what is
the business doing?** It is the plan
`.agents/plans/admin-dashboard-business-kpis-implementation.ignore.md`, delivered in six phases.

| # | Phase | GitHub issue | Status |
|---|---|---|---|
| **P1** | Foundations and truth plumbing | [#335](https://github.com/KavinduNirmal/aveline/issues/335) | **delivered** |
| **P2** | Growth, active users and plan mix | [#336](https://github.com/KavinduNirmal/aveline/issues/336) | **delivered** |
| **P3** | Subscription history and usage | [#337](https://github.com/KavinduNirmal/aveline/issues/337) | **delivered** |
| **P4** | Frontend foundation | [#338](https://github.com/KavinduNirmal/aveline/issues/338) | **delivered** |
| **P5** | The Growth console | [#339](https://github.com/KavinduNirmal/aveline/issues/339) | **delivered** |
| **P6** | Usage console, drill-down, documentation, ratchet | [#340](https://github.com/KavinduNirmal/aveline/issues/340) | **delivered** |

### The three KPIs on the landing page

The dashboard landing page (`/admin/:userId/dashboard`) carries a **Business snapshot** section with
three of the highest-value business KPIs. They are the only part of the landing page that reads the
business endpoints, and each uses a **different chart type**, so the page does not read as one chart
repeated:

| KPI | Chart | Source | What it answers |
|---|---|---|---|
| **New signups** | line trend (`TimeSeriesChart`) | `GET business/growth` | Are we growing? New users and new boutiques per day |
| **Active users** | area trend with a gradient fill (`AreaTrendChart`), with the DAU/WAU/MAU reading above it | `GET business/active-users` | Is anyone using the product? |
| **Plan mix** | single stacked distribution bar (`DistributionBar`) | `GET business/plan-mix` | Where is the revenue? Free versus premium, from `Organizations.PlanTier` |

**The section is gated on `analytics:business:read`**, read from the permission set directly. A caller
without it does not render the section **and issues no business request** — a `403` in the network log
is a worse experience than an absent section, and a test asserts both halves.

`AreaTrendChart` and `DistributionBar` are the two new chart primitives. Both reuse the one series
shaping path (`lib/admin/business-series.ts`), so the landing page introduced no second way to compute
a bucket, and both keep the family's honesty rule: a `null` is a gap, never a line through zero.
`DistributionBar` takes **counts** and derives the proportions, so it says *"no organizations"* rather
than dividing by zero, and a tier with no measurable value is named rather than dropped.

### The volume chart now says what it counts

The existing dashboard chart was titled *"Business action volume"* with the description *"Recorded
business actions per bucket"*, which did not say what a "volume" was a volume **of**. It is now
**"Business actions logged"** with a description that names the unit and the three deliberate
exclusions — *count of write operations recorded in the audit ledger per period: ledger entries,
entitlement overrides, plan changes, approvals and pricing edits; excludes reads, failed requests and
sign-ins* — and the count axis is labelled `actions` with the tooltip spelling the unit out, so a bar
is interpretable without reading the card.

### Trap 2 of the chart layer: never put a message inside `ChartContainer`

Found while building this section, and worth recording because it is mechanical.
`ResponsiveContainer` measures its parent and renders the result into a **plain wrapper**, not a flex
box. A non-chart state rendered as its child therefore lands in a `0×0` box whenever the measurement
has not resolved, and the text wraps **one character per line**.

`ChartFrame` now takes `state: "ready" | "error" | "empty"` and renders the non-chart states itself, at
the card's full width, **without mounting `ChartContainer` at all**. Every failure on the landing page
goes through it, and `ChartFrame.dom.test.tsx` pins that no `[data-slot="chart"]` exists in those
states. The frame also trims a trailing full stop off the server's message, so a `404` reads
*"The requested resource was not found."* rather than *"…not found.."*.

### The scope decision (DR-1): a Postgres series is inside the boundary

Q11's rule — *"Grafana owns the platform's time series; the console owns decisions and actions"* —
is scoped to **Prometheus**. Its mechanical form is the grep for `aveline_`/`pg_` in
`src/test/admin-prometheus-boundary.test.ts`, and a series sourced from Aveline's own Postgres tables
cannot trip it. The delivered console already charts Postgres (`ActivityChart` over `GET /admin/audit`).

**D-1, recorded here as the plan requires:** the console **may** own a time series whose source is
Aveline's own Postgres tables and whose subject is **business state** — users, organizations,
subscriptions, usage. It may **not** own a series sourced from Prometheus, and it may not re-draw a
chart Grafana already renders from the same counter. The `V1–V11` triage surface keeps Q11 unchanged.

### Two surfaces, one new domain

| Entry | Path | Gate | What it shows |
|---|---|---|---|
| **Growth** | `/admin/:userId/business` | `analytics:business:read` | Signups, active users and the DAU/WAU/MAU reading, plan mix, subscription trend |
| **Usage & Engagement** | `/admin/:userId/business/usage` | `analytics:business:read` | Messages, agent runs, API calls and Blossoms per bucket, plus the organization ranking and the org drill-down |

The registry gains a **`business` domain** with those two entries, so the nav, the router and the
registry invariants stay in one source of truth (`lib/admin/routes.ts`). A caller without
`analytics:business:read` sees **neither** the nav item nor the route: the panel is a pure filter,
so a denied entry is absent from the DOM rather than disabled.

### The `B1…B12` catalogue is separate, on purpose (DR-2)

`lib/admin/metrics.ts` is pinned to exactly `V1…V11` by `metrics.test.ts`. Adding a twelfth widget
there is a test failure, not a feature. The business surface therefore has its own catalogue,
`lib/admin/business-kpis.ts`, with ids `B1…B12`, its own integrity test, and a cross-catalogue guard
asserting `V1…V11` is unchanged.

### The honesty contract

Every business endpoint returns a `dataQuality` block, and the console renders it through
`businessDataQuality` — the fourth vocabulary, alongside system, agent and api. The four rules:

1. **A count of `0` inside the observed period is `0`. A measure that could not be computed is
   `null`.** `KpiTile` renders `null` as **"not measured"**, never as `0`.
2. **A `null` measure stays a `null` through the chart layer.** `lib/admin/business-series.ts` is
   the one place that decides this: a bucket the server did not send is `null`, a measured `0` stays
   `0`, and an `undefined` measure becomes `null` rather than a disappearing key (`connectNulls`
   then draws the gap as a gap). That module is pure and unit-tested to 100 % lines.
3. **A partial bucket is shaded**, on the chart: `TimeSeriesChart` gained `partialBucket`, which
   renders a `ReferenceArea` over the leading clipped and trailing open buckets.
4. **A backfilled bucket is drawn dashed and named.** The subscription trend's audit-ledger
   reconstruction sets `isBackfilled`; the chart marks it and the `dataQuality` note says why.

Three further distinctions the surface keeps:

- **`userAttributionAvailable: false`** accompanies a `null` active-user reading, and the notice says
  *"active users are not measured for this window"* — so a blank is not read as "nobody uses the
  product".
- **`unresolvedAttributionCount`** is surfaced, so a stale claim map shows as a visible undercount
  rather than a low DAU.
- **`organizationsTotal` versus `organizationsWithBillingRow`** is a **footnote on the plan-mix
  chart**, not an unexplained difference between two numbers: an organization that never changed or
  cancelled its plan has no `OrganizationSubscriptions` row at all.

### The attribution fix behind the active-user KPI (B1)

The Clerk `jwt-aveline-v1` token carries Clerk's native `user_…`/`org_…` ids while Aveline stores
GUIDs, so every claim failed `Guid.TryParse` and `ApiRequestMetric.UserId` was `null` for all human
traffic. `IClaimIdentityMap` plus a five-minute `ClaimIdentityMapRefresher` now resolve the ids **off
the request path**; `RequestPrincipal.Resolve(principal, identities)` is one synchronous dictionary
read, so the telemetry middleware keeps its *"does no I/O, well under 1 ms to p99"* contract. A
refresh failure keeps the previous map; an unmapped Clerk id increments `UnresolvedCount` so the
undercount is visible. `AttributionB1Tests` is the acceptance test: a real, signature-validated
Clerk-shaped token produces a sample carrying the seeded GUID.

### Caching

The six reads are cached in `IDistributedCache` at `BusinessAnalytics:CacheSeconds` (60 s) and carry
`Cache-Control: private, max-age=60`. `IDistributedCache` rather than `IMemoryCache` because the
deployment runs two replicas; `BusinessAnalytics:RequireSharedCache` fails startup when a shared cache
is required and Redis is not configured, rather than letting the two replicas serve different figures.

### Not built here, and why

- **The organization-owner surface.** OQ-1 puts it out of this plan: it belongs on the tenant tree
  (`/app/b/:slug`), which the predecessor plan declared out of scope for console work. The console's
  org drill-down (`usage?organizationId=…`) is how an operator answers an owner's question today.
- **The seven Prometheus KPI gauges** (§5.9 layer 3 of the plan). The data-quality contract
  (layer 1), the existing request telemetry (layer 2) and the test contracts (layer 4) are all
  delivered. The gauges are the one part that reaches into `MetricsCatalog` and the seeded alert
  rules; they are alerting plumbing rather than a console requirement, and they are recorded as
  remaining work below rather than implemented half-way.
- **A `tests/load/k6-business-kpis.js`.** The plan records that this repository already has a
  load-test script with no CI job, and asks that the precedent not be repeated. The `COUNT(DISTINCT)`
  query's real latency is therefore **not measured here**; the 60-second cache is the mitigation, and
  the load test is recorded as remaining work. **No budget is claimed for an unmeasured query.**
- **A raw-path check on the six new routes.** `RouteTemplateResolver` should record
  `/api/v1/admin/statistics/business/{endpoint}` rather than a raw path; that is a runtime fact and
  is listed in the plan's §9 as a post-deploy check, not something this session could measure.
- **The Playwright walk is written but not executed here.** `tests/e2e/admin-console/console-access.spec.ts`
  now covers both new routes signed-out, but no browser is installed in this environment — the same
  limitation A8 recorded, stated rather than implied.

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
