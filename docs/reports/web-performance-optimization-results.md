# `frontend/web` — Performance Optimization Results

**Revision implemented on:** `cb46024` (branch `development`).
**Scope:** `frontend/web` (React 19 + Vite 8/Rolldown + Tailwind v4 SPA, deployed to Vercel).
**Target:** mid-tier mobile on a slow 4G connection.
**Plan this implements:** `.agents/plans/web-performance-optimization-implementation.ignore.md`
(validated, not trusted — see §2).
**Date:** 2026-09-26.

**Method.** Every load-bearing claim in the plan was re-checked against the source before it was
acted on. Delivery numbers are parsed deterministically from `frontend/web/dist/index.html` and the
files it references. Runtime numbers come from the CDP harness in `testing/performance/` (applied,
not simulated, throttling) and from a purpose-built isolation experiment. No production RUM exists,
so every number here is laboratory.

---

## 1. Headline

| | Before | After | Change |
|---|---|---|---|
| Eager JS+CSS (gzip) | 925,563 B | 197,991 B | **−78.6 %** |
| Eager JS+CSS (raw) | 3,463,566 B | 773,828 B | −77.7 % |
| Entry chunk (raw) | 2,731,826 B | 513,869 B | **−81.2 %** |
| Eager files | 16 | 6 | −62.5 % |
| `modulepreload` set | 14 files / 501 KB | 4 files / 37,463 B | −92.5 % |
| First-party JS transferred (mobile) | 717,404 B | 232,257 B | **−67.6 %** |

The byte figures are as measured on the tree at `24ea72e`. The entry chunk is byte-identical; the
eager gzip total moved 105 B between the first measurement and this one because of the Lina accent
tokens and the restored `blur` utility, which is below the precision of every percentage above.

Measured on the Lighthouse mobile profile over CDP with applied throttling
(`150 ms RTT / 1638 kbps / 4× CPU`, median of 3 cold runs):

| Metric | Before | After | Change |
|---|---|---|---|
| First Contentful Paint | 9,704 ms | 4,064 ms | −58.1 % |
| Largest Contentful Paint | 10,636 ms | 4,932 ms | **−53.6 %** |
| Total Blocking Time | 2,155 ms | 1,050 ms | **−51.3 %** |
| Main-thread busy | 5,355 ms | 2,450 ms | −54.2 % |
| Long tasks (count) | 58 | 28 | −51.7 % |
| Load event | 8,588 ms | 1,847 ms | −78.5 % |
| CLS | 0.0327 | 0.0327 | 0.0 % |

On good 4G with the same 4× CPU throttle: LCP 11,932 → **3,204 ms** (−73.1 %), TBT 3,589 →
**1,084 ms** (−69.8 %). Warm cache: LCP −65.7 %, transferred −65.9 %. Desktop unthrottled:
LCP −62.2 %, TBT 2,920 → **815 ms** (−72.1 %).

> **These figures were re-measured.** The first version of this table was taken on a build where
> the aurora's `blur(70px)` was silently missing (see §4.4). The correction moved the after-run by
> a few percent and the direction is unchanged; the numbers above are from the corrected build.

---

## 2. Claim validation

The plan's findings were checked before implementation. It was substantially correct; the
corrections below are counts and line numbers, not mechanisms.

