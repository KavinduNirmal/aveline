# One-Click WhatsApp (and Instagram) Onboarding — Research & Design

> **Question:** can a boutique connect a WhatsApp number by clicking "Connect", instead of
> pasting an access token, phone number ID, app secret, and webhook verify token?
>
> **Verdict:** Yes. Meta's Embedded Signup, driven by Facebook Login for Business, is
> purpose-built for this and is the only sanctioned path for a platform onboarding third-party
> businesses. It requires Aveline to become a Meta **Tech Provider**, which is gated on business
> verification and App Review. Until that clears, there is a smaller change that removes two of
> the four fields today and reduces the expiry class of failure. Confidence: **Confirmed** for the
> mechanism and the prerequisites; **Inferred** for parts of the token-lifetime model, where
> Meta's own documentation contradicts itself (see §7).
>
> **Sources:** Meta's developer documentation fetched live this session; where a page is
> client-rendered I cite a Wayback Machine snapshot and say so. Repository claims cite `file:line`.

---

## 1. Why the current model is painful — and why it is not just a UX problem

Today each tenant is asked to bring **their own Meta app**. `IntegrationService` requires four
keys per organisation (`Aveline.Api/Modules/Integrations/Services/IntegrationService.cs:15`), and
the webhook is routed **by organisation in the URL** with that tenant's own app secret verifying
the signature (`Aveline.Api/Endpoints/WebhookEndpoints.cs:37`, `:110-117`). The tenant-facing
instructions confirm this is deliberate: `frontend/web/src/docs/integrations.md:36` tells the owner
to open "the Meta App Dashboard **for your WhatsApp Business account**" and gives them a
per-tenant callback URL (`:59`).

That design is what makes the screenshot in the report possible. Three consequences follow, and
only the first is visible to the user:

