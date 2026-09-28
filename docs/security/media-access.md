# Media Access — Aveline's Two-Tier Media Model

> **Scope:** how image and document bytes are stored, who may read them, and how a protected
> read is authorised. This document describes the shipped implementation
> (`Aveline.Api/Modules/Media/**`, `Aveline.Api/Modules/Conversations/Attachments/**`,
> `Aveline.Api/Modules/Conversations/Media/**`) and the frozen decisions recorded in
> [ADR-022](../ADR/ADR-022-media-storage-and-access.md).
>
> The outbound-fetch threat model is **not** repeated here: it is the S6 security review,
> [`image-url-fetch-review.md`](image-url-fetch-review.md). This document cross-references it.

---

## 1. The two-tier model

The tier is decided by the **Cloudinary delivery type**, never by the caller.

| Tier | Cloudinary type | Who may obtain the bytes | Mechanism |
|---|---|---|---|
| Catalog imagery | `upload` | anyone (F-7/Q4) | an absolute CDN URL; no token, no Aveline hop |
| Conversation attachment, client-facing | `authenticated` | an authenticated org member who can see the conversation | the existing attachment route under `BoutiqueConversationAccessPolicy`; **unchanged** |
| Conversation attachment, machine-facing | `authenticated` | any holder of a minted token | `GET /api/v1/media/{token}` — HMAC + `exp` + scope (+ single-use nonce) |
| Vision analysis input | `authenticated` (read server-side) | the API only | the API loads the stored bytes and hands the provider an inline `data:` URL; no token is minted |
| Conversation PDF | `authenticated` / `raw` | as the conversation tier | same answer as a photo in the same thread |

- **The catalog route is deliberately anonymous** (`CatalogEndpoints` `GET …/catalog/images/{id}`,
  `.AllowAnonymous()`), and the reason is recorded on the route and in ADR-022. For a Cloudinary
  row it answers `302` to the absolute CDN delivery URL; for a database row it streams bytes with
  `Cache-Control: public, max-age=31536000, immutable` and `nosniff`. Product photography is a
  public artefact, and the GUID is unguessable but not secret.
- **The protected response is never cached.** `GET /api/v1/media/{token}` sets
  `private, no-store` before any branch — on a failure as much as on a success — and adds
  `nosniff` and `Content-Disposition: inline` on success. The catalog route's public cache header
  must never be copied here.
- **`MessageAttachment.Url` stays the authenticated Aveline route.** A Cloudinary URL is not a
  stable value to persist (signed is permanent, expiring is not, `authenticated` is neither), and
  every reader uses the stored field rather than building a path.

## 2. The scopes

A token carries exactly one scope, bound into the MAC. The wire names are frozen
(`MediaScopeNames`):

| Scope | Tokenised? | Meaning | Minted by |
|---|---|---|---|
| `asset.public` | **No** | Catalog imagery. Public by F-7/Q4, so a token would add nothing | never minted |
| `attachment.view` | Yes | One conversation attachment read by an authorised holder | `POST …/conversations/{conversationId}/attachments/{attachmentId}/media-token` |
| `vision.analyze` | Yes | One analysis by the vision provider; **single-use** | `POST /internal/visual/media-token` (and the `/api/internal/visual` alias) |

- `GET /api/v1/media/{token}` accepts `attachment.view` **or** `vision.analyze` and nothing else.
- The internal mint may request `vision.analyze` only; naming `asset.public` or
  `attachment.view` there is `403`, and an unknown scope name is `400`.
- The member mint serves `attachment.view` only; a body naming any other scope is `403` before the
  row is read.
- `HmacMediaUrlSigner` refuses to mint an `asset.public` token at all: catalog imagery carries no
  token (F-7/Q4).

## 3. The bearer-URL consequence (accepted, Q4)

**A minted token URL is a bearer credential.** Anyone who holds it can fetch the image until
`exp`. This was accepted explicitly (Q4) rather than inherited silently, because a third-party
vision provider cannot hold a credential of ours: it must be handed a fetchable, unauthenticated
URL. The vision reference arm no longer hands over such a URL: it loads the stored asset's bytes and
passes the provider an inline `data:` URL, because the minted URL is served by this API on an
in-network origin (`http://api:8080`) that the provider cannot reach, and the stored bytes remove
the reachability requirement entirely. The bearer-URL consequence still applies to every URL that
is minted for a protected read.

Within the TTL, a leaked URL is usable by anyone who holds it. That residue is the honest cost of
the design. What bounds it:

1. **A short `exp`.** `Media:VisionTokenTtlSeconds` defaults to 600, `Media:AttachmentTokenTtlSeconds`
   to 900, with hard caps of 1800 s and 3600 s enforced in the signer. A caller cannot mint a
   30-day token.
2. **A single-use nonce for `vision.analyze`.** A replay is refused; the second presentation of a
   vision token fails. Single-use is a property of the scope, not a caller's preference.
3. **Scope.** A leaked vision token cannot read an unrelated attachment, and an attachment token
   cannot drive an analysis.
4. **Tenant binding.** The token's organisation is checked against the row's organisation on every
   read.
5. **`clock skew` is bounded** at `Media:ClockSkewToleranceSeconds` (30 s), so expiry is not
   effectively extended by a large tolerance.

The MAC covers the **entire encoded payload**, not the identifier. A scheme that signed only the
public id would let an attacker splice a valid MAC from asset A onto a payload naming asset B; the
splice case is a test (`MediaTokenVectorTests`).

## 4. The fixed verification order and the status matrix

`HmacMediaUrlSigner.Verify` checks in this order, and the order is part of the contract:

**MAC → `exp` → scope → single-use nonce.**

- **MAC first**, so an attacker cannot distinguish "wrong key" from "expired" from "no such asset"
  by response code, and so no claim is read before the payload is proven to be written by the key
  holder.
- **`exp` before scope**, so an expired token is `401` regardless of the scope asked for.
- **Scope before nonce**, so a wrong-scope presentation can never consume a legitimate single-use
  claim, and a nonce is claimed at most once.
- **Nonce last.** A refused presentation must not burn the claim.