| Plan claim | Verdict |
|---|---|
| §3.1 no code splitting; no `build` block in `vite.config.ts` | **CONFIRMED** — zero `React.lazy`/`Suspense`/dynamic `import()` in `src` |
| §3.2 three font families at full variable ranges; Geist Mono's only landing use is the step counter | **CONFIRMED** |
| §3.3 29 `<img>`, zero modern attributes | **PARTIALLY TRUE** — 29 is the raw grep count; **25 real elements in 16 files** (4 hits are prose/test names). Zero carried `loading`/`decoding`/`srcset`/dimensions. |
| §3.4 `vercel.json` has no `headers` block | **CONFIRMED** |
| §3.5 zero `React.memo`; zero `useMemo` in the four app contexts | **CONFIRMED** |
| §3.5 `DocsToc` forced reflow per scroll event, listener re-registered | **CONFIRMED** (`DocsToc.tsx:29,30,40,46`) |
| §3.5 ~25 smooth `scrollIntoView` calls/second while streaming | **PARTIALLY TRUE** — `TypewriterText.tsx:32` calls `onProgress`; the scroll is `MessageThread.tsx:46`. Mechanism and rate confirmed. |
| §3.6 7 `<AuroraField>` mounts; 6 blobs + 14 blossoms each; per-instance `<style>` | **CONFIRMED as mechanism; counts corrected** — 36 real `<Blossom>` call sites (not 43), 13 `animateCounter` props (not 24), **~105** landing-page `<style>` nodes (not ~117). The `<style>` element is `Blossom.tsx:81-106`; `79-108` brackets the `<defs>` around it. |
| §3.7 M1 256 px sidebar, no breakpoint | **CONFIRMED** — `w-64` is on `:233`, not `:232` |
| §3.7 M2 `grid-cols-[280px_1fr]` unguarded | **CONFIRMED** |
| §3.7 M3/M4 19 `min-h-screen`, 0 `dvh`, bare viewport meta | **CONFIRMED** |
| §3.7 M9 no adaptive loading | **CONFIRMED** — `navigator.connection`, `deviceMemory`, `saveData`, `hardwareConcurrency` all unused |
| §3.8 bottleneck is first-party, not third-party | **CONFIRMED** |
| §4.2.2 16 markdown documents imported as `?raw` | **PARTIALLY TRUE** — 15 (the 16th file in `src/docs/` is `config.ts`) |

Re-confirmed and deliberately **not** acted on, exactly as the plan advises: `simple-icons`
tree-shakes correctly, CLS is already passing, and the LCP element is text
(`H1.mt-6.max-w-xl.font-serif` — re-observed in the after-run, unchanged).

One correction the plan did not make: **`.agents/plans/*` is gitignored**, so the plan file itself
is not version-controlled. This report is the committed record.

---

## 3. Implemented

1. **Route-level code splitting.** All 41 route elements are bound with
   `lazyRoute(() => import(...))` behind one `<Suspense fallback={<PageLoader />}>`. Providers,
   route guards, `AuthApiBridge` and `<Toaster>` stay eager; `DashboardRedirect` stays eager
   deliberately, because deferring a redirect costs a navigation an extra round trip.
   *`src/App.tsx`*
2. **Mermaid loaded on demand** inside the diagram's render effect instead of a static import, so
   the docs engine leaves the HTML `modulepreload` set.
   *`src/components/docs/Mermaid.tsx`*
3. **`vercel.json` `headers`.** `immutable` for `/assets/*`, `must-revalidate` for `index.html`,
   short TTL for the three unhashed root SVGs. Hashed filenames only buy a stronger cache policy if
   something declares one, and `index.html` must revalidate or a stale shell points at deleted
   chunks. *`frontend/web/vercel.json`*
4. **Every image declares its priority.** `loading="lazy" decoding="async"` on all 25 `<img>`; the
   hero slideshow serves `srcset` 480w/800w/1200w with `sizes` instead of requesting Pexels at
   `w=1400` into a 672 px box (3.6× oversized). Explicit `width`/`height` was **not** added: every
   image here is already sized on both axes by CSS, so the attributes would be cosmetic.
   *15 component files*
5. **`HeroSlideshow`** stops advancing in a hidden tab and under `prefers-reduced-motion`.
6. **`Blossom` no longer injects a `<style>` per instance.** The keyframes are declared once in
   `src/index.css`; an instance contributes only `--aveline-sway-duration`. Removes ~105 duplicate
   `<style>` nodes from the landing page, and puts the sway under `prefers-reduced-motion` for the
   first time. *`src/components/auth/Blossom.tsx`, `src/index.css`*
7. **Aurora motion moved from JavaScript to CSS** — see §4.2, which is where the measurement is.
   *`src/components/site/AuroraField.tsx`, `src/index.css`*
