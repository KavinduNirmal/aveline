# Aveline tenant dashboard (`/app/b/:slug/:section`)

**Status:** delivered across **T0a–T7**, with the gaps recorded rather than implied: no authenticated
end-to-end walk (no Clerk test session in this environment), `E-2`/`E-3` have no panel caller yet, and
six shell components are still at 0 % coverage. Each is stated in place below. This page is the
general documentation for the tenant dashboard and is authoritative for the shipped behaviour. The
plans are:

- `.agents/plans/tenant-dashboard-implementation.ignore.md` — the executable plan (revision 1). It
  is a gap analysis plus a build specification: endpoints E-1…E-13, one new append-only ledger, and
  eight frontend sections.
- `.agents/plans/tenant-dashboard-implementation-strategy.md` — revision 2 (FINAL), the review and
  design record. It re-cuts the plan's seven phases into **eight `T`-slices** and answers the
  open questions **TD1–TD17**. Where the two disagree, the strategy wins.

The dashboard is the **boutique owner's and staff's tool**. The Aveline team's console is
`/admin/:userId/*`; no `org:boutique_*` role reaches `/admin/*`, and the two trees deliberately
share no query cache, no coverage run and no data-quality vocabulary.

## Slices and their issues

| Slice | Scope | Issue |
| --- | --- | --- |
| **T0a** | Contracts and the permission family | [#351](https://github.com/KavinduNirmal/aveline/issues/351) |
| **T0b** | Tenant conformance, truthfulness and coverage gates | [#352](https://github.com/KavinduNirmal/aveline/issues/352) |
| **T1** | Isolation and correctness (F-1, F-2, F-5, F-6, F-7, F-9, F-11) | [#353](https://github.com/KavinduNirmal/aveline/issues/353) |
| **T2** | Customer CRUD (E-6…E-9) | [#354](https://github.com/KavinduNirmal/aveline/issues/354) |
| **T3** | The income ledger (critical path) | [#355](https://github.com/KavinduNirmal/aveline/issues/355) |
| **T4** | Tenant KPIs, reduced takings, Overview rebuild | [#356](https://github.com/KavinduNirmal/aveline/issues/356) |
| **T5** | Usage and billing | [#357](https://github.com/KavinduNirmal/aveline/issues/357) |
| **T6** | Approvals, Team, Settings | [#358](https://github.com/KavinduNirmal/aveline/issues/358) |
| **T7** | Gates, docs and the coverage ratchet | [#359](https://github.com/KavinduNirmal/aveline/issues/359) |

Critical path: `T0 → T1 → T2 → T3 → (T4 ‖ T5 ‖ T6) → T7`.

## Sections and their gates

A section is visible when its **cheapest panel is readable**; every richer panel inside it is
**hidden with a stated reason**, never 403'd. This is the rule the strategy's TD13 generalises, and
it exists because a hidden panel and an empty panel must be distinguishable only on purpose.

| Section | Gate | Contents (target state) |
| --- | --- | --- |
| **Overview** | — | Reduced takings card for every role; the full KPI strip only with `reports:view`; revenue trend; top items; the existing Home focus feed; low stock |
| **Salon** | — | unchanged (conversations, HITL sign-off) |
| **Customers** | `customers:view` | Client book, detail sheet, log a visit; add/edit/remove only with `customers:manage` |
| **Catalog** | `catalog:view` | Pieces, Lookbooks, Sourcing and Ateliers in one switcher; the header chips read `null` as "not measured"; the piece drawer writes only with a real organisation id |
| **Income** | `reports:view` | Ledger register (E-4), per-kind totals (E-5) and the two-basis reconciliation |
| **Usage** | — (balance is `billing:view:self` for every role) | Blossom balance for every role; usage-vs-limits, burn rate and the burn chart with `billing:view`; API consumption behind `stats:view`. **No agentic panel** — a boutique reads Blossoms, not runs/tokens/cost |
| **Billing** | `billing:view` | Plan card, entitlement table, billing-period history (E-12), Blossom **statement**; the top-up dialog (E-11) only with `billing:manage` |
| **Approvals** | `approvals:approve` | Queue, detail and the decision verbs; **reject/revise hidden without `orders:manage`** (Q14) |
| **Integrations** | `settings:manage` | unchanged (owner-only; the WhatsApp and gateway credentials) |
| **Team** | `team:manage` | **Members** (list, change role, suspend/activate, remove; own row and owner rows disabled with a reason) and **Invitations** (single + bulk E-10) |
| **Settings** | `settings:manage` | Boutique profile, settings + entitlements, API keys |

## The Customers section (T2)

The tenant customer surface had only list, highlights, walk-in create and record-interaction. T2
added the four missing routes and the section that uses them.

**E-6 repairs a dangling contract rather than only adding a route.** `POST …/interactions` already
emitted `Location: …/customers/{customerId}` and the GET did not exist. There is now a test that
follows that header and asserts `200`.

**Two semantics the API docs never stated, now pinned by tests:**

- **409 `customer-phone-conflict`.** The unique `(OrganizationId, PhoneNumber)` index had no
  `DbUpdateException` mapping on this surface, so a collision returned **500**. A pre-check in the
  service makes it a typed 409 on **create as well as update**, and on every provider — the
  in-memory test provider does not enforce the index, so without the pre-check the behaviour would
  have depended on the storage engine.
- **Idempotent soft delete.** Deleting an already-deleted client is `204`, not `404`. The service
  reads through `IgnoreQueryFilters()` with the tenant scope applied explicitly, because the
  soft-delete filter would otherwise hide the row and turn a repeated delete into a 404. A client
  with **non-terminal** orders is refused with `409 customer-has-open-orders`, and that guard is
  documented as what it is: a business guard, **not** referential integrity, because `Order.CustomerId`
  has no foreign key and `Order.CustomerName` is denormalised.

**What the section renders, and what it refuses to render.**

- The book is server-paged and server-filtered. Search, level and page go to the API, so the table
  never invents a total from a page it did not fetch. An error clears the rows and offers a retry
  rather than leaving the previous page's data under a new filter.
- A 404 renders **"This client is not in this boutique"** and says why the view cannot be more
  specific: the server makes "deleted" and "in another boutique" indistinguishable on purpose.
- `loyaltyTierIsDerived` is surfaced as the words "(tier derived)" beside the level. `status` is
  **not** an editable field, and the edit form says so.
- Write affordances exist only where the server would accept them: **Add client**, **Edit** and
  **Remove** are absent for a role without `customers:manage`.
- The log-visit dialog's amount field is labelled **"amount taken"**, the copy states that the amount
  joins the client's lifetime spend and that **no Blossoms are charged**, and the dialog renders no
  income figure. Only an inbound, in-person interaction is counted as a visit, and the confirmation
  says which of the two happened.
- The walk-in dialog honours the **200-vs-201 distinction in its wording**: a duplicate reports
  "already on file" rather than "created", because reporting a create that did not happen is how a
  client book stops being trustworthy.

**One shared formatter.** `src/lib/format-money.ts` is the tenant tree's single money formatter, and
its tests pin the rule the whole dashboard rests on: `null` renders **"not measured"** while a
measured `0` renders as `0`. Two panels cannot disagree about the symbol, the separators or that
distinction.

| **Catalog** | `catalog:view` | unchanged, except: the four write affordances need `catalog:manage`; the ten operational tools (scan, label, photograph, compose) stay at member level |
| **Income** *(new)* | `reports:view` | Ledger register, reconciliation banner, per-kind totals, revenue trend |
| **Approvals** | `approvals:approve` | Queue list, detail, approve / reject / revise |
| **Integrations** | `settings:manage` | unchanged; **owner-only on purpose** — this is where the WhatsApp and payment-gateway credentials live |
| **Team** | `team:manage` | Members (list, promote, demote, suspend, activate, remove) and Invitations (single + bulk) |
| **Usage** | `billing:view:self` for the balance; richer panels for `billing:view` / `stats:view` | Balance and progress, usage-vs-limits, burn rate, API consumption; no agentic panel, because a boutique reads Blossoms |
| **Billing** | `billing:view` | Plan card, entitlements, period history, **statement** (never an invoice), top-up packs |
| **Settings** | `settings:manage` | Boutique profile, settings + entitlements read, API keys |

## The Catalog section — page chrome, honest chips, and the piece drawer

The catalog is the one section that hangs directly off the dashboard shell rather than a slice
documented above, so it did not inherit the shell's page chrome, its money formatter or its
truthfulness rules when the rest of the dashboard was rebuilt. This pass brings it into line.

**The page chrome, rebuilt.** `CatalogPanel` now opens with the same three-part header as every other
tenant panel: the boutique name as an eyebrow, a serif `Catalog` heading, and one sentence stating
what the section holds and that an unreturned figure reads "not measured". Two actions sit opposite
it — **Refresh** (re-runs the four reads and says so) and **Add piece** (opens the drawer). Below the
header is a single section switcher for **Pieces**, **Lookbooks**, **Sourcing** and **Ateliers**. Each
tab carries its count, and an unreturned list renders an em dash rather than a `0`.

**The chips are honest.** Four `CatalogStat` tiles — Pieces (with units in stock), Stock value, Low
stock and Sourcing — are driven by four explicit `…Measured` flags set only when the corresponding
read fulfilled. `value === null` renders **"not measured"** in italic; a measured but empty catalog is
a real `0`. Stock value is the one money chip and goes through `formatMoney`, so the catalog cannot
quote a currency the rest of the dashboard does not use.

**The inventory grid.** `InventoryTab` filters by search (name, SKU, colour, fabric, style), by
availability and by category pill, and renders `ProductCard` per piece. The card leads with the
garment photograph, the stock state as a word and a tone (`In stock · n`, `Low stock · n`,
`Reserved`, `Archived`), the name and SKU, the price through `formatMoney`, and the visual attributes
Aveline read. Row actions (edit, floor-tag QR, delete) sit behind one menu so they do not compete
with the card's two primary actions. There is deliberately **no confidence badge**: the list payload
carries no confidence score, and a number nobody measured is worse than no number.

**The dialog became a side drawer.** `AddProductModal` is now a shadcn `Sheet` (`side="right"`, so
the content is pinned `right-0`, not a centred `inset-x-0` modal). The long form is grouped into six
numbered steps — the photograph, the piece, what Aveline read, stock and sizes, description, floor tag
— with the submit footer outside the scroll region so the primary action stays reachable. The
floor-tag tool was extracted into its own `FloorTagStudio` component: it owns the encoding choice, the
payload preview, copy, the PNG/SVG downloads and print, and it prices the tag through `formatMoney`.
A `ToggleGroup` carries the photograph source (upload vs URL) instead of two raw buttons.

**One money formatter across the whole catalog.** Every hard-coded `$` is gone. `ProductCard`,
`LookbooksTab`, `ComposeOutfitModal`, `CustomerMatchesDrawer`, `ItemQrModal`, `SourcingTab` and
`SuppliersTab` all render through `formatMoney` (LKR by default); the suppliers' `DollarSign` glyph
was replaced with `Coins` because a dollar sign beside an LKR figure is its own small lie. The print
stylesheet for a floor tag uses CSS named colours rather than hex, and the colour fallback lives beside
the colour table (`DEFAULT_COLOR_HEX` in `lib/catalog-api.ts`), so no dashboard component spells a hex
literal of its own.

**F-9 holds in the drawer.** A missing organisation id is an absent field, not a reason to address a
placeholder tenant: the visual analysis falls back to the client-side extractor instead of calling the
backend with an invented id, an image upload is skipped rather than stored against another shop, and
the floor-tag downloads are disabled with the reason shown. This is the same rule the truthfulness
gate pins with `rule 5`.

**Every rule is tested.** `AddProductModal.dom.test.tsx` pins the drawer shape (`right-0`, not
`inset-x-0`), the add/edit titles, the labelled source control, the absence of a `$` and a real
submit; `FloorTagStudio.dom.test.tsx` pins the LKR price, the "not measured" state and the disabled
downloads; `ProductCard.dom.test.tsx` pins the LKR price and the missing confidence badge. The four
source-scan rules stay in `tenant-conformance.test.ts` (palette, hex, raw controls, spacing) and
`tenant-truthfulness.test.ts` (no invented tenant id, no retained rejected draft).

## The section is part of the URL (Q6)

`/app/b/:slug/:section` — a section is linkable and survives a refresh. The bare
`/app/b/:slug` route redirects to `/overview`. An unknown segment falls back to `overview`, and a
section the role may not open is refused by the same filter the navigation uses, so a hand-typed
URL cannot render a panel the nav hides. `src/test/tenant-sections.test.ts` is the invariant between
the nav table and the router.

## The honesty contract (T0b)

Three rules, each enforced mechanically rather than by convention.

### 1. `null` is "not measured"; `0` is a measurement

Every nullable metric is typed `number | null` and the renderer distinguishes them. **No `?? 0` on
a metric.** The pre-existing counter-example is gone: `UsagePanel` computed
`usage?.blossomUsed ?? 0` and rendered "0 used" for a period the API had not measured. It now
renders one of two states — a measured body that receives a non-null `usage`, or an explicit
"usage data unavailable" card that says no allowance was measured rather than showing a fabricated
percentage.

`src/test/tenant-truthfulness.test.ts` enforces four literal rules:

1. no shipped string says "demo mode" (the shell said it whenever the balance was missing — a claim
   about the product, not a state of the data);
2. no Blossom/allowance measure is defaulted to `0`;
3. no money metric (`remaining`, `collected`, `revenue`, `margin`, `total`, `subtotal`, `discount`)
   is defaulted to `0`;
4. `recharts` is imported only by the chart wrapper, because that is where the shared
   `connectNulls` decision lives — `connectNulls: true` draws a line straight through a gap and
   reports a value the server never measured.

The plan's prose rules ("every chart has a null state", "every metric is object-accessed through
the `dataQuality` DTO") are not expressible as source scans, and a gate that cannot fail is not a
gate; that intent lives in DOM tests instead.

### 2. Conformance

`src/test/tenant-conformance.test.ts` walks the dashboard, catalogue, shared and tenant-route files
with the admin tree's rule set: no raw palette utilities, no bare hex, no raw
`<select>/<input>/<button>/<table>/<hr>`, and `gap-*` rather than `space-*`.

The scope is deliberately the tenant dashboard and its catalogue panel, **not** the public
marketing/authentication pages (`LandingPage`, `PlansPage`, `SignUpPage`, `SignInPage`, `TermsPage`,
`ContactPage`, `DownloadPage`, `InvitePage`, `AdminPendingPage`, `AdminSignUpPage`). Those are a
separate surface with their own brand palette and migration plan.

The allow-list is **empty**, which is stricter than the admin gate (which pins exactly one
exception). The pass is complete: ~160 occurrences across 17 files were fixed using the 30
primitives already present — **no `shadcn add`, no CLI run, no new primitive**. Two conversions are
worth naming because they are behaviour-preserving rather than cosmetic:

- the three-way payload and input-mode selectors became `ToggleGroup` (the primitive built for a
  single-choice segmented control), which also gives them keyboard semantics they did not have;
- the "send summary to owner" checkbox became a `Switch`, and the four tab strips became `Button`s,
  so focus rings and disabled states come from one place.

One rule was **deliberately not adopted** from the admin set: "no raw `<svg>`". The admin console
suppresses it because its only `<svg>` is a chart; in the tenant tree the only `<svg>` is
`BrandIcons.tsx`, which draws third-party payment marks whose geometry must match the provider's
official asset. Vendored third-party marks are a legitimate exception to a chart-integrity rule,
and the truthful alternative is to say so rather than to allow-list the file.

### 3. Truthful unavailable states

- A missing balance renders **"Balance unavailable"**, not "Demo mode".
- The Billing surface says **statement**, never invoice. Invoices are deferred (TD8): there is no
  `Invoice` entity, no payment-provider client and no currency column, so a document priced from
  `OrganizationSubscription.PriceLkr` (which is never assigned) would be a fabricated charge.
- The top-up flow says top-ups are **recorded grants until a payment provider is connected**. The
  demo-mode toast is gone; the button now opens the Billing section.

## The permission family (T0a)

The dashboard's server-side authorisation was materially wrong: `OrdersController` and
`BusinessRulesController` had **no `[Authorize]` at all** and used an `{orgId}` route token the
org-scope handler does not read, `catalog:manage` and `reports:view` were granted to three roles and
enforced on **zero** routes, and the client mirror carried 7 of 28 permissions with no drift guard.
T0a fixed the permission family; T1 fixes the routes.

Three permissions were added (**28 → 31**), each granted to `org:boutique_manager`,
`org:boutique_supervisor` and `org:boutique_owner`:

| Permission | Why it exists |
| --- | --- |
| `customers:manage` | `customers:view` is held by every role, so there was nothing correct to gate a client edit or delete on |
| `team:manage` | A manager may manage staff; `settings:manage` would also have handed them Integrations and its WhatsApp/payment-gateway credentials |
| `orders:manage` | Staff may approve a customer order but may not change an order's lifecycle or edit the business rules that gate discounts |

`approvals:approve` is now granted to `org:boutique_staff`. The approval controller puts one policy
on four verbs, so the grant alone would also let staff `reject` (which **cancels** the order) and
`revise` (which **rewrites** discount/total/margin); the verb split — `/approve` and `/decision` at
`approvals:approve`, `/reject` and `/revise` at `orders:manage` — is what makes the grant mean only
what it says.

New **named org-scoped** policies: `BoutiqueMember` (permission-free, so a route that means "an
active member" stops borrowing `catalog:view`), `BoutiqueCatalogManage`, `BoutiqueCustomerManage`,
`BoutiqueTeamManage`, `BoutiqueOrderManage` and `BoutiqueReportsView`.

The client mirror (`src/lib/permissions.ts`) now mirrors all 31 permissions and carries **only the
four `org:boutique_*` roles**: `org:principal` was a phantom in no backend grant set, and the team
role branches could never be reached through an org-scoped policy. `src/lib/permissions.sync.test.ts`
is the drift guard, reusing the same parser as the admin mirror. `isTenantAdmin` became
`canOpenTenantDashboard`, derived from the presence of a permission rather than a hand-listed set of
role strings.

## The isolation and correctness fixes (T1)

T1 closed the defects that made the dashboard's data untrustworthy regardless of what it rendered.

| # | Defect | Fix |
| --- | --- | --- |
| **F-1** | `OrdersController` and `BusinessRulesController` had **no `[Authorize]` at all** and used `{orgId:guid}`, which the org-scope handler never reads — so any authenticated caller could read and write any organisation's orders and rules | Both controllers authorise **and** use `{organizationId:guid}`; reads and create take `BoutiqueMember`, the order lifecycle and every business-rule route take `BoutiqueOrderManage` |
| **F-2** | Every catalogue write inherited the group's `catalog:view`, and `catalog:manage` was enforced on zero routes | The fourteen-route table: four managed (`catalog:manage`), ten operational (`BoutiqueMember`) |
| **F-5** | The approval actor was `Guid.TryParse`d from the Clerk `sub`, which is `user_…`, so `DecidedBy` was always null | Resolved through `IUserRepository.GetByClerkIdAsync`, the way every other actor is |
| **F-6** | `GET …/usage` was gated by `catalog:view` against its own doc comment | Moved to `BoutiqueBillingSelfView` (`billing:view:self`); every org role already held it |
| **F-7** | The catalogue image route was `AllowAnonymous` with no stated reason | Kept **deliberately anonymous**, with the reason recorded on the attribute: catalogue imagery is public product imagery, so the route stays `AllowAnonymous` (F-7/Q4). Only the delivery mechanism changed — the route answers `302` to the absolute Cloudinary CDN URL for a Cloudinary row and streams bytes for a database row. See [ADR-022](../ADR/ADR-022-media-storage-and-access.md) |
| **F-9** | The catalogue panel fell back to the hardcoded tenant `…0001` and retained a rejected draft after a failed save | A missing organisation is a hard error (no request sent), and the optimistic write moved inside the success path so the list always shows the server's state |
| **F-11** | `Endpoints/BusinessRulesEndpoints.cs` was dead code | Deleted |

**The hardcoded tenant id mattered.** `'00000000-0000-0000-0000-000000000001'` is a *real*
organisation id in this system, so the fallback did not fail safe — it addressed a save or an image
upload at a different shop's tenant. `tenant-truthfulness.test.ts` rule 5 now scans the tenant tree
for it.

**A failed save used to render.** `handleSaveProduct` logged the error, showed a toast reading
"Retaining local draft", and then fell through to `setInventory(...)` unconditionally. The
rejected piece appeared in the list as though it had persisted. The write is now inside the success
path and the error toast says the list still shows the server's state.

## The income ledger (T3)

`BoutiqueSaleEntry` is the **boutique's own takings** journal: what the shop took from its clients.
It is a different table from `Modules/Revenue/IncomeLedgerEntry`, which records what Aveline billed
the shop. The two are never read by each other's surface, and a test asserts that the boutique
service writes only its own table — strategy R-12 names the hazard of two isomorphic org-scoped
money ledgers in one codebase, and the name plus that test are the whole defence.

The domain name says **sale**; the wire and the navigation say **income**. That is deliberate, not an
inconsistency: the entity name must be precise enough that nobody merges it with Aveline's revenue
journal, and the owner-facing word for "what we took" is income.

### The rules the ledger enforces, each with a test

| Rule | Why |
| --- | --- |
| `Amount > 0`, enforced by a named CHECK constraint **and** the service | The sign is derived from `Kind`, so a negative amount is a bug rather than a credit. A column sum can therefore never net a refund against a sale by accident. |
| `Reason` 10–500 characters | A money row that cannot say why it exists is not journal-worthy. |
| `SourceRef` required | It is the dedup identity; without it an entry can never be reconciled against the thing that produced it. |
| The **actor rule**, narrowed | A counter sale and a refund must name a person. `OrderPayment` and `OrderSettlement` may not, because **nothing in the order or payment flow resolves an actor** — `OrdersController` passes `createdBy: null` and the payments controller resolves none — so requiring one would force a fabricated user id. The row says which flow wrote it, which is the honest record. |
| A `Verified` entry **supersedes** its `Derived` counterpart | The same money is never counted twice. The superseded row is **voided, never deleted**, and its `SourceRef` is cleared so the filtered unique index releases the key. |
| The dedup index is **per organization** | Two shops may legitimately hold the same provider reference and neither may block the other. |

The filtered-unique-index behaviour is only observable against real Postgres, and it is exactly where
a subtle bug lived: the index is filtered on `"SourceRef" IS NOT NULL` and **not on `Status`**, so a
voided row that keeps its reference still occupies the key and the takeover insert trips `23505`.
`BoutiqueSaleLedgerPostgresTests` (Testcontainers, 8 tests) drives the real service to pin both that
and the column ordering — EF puts the INSERT ahead of the UPDATE, so the void must be flushed first
inside a transaction.

### The four writers

| Writer | Trigger | Kind / Basis | `SourceRef` |
| --- | --- | --- | --- |
| `CustomerVisitService.RecordAsync` | a counter interaction carrying an amount | `Sale` / **`Verified`** | `interaction:{id}` |
| `PaymentService.ConfirmPaymentAsync` | a payment is confirmed | `PaymentReceived` / `Verified` | `payment:{id}` |
| `PaymentService.RefundPaymentAsync` | a refund succeeds | `Refund` / `Verified` | `refund:{paymentId}` |
| `OrderService.TransitionStatusAsync` | an order reaches `completed`/`delivered` with no confirmed payment | `Sale` / **`Derived`** | `order:{id}` |

Three things the writers forced into the open, each resolved rather than papered over:

1. **The visit writer closes the gap the plan calls its single most important finding.**
   `CustomerVisitService` read `PurchaseTotal`, wrote a `CustomerInteraction` with **no amount
   column**, and incremented `Customer.TotalSpent` — a number with no transaction behind it. The sale
   now leaves a row.
2. **A refund now has a destination for its reason.** `RefundPaymentAsync` accepted a `reason` and
   then discarded it: no row, no amount, no timestamp. It now attributes the refund to the caller,
   because the refund route is the one payment verb that *always* has a person behind it (it is
   guarded by `payments:refund`), and it falls back to a stated reason when the operator's note is
   too short for the ledger's rule — a refund is never lost because somebody typed three words.
3. **The derived order entry is skipped when money was already collected.** A `Derived` row is billed
   value, not cash; when the order passed through `payment_confirmed`, or a confirmed payment row
   exists, the confirmation has already written a `Verified` row for the same money and posting both
   would count one sale twice.

**Not built, and recorded rather than omitted:** no tenant-facing adjustment route. Boutique roles
hold no moving-money permission by design, and a shop editing its own revenue journal is an audit
smell; a mis-keyed counter sale is corrected by the reconciliation job plus an Aveline-team
adjustment. No back-dating. No multi-currency: `Currency` is copied from `Organization.Currency` (a
real column) rather than hardcoded, so a future non-LKR shop does not have to migrate the ledger.


The tenant subtree was measured by **nothing**: the global run excludes `src/components/**`,
`src/contexts/**` and `src/routes/**`, and the admin run includes only admin paths — so a file under
`src/components/shared/**` would have been measured by no run at all.

`vitest.dashboard-coverage.config.ts` (`bun run test:coverage:dashboard`) is the third run, mirroring
the admin config: `include`-only, its own reports directory, and a **ratchet** that starts at 0 in
T0b and is raised in T7 to the value actually achieved. A gate added after the work is a gate that
measures nothing.

## The Income section (T3 read half)

`GET …/income/ledger` (S-60) and `GET …/income/accounts` (S-61), gated on `reports:view` through the
org-scoped `BoutiqueReportsView` policy. A staff member receives `403`; their reduced view is the
takings card on Overview.

### The screen has no single "income" number, on purpose

The reconciliation banner renders **Collected** (`Verified`) and **Billed, unconfirmed** (`Derived`)
as two labelled figures, always both, plus **Refunded** so it is never invisible. The gap between
them is presented as a number to act on, not as an error. A caller that wants one figure must take
`netVerified` knowingly — `collectedBreakdown()` returns the three parts and there is no field a
caller could mistake for "the income" on its own.

The register distinguishes the two bases **in text**, not only in colour: a `Derived` row reads
"Billed, unconfirmed" and a `Verified` one reads "Money taken". A colour-only distinction is invisible
to a colour-blind reader and is lost in greyscale, and the one thing this table must never do is let
a reader mistake billed value for money taken. The sign is rendered as a character too (`+`/`−` beside
the basis label), not only as styling.

**The unknown case is rendered as unknown.** A banner that cannot measure the reconciliation says
"Reconciliation status unknown", never "Every sale is confirmed" — claiming a reconciliation nobody
performed is the class of lie this surface exists to prevent.

### Four backend behaviours the screen depends on

1. **The totals cover the whole window, not the page**, so paging through a week still shows the
   week's figures. A page-scoped total would have understated a busy week by an order of magnitude.
2. **The window cap is echoed.** A window over `TenantDashboard:MaxWindowDays` is capped,
   `windowCapped` is `true`, and a data-quality note says so. The banner labels the capped range
   rather than showing a period it did not fetch.
3. **An unknown `kind` or `basis` is a `400`**, naming the known values, never a silently ignored
   filter — a filter that quietly does nothing would make a reader conclude there were no refunds.
4. **The payment-method split comes from the payment rows**, joined on `paymentId`, rather than being
   guessed: a ledger entry carries no method of its own.

`paymentRowsPresent: false` is surfaced in the banner's notes. It is the single most likely reason a
real shop's cash figures read zero, and stating it is the difference between "your shop took nothing"
and "nothing measured your shop."


The tenant subtree was measured by **nothing**: the global run excludes `src/components/**`,
`src/contexts/**` and `src/routes/**`, and the admin run includes only admin paths — so a file under
`src/components/shared/**` would have been measured by no run at all.

`vitest.dashboard-coverage.config.ts` (`bun run test:coverage:dashboard`) is the third run, mirroring
the admin config: `include`-only, its own reports directory, and a **ratchet** that starts at 0 in
T0b and is raised in T7 to the value actually achieved. A gate added after the work is a gate that
measures nothing.

### The reconciliation job, and the one gap it cannot close

A business event can succeed while its ledger write fails: a payment confirmation returns `200`
because the money moved, and if the insert did not happen the register silently understates the
shop's takings and nothing else notices. `IncomeLedgerReconciliationJob` is the repair — hourly,
bounded to a 7-day window, and **idempotent by construction**: every repair uses the same `SourceRef`
the product writer would have used, so the ledger's filtered unique index makes a second attempt a
no-op.

**It reports how many rows it repaired, at warning level.** A job that quietly repairs rows on every
run is telling an operator that a writer upstream is broken; a bare success log would hide exactly
the signal that matters.

**What it cannot repair, and says so rather than guessing.** The plan specified it as repairing
"confirmed payments **or** interactions carrying a purchase total". Writing it established that the
second half is impossible: `CustomerInteraction` has **no amount column**. `CustomerVisitService`
reads the purchase total from the request, folds it into `Customer.TotalSpent`, and discards it —
nothing on the row records what was taken. The only available "repair" would be to invent an amount,
and a fabricated figure in a money journal is worse than a known gap. The job therefore repairs
payments, counts the interactions it could not repair, and logs them; the honest fix is a migration
that persists the amount on the interaction, which is its own slice.

`dataQuality.incomeLedgerBackfilled` is **computed** from the repaired rows rather than hardcoded, so
a register that contains repairs says so and the reader knows the ledger begins at a date rather than
claiming full history.


The tenant subtree was measured by **nothing**: the global run excludes `src/components/**`,
`src/contexts/**` and `src/routes/**`, and the admin run includes only admin paths — so a file under
`src/components/shared/**` would have been measured by no run at all.

`vitest.dashboard-coverage.config.ts` (`bun run test:coverage:dashboard`) is the third run, mirroring
the admin config: `include`-only, its own reports directory, and a **ratchet** that starts at 0 in
T0b and is raised in T7 to the value actually achieved. A gate added after the work is a gate that
measures nothing.

## The KPI aggregate and the reduced takings read (T4, backend half)

Four routes on **two policies**, and the split is the design: a section is visible when its cheapest
panel is readable, and every richer panel inside it is hidden rather than 403'd.

| Route | Policy | Why |
| --- | --- | --- |
| `…/dashboard/takings` (E-13) | `BoutiqueMember` | The cheapest panel: every role may see two labelled figures |
| `…/dashboard/summary` (S-57) | `reports:view` | Carries margin and the catalogue splits |
| `…/dashboard/revenue-series` (S-58) | `reports:view` | The series |
| `…/dashboard/top-items` (S-59) | `reports:view` | Best sellers |

**The reduction is enforced by the shape of the response, not by a UI convention.** `TenantTakingsDto`
carries exactly `collected` and `billedUnconfirmed`; a test enumerates the serialised field names and
fails if anything outside the allowed set appears, so a later addition that leaked margin or a
per-client split fails a test rather than quietly shipping.

**Every absent number is `null`, never `0`.** `0` is a measurement — nobody ordered — while `null` is
the absence of one. An average order value over no orders does not exist, so it is `null`; a margin
percentage against zero revenue does not exist, so it is `null`. A KPI strip that renders `LKR 0` for a
figure the server could not compute is stating a fact about the shop that nobody established.

### Three provenance facts the response states rather than implies

1. **`marginCostsComplete`** is `false` when any contributing order carries a zero `WholesaleCost`.
   That cost is **caller-supplied** rather than read from `InventoryItem.Cost`, so the margin is only
   as trustworthy as what somebody typed, and the flag is how a reader finds out.
2. **`cash` reports collected, outstanding and refunded separately**, so an expiring payment request
   is never presented as a receivable and a refund is never netted invisibly.
3. **The order-status classification is a named constant with a test that walks the transition map.**
   `payment_expired` is **counted** because it is not terminal — `ValidTransitions` lets it return to
   `payment_requested` — and `confirmed` and `revised` are counted because the approval path writes
   them and the model's own comment forgets they exist. Excluded are exactly `cancelled` and
   `rejected`. `OrderService.KnownOrderStatuses` is derived from the transition map, so adding a
   status without classifying it fails that test rather than silently changing every KPI.

### The frontend half, and one recorded deviation

`lib/dashboard-api.ts`, `hooks/useDashboardWindow.ts`, `KpiCard`, `TakingsCard` and the Overview
rebuild. The window is **lifted to the shell** — one selector in the header drives every panel — so
two tiles on the same screen cannot describe different periods.

**`KpiCard` exists for the `null` case.** A measured `0` renders as `0`, because no orders is a real
measurement; a `null` renders the words **"not measured"** in a visually distinct style, with the
reason beside it. `UsagePanel` used to compute `usage?.blossomUsed ?? 0` and show "0 used" for a
period the API had not measured, which is the exact failure this component makes impossible.

**The owner's preview is client-side.** Toggling "Preview the staff view" issues **no request and
grants no data** — a test asserts the strip call count does not change — and the reduced card stays
visible, so an owner sees precisely what their staff see. That answers "what does my staff see?" as a
support question rather than as a permission.

#### Deviation: no TanStack Query, and why

Strategy TD4 said to mount a `QueryClientProvider` **inside `DashboardShell`** and adopt `useQuery`
for the new tenant surfaces. **This slice did not do that.** The new panels use the codebase's
existing `useEffect` + `AbortController` loaders instead, for one reason: the shell already carries
React Context for conversations and notifications, and introducing a second server-state system
alongside them would leave two owners of "is this fresh" in one subtree — the failure mode TD4's own
rationale names. The tenant tree is small enough that the hand-rolled pattern is honest, and the
`staleTime` decisions TD4 specified (0 for a balance, 30 s for aggregates) have no analogue without
the library.

**This is reversible in one change and is recorded as a deviation, not a silent substitution.** If
the panels grow, or a mutation needs cache invalidation, TD4's plan stands: mount the provider inside
the shell, never at the root, and migrate the new surfaces. The reduced read's cache-friendliness is
moot in the meantime, because every fetch happens on mount and on window change.

### Degradation, not failure

The summary is cached for 60 seconds per `(organizationId, window)`. **A cache outage degrades to an
uncached query** and the `dataQuality` notes say the cache was unreachable — an operator reading the
dashboard during a Redis outage can tell the figures are current and only the caching is degraded.
The series carries its **own** cap and echoes `windowCapped`; a bucket with no orders is `null`, so a
chart with `connectNulls={false}` renders a gap rather than a line through nothing.

## The Usage and Billing sections (T5)

T5 wired the tenant billing surface that already existed on the server and added the two reads it was
missing. It is the slice where "a hidden panel is not an empty panel" is enforced most literally.

### E-11 — the top-up catalogue

`GET /orgs/{organizationId}/blossoms/top-up-packs` returns the active `TopUpPack` price-book rows,
because the top-up route already consumed a `SkuCode` while the only price-book read was admin-only
(B-4). The lookup is **exactly** the one the purchase performs
(`BlossomSkuKind.TopUpPack, planTier: null, organizationId: null`, filtered to
`BlossomRuleStatus.Active`), so a pack the dialog offers cannot be rejected at purchase and a pack the
purchase accepts cannot be missing. A unit test asserts the two call sites share the lookup shape and
an integration test asserts a `Draft` row is not offered.

**The catalogue carries the purchase's own permission** (`billing:manage`, owner-only). A manager who
may *read* billing but not *buy* is refused the list: whoever may not purchase may not see what is for
sale. The panel therefore does not even fetch it when the role lacks the permission, so the section
never issues a request it knows will 403.

### E-12 — the billing-period history, and the zero that is not a price

`GET /orgs/{organizationId}/billing/periods?take=12` merges three sources into one row per billing
period: the `UsageAccount` (limit, granted, adjusted, used, remaining), the day's
`OrganizationSubscriptionSnapshot` (or the live subscription row for the current period) for the plan,
and the `BlossomLedgerEntry` top-ups inside the period.

**The trap this field exists for is C-4.** `OrganizationSubscription.PriceLkr` is **never assigned
anywhere** in the product — creation, upsert and the snapshot job all leave the column default — so
"no row ⇒ `null`, else the column" would print **`LKR 0` as the plan price of every subscription**.
The rule is instead that `planListPriceLkr` is `null` whenever the stored price is zero, with
`subscriptionPricesConfigured` saying whether a price was configured. `lib/billing-api.ts`'s
`planListPrice()` is the one client-side place that question is answered, and it treats a zero as
"not configured" exactly as the server does.

`planTier` and `hasSubscriptionRow` come from the snapshot, **not** from `Organization.PlanTier`:
every organization has a tier, but only some have ever had a billing row, and reporting the former as
the latter would invent a subscription. A period with no row reads "no subscription row" rather than
"Seed".

### The Usage section's permission split

| Panel | Permission | Behaviour when absent |
| --- | --- | --- |
| Blossom balance | `billing:view:self` (every boutique role) | — |
| Usage-vs-limits, burn rate, burn chart | `billing:view` | not fetched, not rendered |
| API consumption | `stats:view` | not fetched, not rendered |

A manager sees the API panels; a supervisor sees only the balance and a stated line that the detail
is available to a manager or owner. The families are different policies, so they are separate
questions rather than one "is statistics visible" flag.

**There is no agentic panel, and that is a lockout rather than a hidden panel.** A boutique reads its
usage in Blossoms; runs, tokens and provider cost are agent internals. No boutique role holds
`stats:view:agent`, and the org-scoped `/statistics/agents/**` routes are not mounted at all (they
answer `404`), so there is nothing for the section to fetch even if it asked. The team-only admin
subset is where the agent family lives.

**The `whatsapp.monthly` limit renders "not measured" with the reason.** There is no outbound WhatsApp
send log: `InboundMessageLogs` records inbound only, and Salon `Messages` are not WhatsApp sends. The
limit is real; the meter does not exist, so the row says "no outbound send log exists" rather than
showing the zero the server would have to return (C-10).

**The burn chart renders a gap as a gap.** `connectNulls={false}`, and an empty series renders an
explicit "not measured" state rather than an empty axis that reads as "you used nothing". The chart
imports every primitive from `components/ui/chart.tsx`, which now re-exports them: the tenant
truthfulness gate forbids `from 'recharts'` in this tree precisely because that wrapper is where the
`connectNulls` decision lives.

### The Billing section, and the word that is not there

The section renders the plan card, the entitlement table, the period history and the Blossom
**statement**, plus the top-up dialog for an owner. **It is a statement of account, never an
invoice.** There is no `Invoice` entity, no numbering rule, no payment-provider client and no currency
column, so an invoice would be a document for money nobody collected (TD8/Q2). The truthfulness gate
now carries **rule 7**, which fails if the word "invoice" appears in shipped copy at all, so the
promise cannot creep back in.

**The plan card explains the server's vocabulary.** The status badge maps the
`SubscriptionView.status` enum to words and a sentence: the API's literal `"None"` is the **absence of
a billing record** (no `OrganizationSubscriptions` row), not a plan state, so it reads "No billing
record" with the sentence that says the plan and its limits come from the assigned tier and nothing is
charged. `Active`, `Trialing`, `PastDue`, `Cancelled` and `Expired` each carry their own label, tone
and sentence; an unrecognised value is shown verbatim with a note that the dashboard has no plainer
wording. The card also says **why** a plan can show "no list price configured": `PriceLkr` is never
assigned in the product, so a zero is reported as not configured rather than as `LKR 0.00`, and the
note states that no payment provider is connected so nothing here is a charge. Four glyphs (`Tag`,
`Users`, `CalendarDays`, `CalendarCheck`) label the facts, and a `Gem` tile leads the card.

The statement's reconciliation banner has three states and never collapses them: "reconciled" only
when the server actually checked, "reconciliation status unknown" when it did not, and the drift
otherwise. A failed check rendering as "consistent" would hide exactly the condition the field exists
to expose.

**The statement's last column is the grant's expiry, not a reason.** It used to render the ledger
`reason`, and a consumption row's reason named the provider and model behind the charge
("AI workflow on gpt-4o") — agent internals a boutique does not read. The column is now **Expires**
(a grant's expiry date, an em dash for consumption, which does not expire), and the org-scoped
statement response nulls the provider/model/raw-units/USD-cost fields and neutralises the consumption
reason as a second line of defence. The team-only admin statement keeps the full detail.

## Approvals, Team and Settings (T6)

T6 closed the three sections that were still `SectionPlaceholder` stubs, and the two backend gaps
that stood behind them.

### E-10 — bulk invitations, and the two fields that were dropped

`POST /orgs/{organizationId}/invitations/bulk` did not exist: the tenant panel called it and got a
`404` (F-4), while the single route accepted `validityHours` and `sendSummaryToOwner` in the request
body and the record did not declare them, so they vanished without a trace.

- **`count` is clamped to `[1,10]`** server-side, and the response reports `requestedCount`,
  `createdCount` and `effectiveValidityHours` side by side. The clamp is the control; a
  per-organization limiter (`Invitations:CreateRateLimit`, 10/min) is the backstop.
- **`validityHours` is clamped to `[1,720]`** and reaches `ExpiresAt`, so the panel's 24/168/720
  options mean what they say.
- **Both creation routes now require `Idempotency-Key`.** A double-clicked bulk create would mint a
  duplicate batch of staff codes; a replay returns the stored body with `Idempotency-Replayed: true`.
- **`sendSummaryToOwner` is answered with three states** — `NotRequested` | `Dispatched` | `NotSent`
  — plus a note. "Not asked for" and "asked for but not sent" are different facts, and the previous
  boolean could not express either. `Dispatched` means the notice was handed to the configured
  sender, which records a dispatch rather than a delivery, and the note says so. The summary carries
  **no code**, because an email is a durable, forwarded artefact.

The Members tab shows an **owner count** where the panel used to print the literal
`'Active Workspace'`, and the caller's own row and every owner row are disabled **with the reason**
rather than being left to fail with the server's `409`/`400`.

### Q14 — the approval verbs are split by what they do

Q8 granted `org:boutique_staff` `approvals:approve` and denied them order lifecycle changes. The
approval verbs made those two answers contradict each other: `reject` sets the order to `cancelled`
and `revise` rewrites its discount, total and margin. T6 split them:

| Verb | Policy | Why |
| --- | --- | --- |
| `/approve` | `BoutiqueApprovalDecision` (`approvals:approve`) | Staff may approve a customer's order |
| `/reject` | `BoutiqueOrderManage` (`orders:manage`) | Rejecting **cancels** the order |
| `/revise` | `BoutiqueOrderManage` (`orders:manage`) | Revising **rewrites the money** |

**The split had to be by verb, not only by route.** `POST /decision` carries its verb in the body, so
a staff member who kept `approvals:approve` could post `{"decision":"reject"}` and cancel the order
anyway. `ProcessDecision` therefore checks the body against `BoutiqueOrderManagePolicy` and answers
`403 order-manage-required`. The classification lives in one named function
(`Modules/Commerce/Models/ApprovalDecisions.cs`) so a future verb cannot be added without being
classified, and the panel asks `availableDecisions` before rendering a button: a hidden verb is
honest, a button that answers `403` is not. A staff approver sees only *Approve*.

### Settings, and what is deliberately not in it

The Settings section renders the boutique profile (`PATCH /orgs/{organizationId}`), the resolved
settings + entitlements read, and the API keys panel.

- **Only the changed fields are sent.** The endpoint is a partial update, so posting the whole form
  back would overwrite a concurrent edit with stale values.
- **An API key secret is shown exactly once**, beside a sentence saying the server stores only a
  hash. It is the only time the plaintext exists on the wire.
- **Integrations is not in Settings, on purpose.** The WhatsApp and payment-gateway credentials stay
  owner-only behind their own section; TD5.5 chose `team:manage` for staff management precisely so a
  manager never reaches them, and folding Integrations into Settings would undo that.

## The first-load defect, and the one rule that removes it (fix 1)

Every section showed its own "Try again" card on a cold load, and pressing it worked. The cause was
established from a captured HAR plus request-level instrumentation against the running dev build,
not from inspection: the first `/customers` request **succeeded** (200 with a full body) and only
**one** such request was ever sent, while the only rejection the API client produced was
`CanceledError` / `ERR_CANCELED`, from a request that never reached the network.

That is React StrictMode in development: it mounts, unmounts and mounts again, so the first effect's
cleanup aborts its in-flight request before it is dispatched. Each panel's `catch` turned that
cancellation into "could not load", and "Try again" then worked because a click issues a request no
effect cleanup aborts.

Two defects shared that root cause, because a request that was cancelled or superseded still drove
the UI:

1. **A cancellation rendered as a failure.** `isCanceledError` (`src/lib/api-error.ts`) separates the
   two, keyed on axios's `ERR_CANCELED` / `CanceledError`. It deliberately does **not** claim
   `AbortError`, which is native `fetch` vocabulary this client cannot emit.
2. **A slow response could overwrite a newer one.** A superseded request settling late blanked a
   table that was already correct and raised the error card over it, which is why the error survived
   a later request that succeeded.

`usePanelLoad` (`src/hooks/usePanelLoad.ts`) is the single implementation: an invocation counter
discards anything but the newest outcome, and a cancelled outcome is dropped rather than applied.
`signal.aborted` cannot be the test, because the "Try again" path calls the loader with **no signal**.
The hook takes an explicit dependency list, because the loader must be re-created when a search,
filter or page changes or the panel stops refetching — a regression this work introduced and then
pinned with a test.

## Team is about people, and the drawer is where codes are minted (fix 2)

The section was titled "Team & Code Generation". The generator, its batch controls and three
code-count cards filled the fold, so the roster — the thing the section is for — sat below it. Now:

- **The page is the member list**, with one header and exactly two actions: `Invite staff` and
  `Pending codes (n)`. The page owns no other invitation state; the count comes back from the drawer.
- **Generation lives in `team/InvitationDrawer.tsx`**, a side drawer opened by either action and
  landing on the matching view. Both views sit behind one view switch, and the drawer fetches the
  pending list only while it is open.
- **The two views are one column, not two pill rows.** Role, lifetime, count, destination and the
  summary toggle read top to bottom, and the single primary action sits at the end of the flow it
  completes. The previous layout set role and lifetime side by side as wrapping pill groups, which
  made the form jump as options reflowed.
- **Pending codes show no code**, because the server does not return one: `PendingInvitationDto` is a
  documented *non-secret* view. The list identifies each code by recipient, role and remaining
  lifetime, and offers only Revoke. A copy button there would imply the drawer holds a value it was
  never given. `expiryLabel` renders the remaining time and flags codes inside six hours.
- **The page renders one card, not two.** `MembersTab` already renders its own card with its own
  heading and description, so the page no longer wraps it in a second card carrying a duplicate
  "Members" title.

## The upgrade path (fix 3)

Usage and Billing each carry an **Upgrade plan** button that routes to `/app/b/{slug}/upgrade`, a
section that exists on the route but not in the nav. `UpgradePanel` names the current plan, states
that a change is arranged rather than applied instantly, and sends the owner to `/contact`.

It takes no payment and says so. The repository has no payment-provider client and no invoice entity
(TD8/Q2), so a checkout-shaped page would be theatre. Routing is the shell's
(`goToSection('upgrade')`); the panels only report the intent through an `onUpgrade` prop, which is
what their tests assert.

## Client Salons are created with the client (fix 4)

`POST …/customers` now also creates the client's organization-shared Salon and seeds Aveline's
greeting. The reason is not tidiness: the Salon list lists **conversations**, not clients, so a client
without a Salon is absent from the concierge surface while present in the client book. The demo tenant
was in exactly that state — two clients, no customer-bound conversations.

Clients created before this behaviour are repaired at application start by `CustomerSalonBackfillJob`
(see `docs/api/README.md`), which uses the same service path so a repaired Salon is greeted exactly
like a new one. It is idempotent and capped per boot.

## The Salon list names clients, and the two Salon surfaces are decoupled (fixes 5–6)

Three related defects in the Salon, all from one shared piece of state.

**The list said "Customer".** `salonName` returned the literal word for every client thread while the
server was already sending `ConversationDto.customerName` on each row. `salonLabel`
(`conversation/salonLabel.ts`) now names the client, names an unidentified inbound thread by its
channel reference (the only handle anyone can act on), and only falls back to "Unnamed client" when
there is genuinely no name — never to a label that reads like one. Each row's accessible name is
`Open salon <name>`.

**The general thread drifted down the list.** `sortSalons` pins the customer-less, channel-less
Salon (Aveline's own) to the top and leaves the rest in the server's newest-first order. It returns a
new array, because the list is context state. The row also carries a `Concierge` marker and the
header says "Shared concierge thread".

**Both Salon surfaces shared one open thread.** The global Aveline drawer and the Salon section read
one `activeConversationId`, so opening a client's Salon in the section turned the drawer into that
client's chat, and pinning the drawer to Aveline moved the section off the operator's selection.

The fix is a second thread slot in `ConversationsContext`: `avelineConversationId`,
`avelineMessages`, `avelineAgentState`, `avelineAgentActivity`, `avelineLoading`, `avelineSending`,
plus `openAveline`, `sendToAveline` and `decideAveline`. `openAveline` resolves the general Salon and
**never** calls the section's `openConversation`, which is the coupling that caused the reported
"cannot switch to customers any more". The two slots share the conversation list and the SignalR
connection; both join their own group (`JoinSalon` adds without leaving), and the realtime handlers
route a payload to whichever slot is displaying that conversation. The newest-page rule both threads
use is extracted into `loadNewestPage` so they cannot disagree about which messages are "recent".

**The thread header shows the thread's own subject.** A client's thread header now carries the
client's initial; the blossom was read as "this thread is Aveline's" when the thread is the client's.

## Action bars on AI message blocks

An AI message is a list of typed content blocks, and each block affords a different set of things an
associate can do with it. The rail below a block is that set, and it is a property of the **block
type**, not of the bubble: `conversation/blockActions.ts` holds the mapping as plain data, so the
rules are asserted without rendering anything.

| Block (`type`) | Brief's name | Actions |
| --- | --- | --- |
| `suggestion` | suggestion | Copy, Send to customer, Regenerate |
| `piece` | item | Forward |
| `look` | lookbook | Copy, Forward, Regenerate |

Anything else — `text`, `at_a_glance`, `sign_off`, `payment`, `courier`, `client_message`,
`attachment`, `choice`, an unknown type — draws no rail at all. The brief's own names (`item`,
`lookbook`) are accepted as aliases so a payload written either way resolves to the same rail. There
is deliberately **no Share**: the brief lists it as a general action, but nothing it could do is a
thing a customer receives, and it would have sat beside Forward doing nothing.

**The rail is the card's bottom edge, not a row of buttons under it.** `ActionableBlock` puts the
block's content and the rail inside one `overflow-hidden` box, so the card's own radius clips the
rail's bottom corners; the segments are joined, separated by a single hairline (`divide-x`), and
carry no rounding of their own. Nothing here uses the shadcn `Button`, whose pill radius is exactly
the floating-chip look the rail is not. Inside a tile row (a `piece` card is ~11rem wide) the
printed word is dropped and the glyph carries the segment, but the accessible name and the tooltip
do not change — which is the case the brief's "tooltips for icon-only buttons" is written for.

**A disabled segment stays focusable** (`aria-disabled`, never the `disabled` attribute). The reason
an action is unavailable *is* the tooltip, and a truly disabled button leaves the tab order and takes
its explanation with it. Every unavailable segment states why in the app's voice: "This thread isn't
linked to a client yet.", "No other client to forward to yet.", "Aveline is still working on a
reply.", "Another action is already running on this block."

### Send and Forward leave the boutique; neither is a room post

This is the correction that shaped the feature. `POST …/conversations/{id}/messages` writes a staff
**note** into the Salon: it reaches no customer, and on a client-bound thread it wakes the agent. A
"send to the customer" that did that would be the console telling an associate a client was messaged
when nobody was.

Both delivery actions call `POST …/conversations/{id}/deliver` instead, which resolves the thread's
client, finds a channel the tenant has connected, hands the words to the provider, and only then
records the row with `status: Sent`. `ICustomerDeliveryService` owns that path, its own service
rather than a method on `ConversationService`, because "written into the Salon" and "delivered to the
customer's channel" are different promises and one class that could do both is how a caller comes to
confuse them. The two actions differ only in destination: **Send to customer** is the client on
screen, **Forward** is a client the associate picks from `ForwardPickerDialog`, and the picker offers
only threads that can actually receive a delivery (a `customerId` or a channel `externalRef`) —
the client-less concierge is not among them.

Both are confirmed before anything leaves, because a message on a client's phone cannot be recalled.
On failure the server's own sentence is shown rather than one generic error: "This boutique has not
connected WhatsApp yet." and "That client has no WhatsApp number on file." are different things to do
next. Nothing is recorded on a refusal, so the transcript never claims a delivery that did not happen.

**Regenerate re-runs the question.** The endpoint resolves the staff turn the block replied to,
triggers the agent with the thread's customer context and answers `202`; the fresh blocks arrive as
`message.created` events like every other agent reply. The rail therefore holds a per-block pending
state on the *request* and the thread's existing working indicator carries the wait. Regenerate is
disabled while the agent is already answering, so two runs cannot race the same turn.

**Recorded deviation from the brief.** The brief asks that Regenerate "replace the block on
completion". Stored message rows are immutable history: the superseded block stays in the transcript
and the fresh reply is appended, because a view-level replacement would silently reappear on the next
read and the alternative — deleting or rewriting a stored row — trades the audit trail for a cosmetic
detail. A real replacement needs a `supersededByMessageId` column and a read-path filter; it is
raised as an open question in the PR rather than faked here.

### Per-block state and where it lives

`useBlockActions` is instantiated once per thread surface — the Salon section and the Aveline drawer
each hold their own — so one surface's in-flight action cannot disable the other's. Its pending map is
keyed by **message id**, so one block can be mid-delivery while another is mid-regenerate, and it is
what disables the rest of a block's segments while one runs: two actions on the same content would
race each other. Copy and Forward are wired by the rail's `BlockActionBridge`; a surface that wired
no handler for an action drops that segment rather than drawing it dead.

The drawer's rail has no client of its own — the concierge Salon is client-less — so its **Send to
customer** segment is refused with its reason while **Forward** still works, picking a client whose
channel the delivery goes out on.

### Tests

`blockActions.test.ts` (node) asserts the mapping per block type, that no other type draws a rail,
the availability rules and their sentences, and the payload each action hands over.
`BlockActionBar.dom.test.tsx` asserts the joined shape (one divided strip, `border-t`, no pill
radius), keyboard reachability, the busy state, and that a disabled segment stays focusable and shows
its reason as a tooltip. `blockActionRail.dom.test.tsx` drives the whole flow through `BlockList`
(render block → press segment → assert the outcome) and that the rail is the card's last child.
`useBlockActions.dom.test.tsx` covers each handler, the confirmation in front of a delivery, the
picker's destinations, the server's refusal sentence, and per-block pending isolation.

## Coverage

The tenant subtree was measured by **nothing**: the global run excludes `src/components/**`,
`src/contexts/**` and `src/routes/**`, and the admin run includes only admin paths — so a file under
`src/components/shared/**` would have been measured by no run at all.

`vitest.dashboard-coverage.config.ts` (`bun run test:coverage:dashboard`) is the third run, mirroring
the admin config: `include`-only (a gate added after the work is a gate that measures nothing), its
own reports directory, and a **ratchet that never lowers**. T0b landed it at floor 0; **T7 raised
every floor to the value the shipped subtree actually achieves**:

| Scope | Lines | Branches | Functions | Statements |
| --- | --- | --- | --- | --- |
| `src/components/dashboard/**` (recursive) | 45.05 | 42.75 | 31.87 | 43.42 |
| `src/hooks/useDashboardWindow.ts` | 100.00 | 100.00 | 100.00 | 100.00 |
| the eight tenant `src/lib/*-api.ts` modules | 90.47 | 85.39 | 83.54 | 90.90 |

Each floor is written a point or two below the measured value (at the measured value where it is
100 %) so rounding cannot block a green run. The glob percentage is **not** the text reporter's
`...ents/dashboard` row: that row rolls up direct children only, while the threshold glob matches
every subdirectory.

Two modules were raised to 100 % by T7 rather than excluded from the gate. `lib/dashboard-api.ts` was
at **9 % lines**, because the Overview DOM test mocks the module and nothing tested the request shapes
it builds; `dashboard-api.test.ts` now pins them. `hooks/useDashboardWindow.ts` was at **25 %
functions**, because its test file covered only the pure `resolveWindowRange` helper and never
rendered the hook; `useDashboardWindow.dom.test.tsx` renders it and asserts that `setWindow` moves the
window and its range together — the property the shell's "one window" design rests on.

`src/components/shared/**` is in the `include` and carries a floor of 0 because **no file has ever
landed there**: T4 kept the shared primitives in `components/dashboard/**` instead of promoting them
(see the recorded deviation above). The glob stays so the directory is measured from the moment it
first exists.

Six direct-child components are still at 0 % — the shell's `DashboardShell`, `BrandIcons`,
`NotificationBell` and `SectionPlaceholder`, plus `IntegrationsPanel` and `TeamManagement`, which
T1/T6 rewired but never unit-tested. They are named here rather than excluded, because a floor that
quietly dropped them would be a gate that measures less than it claims.

CI runs `bun run test:coverage:dashboard` as its own step (T7), beside the global and admin runs: a
tenant regression must never be blamed on the console, and an admin slice must never move the tenant
number.

## Gates and documentation (T7)

T7 is not a tidy-up: it is the slice that makes the tenant surface **discoverable and gated**.

- **`tests/e2e/tenant-dashboard/signed-out.spec.ts`** — the one walk buildable without a Clerk test
  session, modelled on the console's `console-access.spec.ts`. Signed out, `/app/b/{slug}` (bare and
  per-section, plus `/app`) reaches `/sign-in`, renders none of the shell's chrome (`Switch boutique`,
  `Reporting window`, `Top up`) and issues **zero** `/api/v1/orgs/` requests. The authenticated
  role-matrix walk remains **not delivered**; the role matrix is pinned instead by the backend
  integration tests and the DOM tests on the shell's `allowedSections` logic.
- **`Aveline.Api.Tests/TenantDashboardDocumentationTests.cs`** — the catalog's opening rule ("nothing
  is exposed unless it appears in this catalog **and** in `openapi.yaml`") made mechanical. It scans
  the endpoint sources for all thirteen new routes and asserts each appears as a README method+path
  row and as a `path` + `operation` pair in the spec. **It caught a real defect on its first run:**
  E-9's `GET …/customers/{customerId}/interactions` had a path item carrying only its `post`
  operation, so the read the client detail sheet depends on was documented as if it did not exist.
- **CI** — `test:coverage:dashboard` is a step in the `test-web` job, so the ratchet above binds in CI
  rather than only on a developer's machine.

**Two reads still have no panel.** `E-2` (`…/dashboard/revenue-series`) and `E-3`
(`…/dashboard/top-items`) are shipped, cached and documented, and `lib/dashboard-api.ts` carries typed
clients for both, but **no tenant component calls them**: the Overview rebuild shipped the KPI strip,
the reduced takings card and the Home focus feed, and stopped there. `Revenue trend` and `Top items`
in the target-state table earlier in this page are therefore **targets, not shipped panels** —
recorded here so the gap is a known one rather than a page that overstates the tree.

## Deliberate exclusions

> **Updated by the Cloudinary media workstream:** the media statements on this page have been
> corrected to match the shipped code. Catalog imagery is **no longer** excluded from Cloudinary:
> moving delivery to a CDN shipped, and the decision is recorded in
> [ADR-022](../ADR/ADR-022-media-storage-and-access.md) and
> [`media-access.md`](../security/media-access.md). What did *not* change is the tier decision —
> catalog imagery is public product imagery, so `GET …/catalog/images/{id}` stays `AllowAnonymous`
> (F-7/Q4).

Recorded so they are decisions rather than gaps.

- **No tenant-authored ledger adjustment.** Boutique roles hold no moving-money permission by
  design; a mis-keyed counter sale is corrected by the reconciliation job plus an Aveline-team
  adjustment.
- **No invoices** (TD8). A statement of account only, with the prerequisites for a real invoice
  subsystem named: a provider client, a currency column, and a numbering rule.
- **Catalog imagery is served from Cloudinary (this supersedes the former "No Cloudinary"
  exclusion).** Delivery moved to Cloudinary's CDN; bytes no longer have to come from
  `InventoryImage.ImageData`. `GET …/catalog/images/{id}` keeps `AllowAnonymous` for the unchanged
  reason — catalog imagery is public product imagery (F-7/Q4) — and answers `302` to the absolute
  CDN delivery URL for a Cloudinary row, streaming bytes only for a database row. `ImageUrl` holds
  the provider's **absolute CDN URL carrying exactly one width plus `f_auto,q_auto`**. The
  delivery mechanism and the tier decision are recorded in
  [ADR-022](../ADR/ADR-022-media-storage-and-access.md) and
  [`media-access.md`](../security/media-access.md); uploading through an arbitrary image URL remains
  a separately gated slice.
- **No authenticated E2E walk** unless a Clerk test session is available; the signed-out walk ships
  regardless and the role matrix is pinned by integration and DOM tests.
- **No per-user or per-action Blossom split.** `AiUsageRecord` carries no user, customer or agent
  key, so any "who spent the Blossoms" chart would be invented.

## Tenant-facing documentation (`frontend/web/src/docs/`)

The directory `frontend/web/src/docs/` is the **tenant-facing** documentation set: Markdown pages
registered in `src/docs/config.ts` and statically imported by `src/routes/DocsPage.tsx`, rendered at
`/docs/:slug` for boutique owners and staff. It is the only tenant documentation surface; the
console's documentation is [`admin-console.md`](admin-console.md).

The set was overhauled to describe the shipped tenant surface, and three pages that described
features the repository does not have were retired:

| Retired slug | Replacement |
| --- | --- |
| `/docs/ava`, `/docs/elle`, `/docs/lina` | The agent personas are covered in `salon.md` (names, roles and what an agent bubble shows) and `catalog.md` (Elle in look composition). The former pages documented an `elle.composeLook()` API, an ERP/HMAC sync and a memory-graph JSON payload that do not exist. |
| `/docs/admin-access` | The administrator console is the Aveline team's surface, not a tenant feature, so it left the tenant set. Its content is on this page's counterpart, [`admin-console.md`](admin-console.md). |

An unknown slug falls back to `DEFAULT_SLUG`, so the retired addresses land on *Getting Started*
rather than 404ing.

**Recorded gaps in the shipped surface**, so a writer does not document them as working:
`/app/b/:slug/upgrade` is not a member of `SECTIONS`, so the shell's `activeSection` fallback
(`DashboardShell.tsx:171-174`) rewrites it to `overview` and `UpgradePanel` never mounts — the two
*Upgrade plan* CTAs therefore land on Overview; and `CatalogPanel` accepts a `role` prop that it
destructures unused (`CatalogPanel.tsx:128`; `catalog:manage` appears nowhere under
`components/catalog/`), so the catalog's write affordances are visible to a role the server's
`BoutiqueCatalogManagePolicy` refuses.