| # | Consequence | Evidence |
|---|---|---|
| 1 | The tenant must understand Meta's token types well enough to generate a non-expiring one. `IntegrationsPanel.tsx:85` says "permanent token from Meta" but the field accepts anything. An expiring token is stored happily and dies later. | `IntegrationsPanel.tsx:85`; the health service only learns of it on its next pass (`IntegrationHealthService.cs:99-104`) |
| 2 | **Expiry is discovered reactively, up to 6 h late.** Meta states plainly that it will not tell you a token died. | `IntegrationHealthService.cs:40`; [Debugging and Error Handling](https://developers.facebook.com/documentation/facebook-login/guides/access-tokens/debugging-and-error-handling) — *"Facebook will not notify you that an access token has become invalid."* |
| 3 | **Recovery costs the same as setup.** Because the fault is "the credential you pasted is wrong", the only fix is to go back into Meta and paste a new one. That is the support ticket in the screenshot. | `IntegrationsPanel.tsx:268-286` |

Point 3 is the argument for Embedded Signup. It is not primarily an aesthetics change.

---

## 2. What Embedded Signup actually is

**Confirmed.** Embedded Signup is a Meta-hosted popup that walks the tenant through creating or
selecting their business portfolio and WhatsApp Business Account, adding a phone number, and
verifying it by SMS or voice code. It "uses the **Facebook Login for Business** product and our
JavaScript SDK" ([Embedded Signup overview](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/overview)).

The screen sequence the tenant sees is documented: Authentication → Authorization → business asset
selection → business asset creation → phone number addition → phone number verification →
permissions review → success ([Default flow](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/default-flow)).

Crucially for adoption: **the tenant does not need to already have a Meta Business portfolio or a
Facebook Page.** The flow offers "select an existing business portfolio or **create a new one**"
and the same for the WABA ([overview](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/overview)).
A Facebook Page is not required for plain Cloud API messaging; it is only needed if CTWA or
Click-to-Message ads are in scope ([version 4](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/version-4)).

### 2.1 What comes back, and how

**Confirmed.** Two independent channels, both required:

1. **A `postMessage` event** to the window that opened the popup, with
   `type: 'WA_EMBEDDED_SIGNUP'` and `data: { phone_number_id, waba_id, business_id }`. The
   `event` field distinguishes `FINISH`, `FINISH_ONLY_WABA`, `ERROR`, and `CANCEL`.
   A reference implementation reads exactly these fields
   ([`MetaEmbeddedSignup/embeddedSignup.js`](https://raw.githubusercontent.com/rk8672/MetaEmbeddedSignup/main/backend/webhook/embeddedSignup.js)).
2. **A one-time `code`** in the `FB.login` response callback.

The browser hands the `code` to Aveline's backend, which exchanges it server-to-server:

```
GET https://graph.facebook.com/<VERSION>/oauth/access_token
    ?client_id=<APP_ID>&client_secret=<APP_SECRET>&code=<CODE>
```

**The code's time-to-live is 30 seconds** ([Onboarding as a Tech Provider](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/onboarding-customers-as-a-tech-provider)).
This constrains the design: the exchange must be a single backend round trip, not a queued job.

### 2.2 The token you get is not the token you have been asking for

**Confirmed.** A Tech Provider receives a **Business Integration System User access token** —
"scoped to individual onboarded customers", issued to the integration rather than to a person:
*"If you are a Tech Provider, you will use business tokens exclusively"*
([Access tokens](https://developers.facebook.com/documentation/business-messaging/whatsapp/access-tokens)).

`frontend/web/src/docs/integrations.md:40` currently asks for "a permanent/system-user token". In
the Embedded Signup world that is the right *family* but the wrong *actor*: the token belongs to
Aveline's integration, not to the tenant.

**Meta does not document whether the token is attached to the tenant's app or to Aveline's app
explicitly**, and whether Embedded Signup re-keys it. What is confirmed is that the token is
returned in exchange for **Aveline's own `client_id` + `client_secret`** — so from Aveline's
perspective it is a token Aveline can use for that tenant. That is the operative fact for the
design.

---

## 3. What Aveline must do once — the real cost

**Confirmed.** This gate is the reason the answer is "yes, but not this sprint".

| Prerequisite | Why it is required |
|---|---|
| Business portfolio for Aveline, with **business verification** | "Your business must be verified **before** you can start the app review process." ([Become a Tech Provider](https://developers.facebook.com/documentation/business-messaging/whatsapp/solution-providers/get-started-for-tech-providers)) |
| Two-factor authentication on the portfolio | Same page. |
| Enrol as a **Tech Provider** in App Dashboard → WhatsApp → Quickstart | Accepts Meta's Tech Provider terms. |
| **App Review for Advanced Access** to `whatsapp_business_management` **and** `whatsapp_business_messaging` | *"If you lack Advanced access for a given permission, your clients cannot grant your app that permission via Embedded Signup."* ([Permissions](https://developers.facebook.com/documentation/business-messaging/whatsapp/permissions)) |
| Screen recordings for App Review | One per permission: sending a message (`..._messaging`) and creating a template (`..._management`). Reference: [Twilio Tech Provider guide](https://static0.twilio.com/docs/whatsapp/isv/tech-provider-program/integration-guide.md) |
| **Access Verification** after approval | ~5 business days; establishes Tech Provider status. |
| A Facebook Login for Business **configuration** (`config_id`) | Selects token type, assets, permissions. |
| The app in **Live** mode | "some webhooks will not be sent if your app is in Dev mode." ([Webhooks overview](https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/overview)) |

Two more constraints that shape the plan:

- **Onboarding volume is gated.** By default a new Tech Provider can onboard **10 businesses per
  rolling 7-day window**; completing business verification, App Review, and Access Verification
  raises the cap to **200** ([Become a Tech Provider](https://developers.facebook.com/documentation/business-messaging/whatsapp/solution-providers/get-started-for-tech-providers)). Wave-1 rollout must respect this.
- **Portfolio separation is mandatory.** Aveline's business portfolio "must be... separate from
  the business portfolio owned by your business client"
  ([Facebook Login for Business](http://web.archive.org/web/20251112160914/https://developers.facebook.com/docs/facebook-login/facebook-login-for-business) — Wayback snapshot; the live page is client-rendered).
- **Version deadline.** Embedded Signup **v2 and v3 are deprecated on 2026-10-15**; build on **v4**
  ([Versions](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/versions)).

**Timeline is dominated by Meta, not by Aveline.** Business verification is documented as taking
"several weeks" in some regions and Access Verification "typically 5 business days". The code work
is days; the gate is weeks to months, and none of it is engineerable.

---

## 4. What still will not be click-only

Being honest about the residual manual surface prevents a second disappointment. Even after
Embedded Signup, the tenant must:

1. sign in with their own Facebook / Meta Business credentials — unavoidable;
2. accept Meta's terms (Cloud API, WhatsApp Business, Meta Business Tools);
3. enter and OTP-verify the phone number — this is only possible with a number they control, and
   if that number is currently registered on the WhatsApp Business *app* it must be migrated or
   freed first;
4. choose a display name, which Meta verifies asynchronously and can decline;
5. complete their **own business verification** to escape the starting 250-unique-recipients/24 h
   limit;
6. **attach a payment method to their WABA before any outbound message can be sent.** Meta requires
   the customer's WABA to have a payment method unless Aveline is a Solution Partner with a shared
   line of credit. This is the largest remaining manual step and it is meta-level, not
   Aveline-level.

Steps 5 and 6 happen **after** the popup closes, asynchronously. The UI must therefore treat
"Connected" as a state machine (popup done → token stored → WABA subscribed → number registered →
payment attached → ready to send), not a boolean. `IntegrationStatus` already has
`Pending/Connected/Error/Expired`
(`Aveline.Api/Modules/Integrations/Models/IntegrationStatus.cs`); it needs finer granularity on
the onboarding axis, not new statuses bolted on.

---

## 5. Options

### Option A — Do nothing

Keep the four-field form. Improve only the copy and the inline hint.

- **Buys:** nothing, zero risk.
- **Costs:** the support burden in the screenshot continues, for every tenant, forever.
- **Forecloses:** nothing.
- **Verdict:** rejected. But note its close relative, Option B.

### Option B — Make the *manual* path as small as it can be (interim, ~1-2 days)

Keep per-tenant credentials; remove two of the four fields.

- **Mechanism:** the **app secret** and the **webhook verify token** are *Aveline's* values, not
  the tenant's. Meta's docs use the app secret for the HMAC and call the verify token "a
  verification string of your own choosing" — there is no Meta documentation anywhere instructing a
  platform to collect either from a tenant, and I searched the Embedded Signup, Tech Provider,
  webhook, and access-token doc sets for one (**NOT FOUND**). So Aveline can supply both from its
  own configuration: one deployment-wide `Meta:AppSecret`, one server-generated
  `Meta:WebhookVerifyToken`. The tenant pastes only **access token + phone number ID**.
- **Buys:** halves the field count and eliminates the entire class of "the tenant entered the wrong
  app secret" failures immediately, before any Meta approval.
- **Costs:** the webhook becomes single-app-verified while the credential blob is still per-tenant,
  i.e. a hybrid state that must be documented.
- **Forecloses:** nothing; it is a strict subset of Option C.
- **Verdict:** **do this now.**

### Option C — Full Embedded Signup with a single shared webhook endpoint (the target)

- **Mechanism:** one Meta app owned by Aveline; one Facebook Login for Business `config_id`; a
  "Connect WhatsApp" button that calls `FB.login`; a backend endpoint that exchanges the code,
  subscribes the tenant's WABA, registers the phone number, and persists the mapping; one public
  webhook URL at `/api/v1/webhooks/whatsapp` that verifies the signature with **Aveline's** app
  secret and resolves the tenant from the payload.
- **Buys:** two clicks for the tenant; no secret ever leaves Aveline's backend; a durable
  (documented "defaults to never expire") business token; per-tenant expiry visible to Aveline
  rather than only to Meta.
- **Costs:** the Tech Provider gate in §3; the blast-radius change in §6.1; a rewrite of the
  webhook router (§6.3).
- **Verdict:** **target state.**

### Option D — Full Embedded Signup, but keep per-tenant webhook URLs via `override_callback_uri`

- **Mechanism:** at the end of onboarding, call
  `POST /{WABA_ID}/subscribed_apps` with `override_callback_uri` and `verify_token`
  ([Webhook overrides](https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/override)),
  preserving `…/webhooks/whatsapp/{organizationId}`.
- **Buys:** no change to the existing route contract; per-tenant URL isolation.
- **Costs, all confirmed against the current Meta docs:**
  - **`account_update`, `account_alerts`, `account_review_update` and every
    `message_template_*` webhook can never be overridden** — "Meta always delivers these webhooks to
    your app's default callback URL". So a shared endpoint plus payload routing must be built
    **anyway**. Option D does not remove the work in §6.3; it adds to it.
  - The override URL is capped at **200 characters**.
  - A plain (body-less) re-subscribe **silently wipes** the override, so every future
    onboarding/repair path must remember to re-send it.
- **Verdict:** rejected as the primary design. It buys a routing table Aveline has to build
  regardless, and adds state that can be lost.

---

## 6. Target design (Option C)

### 6.1 Architectural consequences to accept up front

1. **One app secret verifies all tenants.** `WebhookSignatureVerifier` itself needs no change — it
   is already generic HMAC-SHA256 with a constant-time compare
   (`Aveline.Api/Modules/Integrations/Services/WebhookSignatureVerifier.cs:17-44`). The *key source*
   moves from per-org encrypted credentials to one process-level secret. The current
   verify-before-rate-limit ordering (`WebhookEndpoints.cs:97-117`) stops being a nicety and
   becomes essential: on a shared endpoint, an unauthenticated flood must not be able to burn every
   tenant's rate-limit budget. The rate-limit key must move off `{organizationId}` (currently
   `WebhookEndpoints.cs:121`) to the *resolved* tenant.
2. **Blast radius is fleet-wide.** Rotating the app secret, or losing App Review status, affects
   every tenant simultaneously — where today a bad credential affects one organisation. This is a
   real regression in fault isolation and should be recorded as accepted, with mTLS considered as
   the eventual replacement for the IP allow-list (mTLS is app-level only; Meta states it is "not
   supported at the WABA or business phone number level").
3. **The webhook router must be rewritten.** This is the largest single piece of work and the one
   with a latent correctness bug already:
   - `WhatsAppEntry` has **no `Id`**, and `WhatsAppMetadata` has **no `PhoneNumberId`**
     (`WebhookEndpoints.cs:457`, `:480`), so the tenant identifiers Meta sends are parsed away.
   - `ExtractMessage` uses `FirstOrDefault()` on every level
     (`WebhookEndpoints.cs:435-438`). Meta documents that POSTs are **batched, up to 1000 updates**,
     with "batching cannot be guaranteed so be sure to adjust your servers to handle each POST
     request individually." Today that silently drops everything after the first element. With one
     org per URL that is a throughput bug; on a shared endpoint it means **one tenant's message in
     a batch can starve another tenant's**.
   - Routing key is `entry[i].changes[j].value.metadata.phone_number_id` — per *change*, not per
     request — with `entry[i].id` (the WABA ID) as a fallback for account-level webhooks. A WABA
     owns multiple numbers, so `phone_number_id` is the correct join key; `display_phone_number`
     must not be used.
4. **`phone_number_id` must become a queryable, unique column.** It is currently inside the
   encrypted blob as a pasted string (`IntegrationService.cs:15`). Evidence: `grep -rn "wabaId" Aveline.Api`
   returns **zero** matches; the concept does not exist in the codebase yet. A unique index on
   `phone_number_id → organization_id` is what makes "route by payload" safe, and it is a
   migration, not a field.

### 6.2 Interfaces

**New: backend exchange endpoint** (JWT-protected, `BoutiqueMembershipManagePolicy` so only an
owner of the target org can bind a number to it).

```
POST /orgs/{organizationId}/integrations/whatsapp/embedded-signup
{ "code": "<one-time code>", "wabaId": "...", "phoneNumberId": "...", "businessId": "..." }

200 → IntegrationStatusDto   // same shape the UI already handles
400 → { "message": "..." }   // expired/reused code (Meta returns 400 "Matching code was not found or was already used")
409 → { "message": "This WhatsApp number is already connected to another boutique." }
```

The `wabaId`/`phoneNumberId`/`businessId` from the popup are **client-supplied and therefore
untrusted**. The server must not persist them on the client's word: after the exchange it should
read `GET /{WABA_ID}` and `GET /{PHONE_NUMBER_ID}` with the new token, confirm the pair is
consistent, and only then write. This is the mitigation for a hostile tenant claiming someone
else's `phone_number_id`.

**New: Meta browser flow** — a thin `MetaEmbeddedSignup` frontend module that loads the JS SDK
**only on the settings/onboarding step** (Meta's own guidance), opens `FB.login` with
`config_id`, `response_type: 'code'`, `override_default_response_type: true`, and posts the
`WA_EMBEDDED_SIGNUP` payload plus the `code` to the endpoint above. No token touches the browser.

**Unchanged:** `IntegrationService.SaveAsync/TestConnectionAsync`, `IIntegrationService`,
`IntegrationEndpoints`' existing routes. The manual four-field `PUT` stays as the escape hatch for
tenants whose number cannot go through Embedded Signup (for example, a number already on another
BSP), and for local development where no Meta app exists.

### 6.3 Webhook data flow (target)

```
GET  /api/v1/webhooks/whatsapp              ← Meta's verification challenge
     compare hub.verify_token to Meta:WebhookVerifyToken (constant-time)

POST /api/v1/webhooks/whatsapp
  1. IP allow-list (unchanged)                      WebhookEndpoints.cs:83-95
  2. Read + buffer raw body                         :101-107
  3. Verify X-Hub-Signature-256 with Meta:AppSecret  :109-117  (verify BEFORE rate limiting)
  4. Parse; for EACH entry → EACH change:
       resolve tenant by value.metadata.phone_number_id (fallback: entry.id → WABA)
       if unresolved → log + skip THIS change, keep processing the batch
  5. Rate limit per resolved tenant + IP
  6. For each message: persist InboundMessageLog, publish message.received
  7. Return 200
```

Failure modes that must be decided, not discovered:

- **An unknown `phone_number_id`** (a WABA subscribed but not yet mapped, or a mapping deleted) must
  be logged and skipped, never 500 — a non-200 makes Meta retry for up to 7 days.
- **A partially resolvable batch** must still deliver the resolvable changes. Meta retries
  duplicates; `InboundMessageLog.ExternalId` dedup already handles that
  (`WebhookEndpoints.cs:168-174`).
- **The persisted `message.received` event payload must carry the resolved `organizationId`**, not
  one derived from the route, since the route no longer has one.

### 6.4 Token lifecycle

Do not assume either lifetime. Meta says business tokens "default to never expire", while the
Embedded Signup implementation page directs you to a configuration template named "With 60
Expiration Token", and the token-management API exposes `set_token_expires_in_60_days`. These are
contradictory and **unresolved**; design for both:

- At exchange time, call `GET /debug_token?input_token=<token>` and persist `expires_at`,
  `issued_at`, `data.type`, and `scopes` into `IntegrationCredential.Metadata` (the column is
  already described as "e.g. WhatsApp phone number or token expiry",
  `Aveline.Api/Modules/Integrations/Models/IntegrationCredential.cs:24`).
- If `expires_at` is a real timestamp, alert at T-30/T-14/T-7 days. If it is `0`, treat as
  non-expiring. `expires_at: 0` meaning "never" is **Inferred** — Meta's sample data supports it but
  no page states it in words.
- Replace the 6-hour health pass's role for *known* expiries with a calendar job; keep the 6-hour
  probe as the backstop. This is what turns a 16-minute-late "Expired" badge into a three-week
  warning. It is worth doing even if Embedded Signup is never adopted, and it is cheap: Option B
  plus this alone would have prevented the reported incident.
- Handle `190` (with subcodes `463` expired / `460` invalidated / `458` app de-authorized) and
  `200`/`200-299` (permission not granted — *not* the same as expiry) distinctly at every call site.

---

## 7. Open questions, contradictions, and unknowns

These are places where the honest answer is "Meta's docs do not settle it" and a decision is needed.

1. **Is the Embedded Signup token attached to the tenant's app or Aveline's?** Meta's public docs do
   not state this explicitly for Embedded Signup. The confirmed, load-bearing fact is that the token
   is issued in exchange for **Aveline's** app credentials. **What would settle it:** run one real
   Embedded Signup in Dev mode and inspect `debug_token`'s `app_id` and `type`. **Decision needed
   before Phase 2.**
2. **Does the business token ever expire?** See §6.4. Unresolved in Meta's own documentation.
3. **Is there any way to re-mint an expired business token without re-running Embedded Signup?**
   **NOT FOUND.** Assume expiry = re-onboard. This is a strong argument for the single
   `debug_token`-driven expiry job (cheaper than an incident).
4. **`/{app-id}/subscriptions` contradiction.** One Meta page says to configure WhatsApp webhooks
   with the Application Subscriptions API; the Graph API reference for that edge says "Webhooks for
   WhatsApp is not supported." Treat the App Dashboard as the guaranteed path and verify with
   `GET /{app-id}/subscriptions` before relying on the API.
5. **Retry window contradiction:** 36 hours (Graph API webhooks page) vs 7 days (WhatsApp page).
   Design for 7 days.
6. **Does Meta issue a `GET` verification challenge to an override URL?** Undocumented. If
   Option D is ever revisited, implement the same verify handler on every URL that can receive one.
7. **Instagram is a separate decision, not a copy of this one.** Aveline stores Instagram
   credentials but has **no Instagram provider** — `Providers/` contains only WhatsApp
   (`Aveline.Api/Modules/Integrations/Services/Providers/`) and
   `IntegrationService.cs:132-138` marks every non-WhatsApp type `Connected` on save without
   contacting Meta. If Instagram is pursued, Meta documents **Instagram API with Instagram Login**
   as the simplified path ("an average of 12 steps to just two",
   [migration guide](https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-instagram-login/migration-guide)),
   which is *not* the Facebook Login path that `IntegrationsPanel.tsx:98-100` currently implies.
   Note also: *"Your app can either use Facebook Login or Instagram Login but not both"*
   ([App Review](https://developers.facebook.com/documentation/instagram-platform/app-review)).
   That is a real conflict with the WhatsApp plan if both are wanted under one app.
8. **The 2026 account-model change.** Meta states that from Phase 1 (H2 2026) `waba_id` in message
   events refers to a *Messaging Account ID*, with `waac_id` arriving in Phase 2 (H1 2027). Any
   routing table built on `waba_id` should store it as an opaque string and expect it to change
   meaning.

---

## 8. Recommendation

**Sequenced, because the gate is Meta's and the pain is Aveline's.**

**Now (no Meta dependency):**
1. **Option B** — move `appSecret` and `webhookVerifyToken` to deployment configuration; the tenant
   pastes only *access token + phone number ID*. Update `IntegrationsPanel.tsx:84-89` and
   `frontend/web/src/docs/integrations.md:34-51` accordingly. This is a strict subset of the target
   design and removes two thirds of the field-level support surface.
2. **Proactive expiry** — add a `debug_token` call at save/test time that persists `expires_at` and
   `type`, and alert at T-30/T-14/T-7. This alone prevents the reported incident class.
3. **Fix the batched-payload bug** — flatten the `entry`/`changes`/`messages` loops in
   `ExtractMessage`. It is a correctness fix today and a prerequisite for Phase 2.

**Parallel, non-engineering (start immediately, it is the critical path):**
4. Business verification on Aveline's Meta business portfolio, 2FA, Tech Provider enrolment, and
   preparation of the two App Review screen recordings. Create a *second* test business portfolio
   for Dev-mode Embedded Signup testing — Meta does not allow testing with the portfolio that owns
   the app.

**After Advanced Access is granted (Phase 2):**
5. Embedded Signup v4: the `config_id`, the browser flow, the `/embedded-signup` exchange endpoint,
   `POST /{WABA_ID}/subscribed_apps`, `POST /{PHONE_NUMBER_ID}/register`, the `phone_number_id`
   routing column, and the webhook router rewrite per §6.3.
6. Keep the manual `PUT` path as the documented escape hatch. Do not delete it.

**Phase 3 (separate decision):** Instagram. Resolve the mutual-exclusivity conflict in §7.7 first;
it may force a second Meta app, which changes the webhook story again.

---

## 9. Risks

| Risk | Severity | Mitigation | Signal it materialised |
|---|---|---|---|
| App Review rejected or stalls | High | Prepare recordings early; keep Option B as the permanent fallback | No decision after ~4 weeks past submission |
| Fleet-wide app-secret rotation drops all tenants' webhooks | High | Keep verify-before-rate-limit; dedupe on `ExternalId`; rehearse rotation; consider mTLS | Spike in signature failures across many orgs at once |
| `phone_number_id` uniqueness violated (tenant binds a number already bound) | Medium | Unique index + pre-flight check in the exchange endpoint | `409` rate; duplicate threads on one number |
| Unknown `phone_number_id` causes 500s, Meta retries 7 days | Medium | Skip-and-log per change; alert on unresolved rate | Non-200s in webhook logs; Meta retry storms |
| Token does expire at 60 days despite the "never" documentation | Medium | `debug_token`-driven expiry job from day one | `expires_at != 0` on a real integration |
| Embedded Signup v4 mechanics differ from the v3 examples in secondary sources | Medium | Read the v4 page directly before implementing; test in Dev mode against a throwaway portfolio | Popup returns no `code`, or `WA_EMBEDDED_SIGNUP` payload lacks `phone_number_id` |
| Onboarding volume cap (10/7 days before verification) throttles launch | Low | Raise via verification before Wave 1 | Meta error on the Nth onboarding in a week |

---

## 10. What would change this conclusion

- If Aveline's business verification is refused, Embedded Signup is **not** reachable and Option B
  becomes the long-term answer, not the interim one. That is the single most important fact to
  establish, and it is not answerable from the repository.
- If a real Embedded Signup reveals the token is attached to **Aveline's** app but scoped such that
  a revoked tenant blocks others, the blast-radius graph changes and Option D's per-tenant
  isolation becomes more attractive despite its costs.
- Instagram's mutual-exclusivity rule (§7.7) could force a second Meta app, which would re-open the
  webhook design.