| Condition | Result |
|---|---|
| Malformed token, bad MAC, expired, replayed nonce | `401` `{ message: "The media token is not valid." }` — deliberately uninformative |
| Wrong scope for this route | `403` `{ message: "The media token is not valid for this resource." }` |
| Token verifies but the row does not exist (or is another tenant's) | `404` `{ message: "Media not found." }` |
| Nonce store unavailable | `503` — the claim cannot be proven, so the request is refused |
| Provider rate-limited (`420`) | `503` with a correlation id |
| Provider timeout (`408`/`504`) | `504` with a correlation id |
| Any other provider failure | `502` with a correlation id |

A provider failure is **never** silently replaced by the row's copy: a denied asset is an error
status, not a fallback image.

## 5. The fail-closed nonce rule

**An unverifiable single-use claim must not become a served asset.** So the nonce store fails
closed in every failure mode:

- Redis throws → `RedisMediaTokenNonceStore` logs at `Error` (with no nonce value in the line) and
  returns `Unavailable` → the route answers `503`.
- Redis is not configured at all → `UnavailableMediaTokenNonceStore` refuses **every** claim, so a
  host without Redis can mint a `vision.analyze` token but can never serve one.
- The nonce key's TTL is the token's remaining lifetime, so an evicted key cannot outlive the
  token.

**This is deliberately the opposite direction from the agent service's rate limiter, which fails
open** (`agent-service/app/middleware/rate_limit.py`: an unreachable Redis logs a warning and the
request is allowed through). The distinction is intentional: a rate limiter that fails open
degrades a protection; a single-use store that failed open would turn a one-shot credential into a
replayable one, which is a grant, not a degradation.

## 6. Tenant binding

- Every mint re-checks that the referenced row belongs to the request's organisation.
- The member mint resolves visibility through `GetAttachmentAsync(organizationId, userId, …)`, so
  an invisible or cross-org conversation is `404` and **never mints**.
- The internal mint takes `organizationId` explicitly; a reference that resolves to another
  tenant's row is `404`, never a token.
- On the read side, the locator resolves the asset **by the token's organisation and public id**,
  so a token cannot be replayed against a row in another tenant even if a public id collided.
- **A tag or a `context` value is never an authorisation input.** The tenant-proof dimension is the
  public-id prefix plus the applied organisation filter; the metadata is audit and console
  material only.

## 7. The outbound-fetch guard

The pasted-image-URL fetcher (`Modules/Conversations/Media/**`) is the one genuinely new outbound
attack surface in this workstream, and its full threat model, its fourteen behaviours, its
findings (F1–F9) and its kill switch are documented in
[`image-url-fetch-review.md`](image-url-fetch-review.md). It is **not** duplicated here. Two
operator facts from that review belong on this page:

- The feature is **off by default** (`Media:ImageUrlUploadEnabled=false` in `MediaOptions.cs` and
  `appsettings.json`). In the shipped configuration the whole surface is closed and a pasted URL
  is answered with an explicit `400 { code: "feature-disabled" }`.
- An operator enabling it should also set `Media:ImageUrlAllowlist`; the allow-list is the
  documented mitigation (review F1/F6). There is no per-org quota on that path.

## 8. The URL-redaction rule

`GET /api/v1/media/{token}` carries a bearer credential in its **path**. A token in a log file or a
trace backend is the same exposure as a token in a response, so redaction is centralised in one
type — `RequestPathRedaction` — and every sink asks it for the safe form:

| Sink | Where | What it records |
|---|---|---|
| The 401/403 authorization audit | `LoggingConfiguration.UseAvelineAuthAudit` | `RequestPathRedaction.SafePath(context)` — never the raw path |
| The global exception handler | `Common/Exceptions/GlobalExceptionHandler` | `RequestPathRedaction.SafePath(httpContext)` in the `Unhandled exception` line |
| The OpenTelemetry span exporter | `ObservabilityConfiguration.CredentialAttributeRedactionProcessor` | `OnEnd`: the display name and **all** string attributes are swept through `TryRedactValue` |

`RequestPathRedaction` has two rules, in order:

1. When routing has resolved an endpoint, the endpoint's **route template** is the safe form. A
   template is credential-free by construction, so any future route that puts a secret in a path
   parameter is covered the moment it is mapped, without being registered.
2. When no endpoint resolved (a refusal or failure before routing, or a media-shaped path that
   matched nothing), the registered credential families are matched by prefix and replaced with
   their placeholder. The only row is `/api/v1/media` → `/api/v1/media/{token}`.

`MediaTokenPathRedactionMiddleware` remains as pipeline-level containment for any future reader of
`HttpContext.Request.Path` that does not ask the rules first: it rewrites the path to the template
**after** routing has bound the `{token}` parameter, so the endpoint still runs while whatever
observes the path afterwards observes the template.

**Add a row to `RequestPathRedaction.CredentialPaths` when a new route carries a secret in its
path.** The audit middleware, the exception handler and the span exporter all read that one list.

## 9. `noeviction` is an operational prerequisite

**The Redis instance must run with `maxmemory-policy noeviction` before the protected tier is
treated as production-ready.** This is an operational fix, not a code one: `AllowAdmin` is false
and the connection string is bare, so the application cannot set `maxmemory` or the policy itself
(`CONFIG SET` is also unavailable on Redis Cloud). One `command:` line locally; one provider-side
setting for the managed instance.

**Why an evicted nonce is a fail-open.** Under `volatile-lru` — Redis Cloud's documented default —
eviction candidates are exactly the **TTL-bearing** keys, and this application has four of them:
the distributed job lock, the idempotency lease, the quota counters, and the new
`aveline:media-nonce:*` key. A nonce evicted before its TTL means a `vision.analyze` token that has
already been used can be presented **again**: the store answers `Claimed` for a nonce it no longer
remembers, and the single-use guarantee silently disappears. That is a fail-open in an access
control, and it is the opposite of the fail-closed rule in §5.

Two related facts:

- The compose Redis in `docker-compose.yml` is unbounded: no `command:`, no `maxmemory`, no
  eviction policy and no volume, so it runs with Redis 7 defaults (unlimited memory, `noeviction`)
  and grows until it is OOM-killed rather than degrading. That is safe for the nonce but is a
  capacity problem of its own.
- The image pipeline adds **zero** Redis operations per image view or upload. The only additions
  are one `SET NX` per `vision.analyze` analysis, and — if the analysis cache is ever wired —
  1 GET + 1 SET per analysis. The cache module is **re-keyed to `{orgId}:{publicId}` and
  deliberately not wired**; it runs on no path today.

---

## 10. The web attachment picker — shipped behaviour and residuals

> **Scope.** The web dashboard's file picker and the flow it drives: the composer affordance, the
> pending tray, the client seam, and interactive attachment rendering under
> `frontend/web/src/components/conversation/`, `frontend/web/src/contexts/ConversationsContext.tsx`
> and `frontend/web/src/lib/` (`attachment-preparation.ts`, `conversations-api.ts`). The two-tier
> access model above is unchanged by it; this section records what the web picker does, the product
> decisions it carries, and what is **not** yet proven. The mobile client has run the same flow since
> the client-thread workstream, so most of this is parity rather than new policy.

### 10.1 Upload on pick, and the 24 h sweep

A picked file is uploaded **as it is picked**, not when the message is sent. The upload route stores
the row **unbound**; the send binds it by naming its `attachmentId` in the message's `attachmentIds`.
That is the API's designed two-step flow, and the picker follows it rather than buffering bytes until
send.

The consequence is deliberate and covered: a pick the user abandons — a cancelled composer, a send
that failed validation, a tab that never returned — leaves an unbound row and its provider asset
behind until `AttachmentSweepJob` collects it. The TTL is 24 hours and the sweep runs hourly, and it
releases the provider asset before deleting the row. **That is the designed cleanup, not a leak.**

The pending tray lives in `ConversationsContext`, keyed by conversation id, so switching dashboard
sections (which unmounts the Salon panel) does not silently orphan an upload the user still means to
send. A full page reload still loses the tray; the sweep covers those rows. There is deliberately **no
attachment-delete route**: removing a chip leaves an already-stored upload to the sweep, and the
composer never claims it can un-store bytes.

### 10.2 The caps, and where each is enforced

| Cap | Value | Client | Server |
|---|---|---|---|
| Per file | **5 MB** | `MAX_ATTACHMENT_BYTES` in `lib/attachment-preparation.ts`; `prepareAttachment` refuses before any request | `MediaContentTypes.MaxFileBytes` (`Aveline.Api/Common/Media/MediaContentTypes.cs`), checked in the upload handler **before anything is written** |
| Per message | **5 files** | `MAX_ATTACHMENTS_PER_MESSAGE`; `checkAttachmentCap` runs in the composer before `onAttach`, so the sixth file never starts an upload | `MediaContentTypes.MaxPerMessage`, enforced when the send binds its ids; over the cap is an `AttachmentBindingException` → `400` |

The client is a **fast, honest refusal**, never the authority: the server's `400` remains the
backstop for anything the client gets wrong. Two details matter:

- The per-file gate compares the **decoded byte length of the payload that would actually be sent**,
  not the source file's `size`. `compressAndResizeImage` falls back to the raw bytes whenever its
  canvas path fails, so an 8 MB photo that resizes to 300 KB is accepted, while the same photo whose
  decode fails is refused with the size the server would have received. A base64 data URL's string
  length is never compared to the cap, because base64 inflates a payload by about a third.
- A PDF has no resize to bring it down, so its `file.size` is checked directly. The cap here is the
  **5 MB attachment cap**, not the catalog's 2 MB `Media:CatalogMaxFileBytes`, which is read only on
  the catalog write paths.

The sixth-file refusal uses the server's own sentence (`"A message may carry at most 5 attachments."`)
so the two clients and the API cannot disagree about the rule or its wording.

### 10.3 The analysable subset is accepted, stored, tagged, served — and recorded as not analysable

Only JPEG, PNG, GIF and WebP are analysable by the vision provider
(`Aveline.Api/Common/Media/VisionContentTypes.cs`). HEIC, HEIF, AVIF, BMP, TIFF and PDF are on the
**storage** allow-list but outside that subset, and the contract is that such a file is *stored,
served and tagged normally, and recorded as not analysable — never silently degraded and never
blocked at upload*.

The web picker honours that: it accepts every type the route stores, uploads it, and then derives the
per-file note from the **stored** content type the upload **response** reports — never the picked
file's declared type. So a HEIC the browser re-encodes to JPEG is stored as `image/jpeg`, is
analysable, and gets no note; a HEIC whose decode fell back to raw bytes, or a PDF, gets a
non-blocking note beside a chip that is still `Ready`. The note never blocks the upload and never
blocks the send.

### 10.4 Rendering is the authenticated serve route, not a token URL

A stored attachment's `Url` is the authenticated Aveline serve route
(`GET /api/v1/orgs/{orgId}/conversations/{conversationId}/attachments/{attachmentId}`, guarded by the
conversation access policy, streaming with `nosniff` and `Content-Disposition: inline`). The web
fetches those bytes **through the shared authenticated axios client**
(`apiClient.get(url, { responseType: 'arraybuffer' })`), which attaches the Clerk bearer token, builds
a `Blob` with the **response's** `Content-Type`, and hands `URL.createObjectURL` to the `<img>`; the
object URL is revoked on unmount.

It deliberately does **not** mint an `attachment.view` token and put it in an `<img src>`. The token
route is anonymous because the token *is* the credential, and the token's default TTL is 900 s, so a
cached image would go stale mid-session (§3). An `<img src={stored route}>` cannot work either: an
image element cannot carry an `Authorization` header. A failed or undecodable fetch degrades to the
name-and-size chip, never a broken image.

### 10.5 The retention clock starts at upload (Q6, accepted)

The 7-day bound retention is measured from the attachment's own `MessageAttachment.CreatedAtUtc`,
which is set at **store** time. Because the web uploads on pick, **the clock starts at upload, not at
send**: a photo uploaded and sent much later is still deleted on the upload-based window (a file
uploaded 23 hours before send is dropped roughly 6 days after it was sent, not 7).

This is a **deliberate product decision the owner accepted (Q6)**, not an oversight. Changing it
means changing the pinned semantics the retention job's remarks defend — the attachment is the item
the 7-day policy drops, and anchoring on the conversation would let an active thread hold every image
it ever received.

### 10.6 The per-org/per-thread cap is deliberately deferred (Q1, with a named residual)

No per-org or per-thread attachment cap exists, and adding one is **explicitly deferred** (owner
decision Q1). The steady state is already bounded by three real mechanisms: 5 per message, the 24 h
unbound sweep (hourly), and bound-row retention at 7 days.

**The residual, named rather than left to be rediscovered:** the *number* of unbound uploads one
member can create within 24 hours is not bounded, so a scripted client — or a user who picks and
abandons repeatedly — can hold up to **`24 h × upload rate × 5 MB`** of provider bytes per member.
If a ceiling is wanted later, the cheapest correct place is the existing upload handler (server-side,
reusing the org-scoped count), **not** a client-side guard, which a scripted client ignores.

### 10.7 Outstanding work and known residuals

These are stated so nobody has to rediscover them. None is a surprise in the code; each is a check
this environment could not complete or a trade already made.

1. **The authenticated end-to-end browser walk is outstanding.** A real headless browser *did*
   disprove the one transport risk this feature carried: the browser supplies the multipart boundary
   and the shared client's JSON default does not leak into the request, on the direct API path and
   through the Vite dev proxy. What it could not do is the signed-in walk — pick a JPEG, send, and
   see the bubble — because that needs a Clerk session this environment does not provide. The
   outstanding manual check is:

   1. Start the API: `cd Aveline.Api && dotnet run` (listens on `http://localhost:5091`; the dev
      Clerk authority is already wired in `appsettings.Development.json`).
   2. Start the web dashboard: `cd frontend/web && bun install`, copy `.env.example` to `.env.local`
      and set `VITE_CLERK_PUBLISHABLE_KEY` from the Aveline Clerk instance, then `bun dev` (Vite on
      `http://localhost:5173`, proxying `/api` to `http://localhost:5091`).
   3. Sign in at `http://localhost:5173` as a member of an organization that holds
      `conversations:view` (any of the four boutique roles). Do this once against the **direct**
      API path and once **through the Vite proxy**, because the multipart header/proxy interaction
      is exactly what was flagged.
   4. Open the **Salon** section (`/app/b/{slug}/salon`, or its nav entry) and select the thread.
   5. Click the paperclip (`Attach files`) and choose a real JPEG (a few hundred KB to a few MB).
      Watch the chip move from "Uploading…" to "Ready"; there is deliberately no percentage.
   6. Type a message and press Send (or Enter). The optimistic bubble appears and is replaced by the
      confirmed one, and the image renders as a thumbnail fetched from the authenticated route.
   7. Click the thumbnail: the dialog shows the image. Repeat with a PDF and confirm Open/Download.
   8. Negative check: pick a `.txt` file and a photo over 5 MB, and confirm each is refused with its
      own message and that text can still be sent.

2. **The attachment response was never fetched against a real provider in this environment.** Every
   rendering test mocks the authenticated client (`components/conversation/blocks.dom.test.tsx`
   replaces `@/lib/api`), so the round trip through the real serve route and a real Cloudinary or
   database asset is unproven here. The route, the policy and the store are the shipped ones; the
   browser check in item 1 is what closes this.

3. **The in-memory byte cache is process-lifetime and unbounded per session.** `blocks.tsx` keeps a
   module-level `Map<string, Promise<AttachmentBytes>>` keyed by attachment id; it is never evicted
   except when a request rejects (so a later mount can retry rather than caching the failure
   forever). It is not persisted and a page reload clears it, and object URLs are revoked on unmount,
   so only the fetched byte arrays are retained. It is the same trade mobile already makes and
   documents at its own repository-level cache. It is acceptable now because the entries are
   immutable (attachment rows are written once), the 7-day retention bounds the population that can
   still be fetched, and no eviction policy is invented that would either refetch immutable bytes or
   hold a stale blob. An LRU or a size ceiling is the obvious follow-up if a long-lived session over
   many threads becomes a real cost.