8. **Adaptive loading.** `useConstrainedDevice()` gates decorative animation on `saveData`,
   `deviceMemory <= 4` or a 2G-class connection. `AuroraField` and `FlowerAuroraBackground` still
   render their colour; only the motion is dropped. *`src/hooks/useConstrainedDevice.ts`*
9. **Mobile layout defects M1/M2.** The dashboard sidebar is an off-canvas drawer below `lg` with a
   header toggle and a scrim (it previously left ~134 px of content column on a 390 px phone);
   `SalonPanel` is single-column below `lg` with its list capped to the top 40 %.
   *`DashboardShell.tsx`, `SalonPanel.tsx`*
10. **`min-h-screen` → `min-h-dvh`** at all 19 sites, and `SalonPanel`'s fixed height to `dvh`. The
    two `DocsLayout` `100vh` values stay: both columns are `hidden` below `lg`/`xl`.
11. **`DocsToc`** coalesces its forced-reflow `offsetTop` reads to one per animation frame and no
    longer re-registers its scroll listener on every active-section change.
12. **`theme-color`** (light + dark) and a **Pexels `preconnect`**. *`index.html`*

---

## 4. Measurements

### 4.1 Delivery

The plan predicted −73.2 % eager gzip from route splitting alone. The delivered figure is −78.6 %.
The residual entry chunk is `react-dom`, `@clerk/react`, `react-router`, `axios`, SignalR, `sonner`
and the app shell — the ~250 KB gzip floor the audit identified as genuinely shared.

Verified on the artifact rather than inferred: the eager entry chunk contains **no** `recharts`,
**no** `mermaid` and **no** `highlight.js` string, and TanStack Query's only match in it is Clerk's
internal `ClerkMockQueryClient`. All three are asserted as gates (§5).

### 4.2 Runtime — and a regression-shaped finding that was not a regression

After the route split **alone**, Total Blocking Time on the mobile profile appeared to *rise*, from
2,155 ms to 5,298 ms, with an otherwise identical byte reduction. That was investigated rather than
published or ignored, and it was a measurement artefact with a real cause: the observation window's
phase had shifted. Before, the window was spent downloading and parsing 2.7 MB of JavaScript; after,
the app mounted at ~2.4 s and the window was spent running the landing page itself.

`testing/performance/isolate-animation.cjs` settles it by measuring the same page twice under the
same profile with only `prefers-reduced-motion` differing, which switches the aurora animation off
through the component's own gate and changes nothing else:

| Landing page, mobile-slow4g profile | Long tasks | Main-thread busy |
|---|---|---|
| Framer Motion animations (before) | 80 | 3,240 ms |
| CSS animations (after) | **20** | **1,089 ms** |
| Animation off, before | 9 | 630 ms |
| Animation off, after | 7 | 623 ms |

**Two thirds of the landing page's main-thread busy was ~140 JavaScript animations**, and moving the
same keyframes into CSS removed them without changing the DOM: the animated page mounts 2,620 nodes
both before and after. This is also the first *direct* measurement of the plan's §3.6, which had
reasoned from Lighthouse's 4,431 ms of "Style & Layout". (The after-row was re-measured after the
defect in §4.4 was fixed; see that section.)

### 4.3 Metrics that did not improve, reported as such

- **CLS on good 4G moved 0.0192 → 0.0327.** It remains inside the "good" band (< 0.1) and matches
  the value the same page already showed under Slow 4G (0.0327) at baseline, so nothing here
  introduced a layout-stability problem — but it is not an improvement and is not presented as one.
- **Requests rose, 28 → 53.** Splitting the entry means the landing page fetches its own chunk and
  its shared dependencies. Total bytes fell 32 %, so this is a byte-for-request trade that is
  unambiguous over HTTP/2 (what Vercel serves) and less clearly positive over the harness's
  HTTP/1.1 server. See §6.

### 4.4 A defect this work shipped, and the gate that now catches it

The aurora blur was silently lost for one commit, and the byte- and metric-based evidence above did
not notice. It is recorded here because the failure mode is more instructive than the fix.

`AuroraField`'s blur layer was written as a template literal:

```jsx
className={`absolute rounded-full blur-[70px]${staticOnly ? '' : ' aveline-aurora-blob'}`}
```

