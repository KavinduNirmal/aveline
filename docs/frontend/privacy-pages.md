# Public Privacy & Consent Pages

> **Phase:** privacy/consent plan, Phase 7 (`7.1`–`7.6`)
> **Surface:** the public, unauthenticated frontend in `frontend/web`
> **Status:** shipped. The opt-out page calls the anonymous privacy API from Phase 4.

The transparency surface a customer meets **without an Aveline account**: the landing-page privacy
section, the full data policy the WhatsApp disclosure links to, the consent-flow explainer, and the
self-service opt-out page that proves a phone number with a one-time code.

## Routes

| Route | Component | Who reads it | Notes |
| --- | --- | --- | --- |
| `/` (landing) | `PrivacySection` in `routes/LandingPage.tsx` | prospects | Inserted **immediately after `ProblemSection`**, so "the problem" runs into "here is our commitment". |
| `/privacy` | `routes/PrivacyPage.tsx` | customers | The `{data_policy_url}` target. Reads an optional `?org={slug}` and names the shop. |
| `/privacy/consent-flow` | `routes/ConsentFlowPage.tsx` | customers | The customer-facing consent states. Non-authenticated by design. |
| `/privacy/opt-out` | `routes/OptOutPage.tsx` | customers | Reads `o` / `v` / `s` from the signed link and runs the OTP flow. |

All four are public routes in `src/App.tsx` (outside the `ProtectedRoute` tree). `SiteNav` carries a
`Privacy` tab that stays lit across `/privacy/*`; `SiteFooter` links **Privacy → `/privacy`** (it no
longer points at `/terms#privacy`).

## The opt-out flow

`lib/privacy.ts` is the only place the browser speaks to the privacy API.

```
WhatsApp disclosure
  └─ link: {App:BaseUrl}/privacy/opt-out?o={organizationId}&v=1&s={hmac}
        │
        ▼
  /privacy/opt-out  ── parsePrivacyLink() ── o (uuid) + v + s, or a refusal before any request
        │
        ├─ POST /api/v1/privacy/opt-out/start   { organizationId, phoneNumber, scope, version, signature }
        │      ← 202 { status: "accepted", handle, expiresInSeconds }   on every server path
        │
        ├─ collect the six-digit code from WhatsApp
        │
        └─ POST /api/v1/privacy/opt-out/verify  { organizationId, handle, phoneNumber, otp, scope, version, signature }
               ← 200 { status: "revoked", scope, effectiveAtUtc }   or   400 { code: "otp-invalid" }
```

**The handle is load-bearing.** The code is only verifiable against the opaque `handle` the start
call minted; the page carries it from `start` to `verify`. The verify body also carries the phone the
customer typed, but the server revokes the number the code **proved**, never the one a caller
supplied.

**Anti-enumeration is a UI contract, not just a wire one.** `start` answers the same accepted body
whether or not the number belongs to a customer, so the page never branches on existence:

- it always advances to the same neutral "If that number is registered with a boutique on Aveline…"
  copy;
- no error message says the number is unknown, unregistered, or has no record;
- the single `otp-invalid` refusal covers wrong, expired, replayed and over-attempt codes, and the
  page renders one generic sentence for all four.

**Scope.** `org` (the default) revokes the issuing boutique's record only; `all` revokes every
Aveline boutique holding the proven number. The confirmation copy reflects the scope the **server**
returned.

**Accessibility.** Every input has a `<Label>`; the scope choice is a Radix `ToggleGroup` rendered as
a labelled `radiogroup`; the flow is an ordinary `<form>` with a submit button, so Enter submits and
Tab reaches every control. State is carried by words and icons, never by colour alone.

## The data policy

`/privacy` is written to stand on its own as the disclosure's `{data_policy_url}` target. Two
positions are mandatory and are asserted by `PrivacyPage.dom.test.tsx`:

- **Controller / processor (plan §15 Q-8).** "Your boutique is the controller… Aveline is the
  processor."
- **Instagram is not covered (plan §15 Q-5).** "Instagram is not yet covered." No Instagram messaging
  provider ships and no parity with WhatsApp is promised. Do not add parity language.

It also lists what is collected and why, the AI assistance, the three consent states, the opt-out,
copy/erasure, retention, security, and contact/complaints. It must not contradict the Privacy Notice
in `routes/TermsPage.tsx`.

## Tests

| Tier | File | Covers |
| --- | --- | --- |
| Unit (`node`) | `src/lib/privacy.test.ts` | link parsing, both API calls, the code-lifetime copy, and the error mapping (including "must not say unknown number"). |
| DOM (`jsdom`) | `src/components/site/PrivacySection.dom.test.tsx` | headline, three commitments, both buttons. |
| DOM | `src/components/site/SiteNav.dom.test.tsx`, `SiteFooter.dom.test.tsx` | the nav tab and the footer link both point at `/privacy`, and nothing points at `/terms#privacy`. |
| DOM | `src/routes/PrivacyPage.dom.test.tsx` | the policy, the controller/processor statement, the Instagram gap, the consent states, `?org=` handling. |
| DOM | `src/routes/ConsentFlowPage.dom.test.tsx` | the three states in order, re-grant, the links. |
| DOM | `src/routes/OptOutPage.dom.test.tsx` | invalid link, phone + scope → start → code → verify → revoked, the `otp-invalid` path, anti-enumeration copy, the defensive missing-handle path. |
| DOM | `src/routes/LandingPage.dom.test.tsx` | the section renders, and it renders **after** `ProblemSection`. |
| E2E | `tests/e2e/privacy/opt-out.spec.ts` | the same walk in Chromium, with the two API calls intercepted (a hermetic run cannot read a WhatsApp code). |

```bash
cd frontend/web
bun run test                 # Vitest, both projects
bun run test:coverage        # the gate
bun run test:e2e -- tests/e2e/privacy/opt-out.spec.ts
```

## Known limitations

- **The opt-out link cannot name the boutique.** The signed link carries only `o` (the organization
  GUID); there is no public organization-GUID → name endpoint, and this phase deliberately does not
  add one. The page shows a safe generic confirmation, and the server's HMAC check is what rejects a
  forged `o`. The `/privacy?org={slug}` policy link does carry the slug, so the policy page can name
  the shop.
- **There is no customer-facing export/erasure page yet.** The Phase 5 API exists; the policy states
  that the pages are being added and routes those requests through `privacy@aveline.lk` until they
  ship.