Tailwind v4 finds candidates by scanning source text. With no separator between the utility and the
interpolation, the candidate reads as `blur-[70px]${staticOnly`, which matches nothing — so
`.blur-\[70px\]` was **never emitted into the stylesheet**. TypeScript passed, lint passed, all 1364
tests passed, `bun run build` succeeded, and the byte budgets improved. The only symptom was visual:
six 700 px gradient blobs rendered as hard-edged circles instead of a soft aurora. It was caught by
looking at the deployed page against a local build, not by any gate.

Diagnosis was `getComputedStyle(el).filter === 'none'` on the blob elements, while a sibling class in
`FlowerAuroraBackground` written as a plain `"blur-[110px]"` literal was emitted normally — which is
why exactly one component was affected.

Two things follow:

- **The fix** keeps the utility as a standalone literal, via the project's `cn()`, and the CSS now
  emits `.blur-\[70px\]{--tw-blur:blur(70px);filter:var(--tw-blur,) …}` with a computed
  `filter: blur(70px)`.
- **A gate**, `src/tailwind-candidates.test.ts`, scans `className` template literals and fails if any
  `${` is glued to a preceding character. It is deliberately narrow so it cannot cry wolf: `cn()`,
  plain literals and `\`${base} ${extra}\`` all pass.

The honest lesson for the measurements: a byte budget cannot see a class that was never generated,
and neither can a unit test that never renders the component. `testing/performance/ab-aurora.cjs`
was written for this and reports the **computed** `filter` on the blur layers under both motion
preferences — it is the check that would have caught this immediately, and the one to run after any
change to that component.

---

## 5. Tests and gates

| Gate | Where | Result |
|---|---|---|
| No static route import; every dynamic import resolves; every `lazyRoute` names a real export | `src/App.routes.test.ts` (new) | 4 passed |
| Admin registry ⇄ router parity, updated for the lazy binding form | `src/lib/admin/routes.router.test.ts` | 7 passed |
| Every `<img>` has a loading hint and `decoding="async"`; the hero no longer requests `w=1400` | `src/image-hints.test.ts` (new) | 4 passed |
| `Blossom` injects no `<style>` and carries its duration as a custom property | `src/components/auth/Blossom.test.tsx` (new) | 5 passed |
| `useConstrainedDevice` fires on each signal and only on those | `src/hooks/useConstrainedDevice.test.ts` + `.dom.test.ts` (new) | 11 passed |
| Tenant + admin design-conformance rules | existing | 15 passed |
| No Tailwind candidate glued to `${` in a `className` template literal (§4.4) | `src/tailwind-candidates.test.ts` (new) | 2 passed |
| Byte budgets, promoted Mermaid gate, entry-chunk assertions, mobile landing assertions | `testing/performance/budgets.spec.ts` | 10 passed |

Full suite: **170 files / 1369 tests passed**; all three coverage runs (global, admin ratchet,
tenant-dashboard ratchet) green at 87.7 % lines. Signed-out Playwright e2e: **19 passed, 1 skipped**.

Two existing gates earned their keep during this work:

- `routes.router.test.ts` parses `App.tsx` as source and **failed** the moment static imports became
  lazy bindings. It was updated to understand both forms, not deleted.
- `tenant-conformance.test.ts` rejected a raw `<button>` and a `bg-neutral-950/40` palette utility in
  the new dashboard scrim. Both were replaced with the `Button` primitive and a semantic token.

The two inverted (`test.fail`) gates the audit left behind — Mermaid preloading and image hints —
were **promoted to real gates**, and three were added: the landing route must not be in the entry
chunk (asserted by a landing-only hero phrase, so the budget cannot be met by deleting code), and
the entry must not reference `recharts` or `mermaid`.

---

## 6. Refuted: the chunk-grouping recommendation

The plan's §4.1 recommended adding a `manualChunks` (Vite 8: `rolldownOptions.output.codeSplitting`)
grouping "so vendor code splits deterministically rather than by graph discovery". It was
implemented, built and measured, and **measurement contradicts it**:

- Rolldown's `minSize` is only a floor *within* a group, so it does nothing about the default
  split-on-discovery fragmentation. The landing page's graph is 42 chunks, 28 of them under 3 KB,
  including one chunk per Lucide icon and four anonymous `dist-*.js` files.
- Grouping `src/components/ui/**` as `ui-primitives` was actively harmful: that directory contains
  `chart.tsx`, the Recharts entry point, so the group produced a **141 KB raw / 47,625 B gzip chunk
  that Vite then `modulepreload`ed**. Eager gzip went from 197,886 B to **236,285 B**, putting an
  admin-only dependency back on the critical path of every visit — the exact defect this work
  removes.
- The `react` and `clerk` groups did work and were byte-neutral (197,886 → 197,613 B gzip), but a
  neutral change with a config surface that sharp is not worth keeping on a theoretical
  repeat-visit caching argument.

`vite.config.ts` was reverted to its committed state and has **no** `build` block, exactly as the
audit found it. The residual fragmentation is recorded as an open item rather than shipped as a fix.

---

## 7. Deliberately not done

- **Font subsetting / self-hosting (plan §4.3).** Untouched: 235 KB across 5 requests, the largest
  remaining item. Dropping Geist Mono and narrowing variable ranges needs the design sign-off the
  plan itself flags as an open question, and the LCP font is Playfair Display, whose role is locked
  in `.agents/brain/DESIGN.md`.
- **Lazy markdown for the docs route (§4.2.2).** The 15 documents are 84 KB raw and load only on
  `/docs/*`, which is now itself a lazy chunk, so the landing page pays nothing. Making them
  non-eager requires restructuring `DocsPage`'s synchronous redirect into an async load in a route
  with no test coverage — real regression risk for a per-route win well below the top item.
- **Virtualization, `React.memo`, context `useMemo`, the `color-extractor` Web Worker (§4.5).**
  Confirmed in source, unmeasured in effect, and each changes behaviour in the authenticated app
  rather than delivery. Correct Phase 2 work.
- **`viewport-fit=cover` (M4).** Refused on purpose: `cover` without `env(safe-area-inset-*)` padding
  on the sticky chrome renders content under the notch, so it is a regression until the inset work
  ships with it. `theme-color` was taken on its own.

---

## 8. Next, in the order the measurements support

1. **Fonts (plan §4.3)** — 235 KB and 5 requests, untouched, largest remaining item.
2. **The remaining animation and DOM budget.** With the aurora switched off the landing page still
   spends 592 ms of main-thread busy, against 1,014 ms with CSS animation — so CSS animation is now
   about half the residual and the DOM itself (2,620 animated nodes vs 1,338 static) is the rest.
   Reducing the 7 `<AuroraField>` mounts to 1–2 addresses both at once. It is a visual decision.
3. **Virtualization and memoization in the conversation tree**, the only items that affect the
   authenticated dashboard rather than first paint.
4. **Production RUM.** Still absent; no number in this report is field data.

---

## 9. Reproducing this

```bash
cd frontend/web
bun run build && bun run test:coverage && bun run test:e2e

# Byte budgets + mobile landing-page gates (expect "10 passed")
HOME=/tmp NODE_PATH=$PWD/node_modules \
  PLAYWRIGHT_BROWSERS_PATH=$PWD/node_modules/.playwright-browsers \
  node_modules/.bin/playwright test -c ../../testing/performance/playwright.perf.config.ts

# Runtime comparison, Lighthouse mobile profile, applied throttling
HOME=/tmp NODE_PATH=$PWD/node_modules node ../../testing/performance/measure.cjs

# Isolate the landing page's animation budget (animated vs reduced motion)
HOME=/tmp NODE_PATH=$PWD/node_modules \
  PLAYWRIGHT_BROWSERS_PATH=$PWD/node_modules/.playwright-browsers \
  node ../../testing/performance/isolate-animation.cjs
```

`HOME` must be writable — Chrome writes its profile there and dies at startup otherwise. Absolute
figures from a container are not production figures; the isolation experiment and the byte counts
are the durable findings, and a Lighthouse run on real hardware is still the way to publish LCP.
