# ADR-022: Media Storage and Access — the Cloudinary Tier Model

## Status

Accepted.

This ADR records the frozen decisions of
`.agents/plans/cloudinary-media-and-salon-image-implementation-strategy.md` §3 and §8. It is a
record, not a re-opening: the code is authoritative where this document and the code disagree.
Wave 5 (S8 — the catalog delete path, the orphan reconciler, and the irreversible `ImageData`
drop) is **deferred** and gated on production proof plus the database owner's confirmation; it is
not shipped and is described here only as deferred.

## Context

Aveline stored every image byte in PostgreSQL (`InventoryImages.ImageData`,
`MessageAttachments.ImageData`, both `bytea`). That made the database the asset store rather
than the schema, which blocks any small database tier and puts binary delivery on the API
process. The workstream behind this ADR moves bytes to Cloudinary behind a provider seam, splits
the catalog's public tier from the conversation's protected tier, and adds a minted, expiring
token route for the protected tier.

Four constraints shaped the decision and none is ours to change:

1. **Cloudinary cannot enforce URL expiry on the Free plan.** Aveline therefore enforces expiry
   itself by re-checking every request.
2. **The vision provider fetches the image URL itself.** The URL must be absolute, fetchable by a
   third party, and therefore a bearer credential.
3. **The tier is Free.** The plan carries the demo, and the per-tier allowances are revisited
   later by configuration rather than by redesign.
4. **A third-party CDN in front of Cloudinary is prohibited by Cloudinary's Acceptable Use
   Policy** without a paid plan and configuration.

## Options Considered

1. **Cloudinary behind an `IMediaStorage` provider seam, with a public catalog tier and an
   authenticated conversation tier (chosen).**
   Bytes leave the database; the catalog serves an absolute CDN URL anonymously; conversation
   attachments are served only through an Aveline-minted token; the database tier stays as the
   test-time and rollback implementation.
2. **Keep bytes in PostgreSQL and serve them from the API.** No provider dependency and no
   credential, but the database remains the asset store, delivery bandwidth stays on the API,
   and the database downsize stays blocked. Rejected.
3. **Move to an S3-compatible object store (Cloudflare R2) now.** Free egress by policy and no
   metered delivery, but presigned URLs cannot be used on a custom domain, S3 object tagging does
   not exist, and `customMetadata` is reachable only through the Workers API. Recorded as the
   escape hatch, not chosen now.

## Decision

### The two-tier model

- The tier is decided by the **delivery type**, never by the caller.
- **Catalog imagery** is Cloudinary type `upload`. It is public by design (F-7/Q4): the catalog
  image route stays `AllowAnonymous`, returns `302` to the absolute CDN delivery URL for a
  Cloudinary row, and streams bytes for a database row. It carries no token and no Aveline hop.
- **Conversation and protected assets** are Cloudinary type `authenticated` (a PDF is
  `authenticated`/`raw`). They are reachable only through `GET /api/v1/media/{token}` or the
  existing authenticated attachment route. The tier is a property of *who may read*, so a PDF and
  a photo in the same thread have the same answer.
- `asset.public` exists as a scope name but is **not tokenised**: catalog imagery is public, so no
  token is minted for it. The tokenised scopes are `attachment.view` and `vision.analyze`.

### The Free-plan expiry constraint

Cloudinary cannot enforce a URL's expiry on this plan, so a token URL is a bearer credential
until `exp` regardless of the provider's own signing. **Aveline enforces expiry by re-checking
every request**: the verification order is MAC → `exp` → scope → single-use nonce, and the MAC
covers the entire encoded payload, not the identifier. The consequence is accepted (Q4): within
the TTL, a leaked URL is usable by anyone holding it. What bounds it is a short `exp`, a
single-use nonce for `vision.analyze`, a scope so a leaked vision token cannot read an unrelated
attachment, and tenant binding. See [`docs/security/media-access.md`](../security/media-access.md).

### The streaming proxy

**`GET /api/v1/media/{token}` streams and never redirects.** A `302` to a permanent signed
Cloudinary URL makes "time-limited" decorative: the caller keeps the redirect target and replays
it after `exp`, so the time limit stops meaning anything. The proxy is the only place `exp` can be
re-checked, so it is the route. The response carries `private, no-store`, `nosniff` and
`inline`, and the catalog route's `public, max-age=31536000, immutable` must never be copied to a
protected response.

### No Python Cloudinary SDK

The Cloudinary SDK is added to `.NET` only (A7.5 amended to "**`.NET` only**"). The Python agent
service gains no Cloudinary credential and no Cloudinary dependency: its only interaction with a
protected asset is a URL the API mints, and a second credential store would buy no capability.

### The tag schema, and the D6 disagreement

Every stored asset carries provider-neutral metadata on the seam (`MediaMetadata.Labels` →
Cloudinary tags; `MediaMetadata.Attributes` → the `context` record). Two entry points, one
builder (`MediaTagger`), no tier switch:

| Caller | Labels | Context |
|---|---|---|
| Catalog write | `catalog-image`, `kind:image`, `organizationId:{orgId}`, `date:{utc:yyyy-MM-dd}` | `o`, `d`, `s=catalog` |
| Salon attachment write | `salon-image`, `source:{web\|whatsapp\|url}`, `kind:{image\|pdf}`, `conversationId:{id}`, `organizationId:{orgId}`, `userId:{id\|none}`, `date:{utc:yyyy-MM-dd}` | `o`, `v`, `u`, `c`, `d`, `s` |

`fileName` never enters `context` (caller-controlled; `=`/`|` would need escaping) and
`contentType` never enters it either (`kind:image|pdf` carries the only distinction a query
needs). `context` is designed against the conservative 255-character limit; the worst case is
asserted in a test.

**The disagreement, recorded rather than silently resolved.** The delegated research artefact
(`.agents/plans/cloudinary-tags-context-metadata-research.md:461,465`) advises putting
`organizationId`/`conversationId` in `context` or structured metadata rather than in tags, because
tags cap at 1000 per asset and are a product-environment-global namespace with no per-tenant access
control. **All four identifiers stay as tags, because the requirement names them.** The cost is
recorded here, and it is now bounded: Q1's catalog bound (250 items/tenant, rising to ≤1250 assets)
and the 7-day conversation retention (S7) bound the tag population to roughly a week of traffic
rather than to all history. The artefact's **security** half is answered in code and in this ADR:
**a tag is never an authorisation input.** The tenant-proof dimension is the public-id prefix plus
the applied `organizationId` filter, and a test asserts no code path authorises from metadata. If
the product's shape ever changes to millions of conversations with long retention, this is the
first thing to revisit; because `context` already carries every value, the change is a tag
removal rather than a redesign.

### The tier position

**The Free plan carries the demo, and the allowances move later by configuration.** The
obligation this creates is not "fit inside 25 credits"; it is to make the tier an **operational
decision** rather than a redesign. Three properties buy that: the store is behind `IMediaStorage`
(an adapter, not a rewrite); every metered dimension is a configuration value
(`Media:CatalogMaxFileBytes`, `Media:CatalogDisplayWidth`, `Conversations:AttachmentRetentionDays`,
`Media:Provider`); and the two cheap levers (`f_auto,q_auto` at one width, and the item cap) are
additive and independent of the tier. What this does **not** license: deferring the caps, the
single-width rule or the retention job, or reaching for a second Free account or a third-party
CDN. If the tier moves, the re-entry point is `f_auto`'s derivation count — the one cost that
grows with the catalogue rather than usage.

### No third-party CDN in front of Cloudinary

**Prohibited, and the permitted version costs more than the problem.** Cloudinary's Acceptable Use
Policy §4.2 ([cloudinary.com/trust/aup](https://cloudinary.com/trust/aup), last updated
12 September 2024):

> *"Using a third-party CDN in front of the Services without the corresponding plan and
> configuration is not supported. If you have independently configured a caching layer in front of
> the Services, in addition to any other provisions in this AUP, your Account may be suspended
> until migrating to a suitable subscription plan."*

Adjacent clauses close the obvious workarounds: §4.1 prohibits opening multiple accounts to bypass
usage restrictions, and §4.3 prohibits excessive use. **"The corresponding configuration" is a paid
feature that makes the exercise pointless:** custom domain (CNAME), HTTPS SSL certificate and
token/cookie auth are Advanced-tier only, and Advanced is **$249/month with 600 credits** — 24× the
Free allowance, on a plan where the CDN is unnecessary. Cloudflare's own Service-Specific Terms
reserve the right to disable a free CDN "suspected of … a disproportionate percentage of pictures",
which is exactly this workload, and Cloudinary publishes **no** statement that a third-party cache
hit costs zero bandwidth. So the decision is: the CDN is the cache, and no CDN is put in front of
it.

### The recorded escape hatch: Cloudflare R2

If Free stops fitting, **Cloudflare R2** is the recorded alternative — egress is free by policy,
with 10 GB-month storage, 1M Class A and 10M Class B operations free. It removes the metered
delivery problem at the root, and it is **not a drop-in**. Three rework items are named so the move
is a schedule rather than a discovery:

| R2 constraint | Consequence |
|---|---|
| Presigned URLs work with the S3 API domain and **cannot be used with custom domains** | The protected tier is built on expiring presigned access; auth on a custom domain needs WAF HMAC validation (Pro+) or a Worker — new work and a new surface |
| **No S3 object tagging** (`GetObjectTagging`/`PutObjectTagging`/`DeleteObjectTagging` unsupported) | The approved tag schema has no R2 equivalent; `customMetadata` works only through the **Workers** API, so the tagger and its audit get rewritten |
| A custom domain requires the domain as a Cloudflare zone; default caching is extension-limited | Needs a "Cache Everything" rule; 512 MB cacheable cap; partial/CNAME setup is Business+ |

Because R2 is S3-compatible, the seam survives: `CloudinaryMediaStorage` becomes `R2MediaStorage`
behind the same `IMediaStorage`.

### The two clearance decisions

- **No backfill.** Both environments are empty and there is no historical data to migrate. There
  is no `MediaBackfillJob`, no `MediaMigrationRecords`, no reconciliation report, and no
  tag-reconciliation job. What remains is one assertion in S7's suite: after a week of real
  traffic, no asset under `aveline/` lacks its labels.
- **A 7-day conversation retention policy.** A bound conversation attachment older than
  `Conversations:AttachmentRetentionDays` (default 7) is deleted, and its remote asset is released
  **before** the row disappears. The window is measured from the attachment's own creation, a live
  conversation is swept, and the catalog is exempt. This is a new product policy, not a
  documentation of existing behaviour, and it makes Q1's volume arithmetic true.

### Security-review carry-forward findings (F2 / F5 / F8)

The S6 security review ([`docs/security/image-url-fetch-review.md`](../security/image-url-fetch-review.md))
attached three findings to this ADR. Recorded verbatim in substance:

- **F2 — the pin's authority is the request, not the handler.** The guarantee "every socket goes
  to the address validated for this request" holds only while the connection pool cannot hand a
  socket for authority *X* to a request pinned to a different address for the same *X*. The
  review judged it **not exploitable as written** (the fetcher calls the resolver once per hop,
  requires every answer to be public, and always pins `addresses[0]` for `uri.DnsSafeHost`), but
  the next caller of the named client may not be as careful: it is the request option, not the
  handler, that decides where the socket goes, and the type system does not enforce it. The robust
  form is to partition the pool by address (a per-hop throwaway handler, or an address in the pool
  key).
- **F5 — `FetchedImage` does not encode its image invariant.** A caller that stored
  `fetched.ContentType` without its own `IsImage` check would accept `application/pdf`, because the
  fetcher's guard is a separate statement. The single caller added by S6 does **not** re-check and
  is safe only because the fetcher checked. The suggested hardening is a `FetchedImage.TryCreate`
  factory or a `MediaContentTypes.ImageType` value object, so the check cannot be skipped.
- **F8 — the pin governs the address, not an authenticated peer.** Pinning defeats DNS rebinding;
  it does not authenticate the peer. Once an address is judged public, the fetcher trusts it: it
  inspects no certificate for identity beyond the transport's own hostname check, and it treats
  the response as an image only if the bytes say so. An attacker who controls a *public* host can
  serve us an image — that is the feature working as designed, not a bypass. **A future
  "verify the image is from a known CDN" requirement must not assume this is already met.**

### The shared 404 translation, and the additive reference arm

- The catalog's `POST /api/v1/orgs/{organizationId}/catalog/analyze-image` route and the internal
  vision route (`POST /internal/visual/analyze-image`) now **share one 404 translation**: a
  `KeyNotFoundException` raised by the reference arm (another organisation's row, an unknown row,
  or a deleted row) is mapped to `404`, never to a resolved image and never to a token. Only that
  one exception type is caught, so every other fault still reaches the global handler as a `500`.
- **`AnalyzeImageDto` carries an additive reference arm** — `ImageRefKind`
  (`attachment` | `inventoryImage` | `externalUrl` | absent) and `ImageRefId` — while
  **`ImageUrl` stays non-nullable** and defaults to empty (strategy §4 C14). Making it nullable
  would be a nullability change on a live DTO and would alter binder behaviour, so the reference
  arm treats **empty as absent**: a stale `""` never shadows a named reference, and an existing
  caller that omits the property sees exactly the object it saw before.

### The §8 decisions table, one line each

| Decision | Choice | Decided by | Rejected |
|---|---|---|---|
| Upload metadata carrier | **On the seam** — `MediaMetadata` on `MediaPutRequest`, labelled provider-neutrally | review (D1) | A separate tagging pass after upload; tagging only in the Cloudinary row adapter |
| Metadata lands at | **S0**, with the seam | this strategy (D1) | With the tagging slice — which would re-create a reconciliation obligation |
| Protected-tier token machinery | **Split P2; the token route lands before the bridge** | review (D2) | Running the migration plan's whole P2 first; populating only `attachments` |
| The Cloudinary attachment class | **Two classes, two seams**: `CloudinaryMediaStorage` + `CloudinaryAttachmentStore` | this strategy (§3.1) | One class implementing both |
| The backfill | **Not built — there is no data to migrate.** No `MediaBackfillJob`, no `MediaMigrationRecords`, no reconciliation report | review (D3) | Building migration machinery for zero rows |
| Tag reconciliation | **Not built at all** — an assertion in S7's suite replaces even a read-only audit | review (D3) | A mutating repair job for assets the one write path cannot leave untagged |
| `MessageAttachments.ContentHash` | **Lands, written at upload** | review (D3, Q9) | Dropping it with the backfill; a Redis-only identity |
| Conversation retention | **Build it: 7 days, S7, releasing the remote asset before the row** | review (Q1) | Assuming a policy that does not exist; leaving conversation storage unbounded |
| Credential form | **`CLOUDINARY_URL` primary, the two discrete keys as fallback, parsed at startup** | review (Q3) | The three `Cloudinary:*` keys as the only form; the SDK's implicit ambient read |
| Catalog tier cap | **Separate and tighter than the attachment cap** (`Media:CatalogMaxFileBytes`) | this strategy, from review (Q1) | One 5 MB ceiling for both tiers |
| Product tags vs media tags | **Two vocabularies, never merged** — the catalog's tags are Postgres domain data | review (Q5) | Wiring the catalog's sort-by-tag to Cloudinary media tags |
| Bearer-token consequence | **Accepted** | review (Q4) | Requiring a fetcher credential, which a third-party vision provider cannot hold |
| The three call sites | **Salon plan owns them**; a per-site test each | review (D4, Q8) | Both plans writing them; Elle owning them |
| Vision provenance field | **`vision_source: model\|unavailable`**, owned by Elle; `is_fallback` dropped here | review (D4) | Two fields in two languages describing one state |
| Vision variant set | **Empty** for the protected tier until measured | review (D5) | A downscaled eager "vision" variant, which contradicts the provider's `detail: original` requirement |
| Downscaled vision variant | **Deferred** | review (Q6) | Building it before measuring |
| Catalog display variant | **`w_800,f_auto,q_auto` — and exactly ONE width, asserted in a URL-shape test** | this strategy (§3.7, §7 check 11), from review (Q1)'s bandwidth arithmetic | Serving originals into the grid; and **a second width**, which is the fastest way to lose the plan |
| Delivery format | **`f_auto`, never a forced `f_webp`** | this strategy (§3.7) — Cloudinary's own `q_auto` refuses WebP when chroma subsampling would hurt colour | Forcing WebP globally, trading the vision path's colour-hex fidelity for bytes; and AVIF, unavailable on a bandwidth-metered plan |
| Image caching | **The CDN and the client; never Redis; in-process only if measured** | this strategy (§3.8) | A Redis image or image-metadata cache — capacity rules it out before design does |
| Third-party CDN in front | **Not used** — AUP §4.2 prohibits it and the permitted version is $249/mo | this strategy (§3.9) | Cloudflare in front of Cloudinary; a second Free account (§4.1) |
| Store migration path | **R2 recorded as the escape hatch**, with three named rework items | this strategy (§3.9) | Migrating now; treating R2 as a drop-in |
| Tier allowances | **Free carries the demo; allowances move later by configuration** | review (this session) | Treating 25 credits as a hard design constraint |
| Catalog-tier metadata | **Same carrier, both tiers** | this strategy (§3.2) | Tagging the conversation tier only |
| Tag cardinality | **All four identifiers, as the requirement states**, with the cost recorded and now bounded by S7 | review (D6) | Following the delegated research artefact's advice to drop the UUID-valued tags |
| SSRF kill switch default | **`false` everywhere** | this strategy (C7) | The salon plan's "`true` in non-production" |
| `externalUrl` validation | **Deferred, documented** | review (Q7) | Allow-listing it now, which changes a shipped path |
| PDF gating | **S2's PDF arm only**, never S1 | this strategy (C12) | Gating the whole catalog half on A7.2 |
| SSRF fetcher | **Build at S6, or ship Option A** (an explicit `400`) | salon plan §7.4, adopted | Letting Cloudinary fetch the URL (no cap, no hash, no type policy of our own) |
| Database provider in production | **Allowed behind an explicit override** | review (Q11) | Refusing outright, which removes the rollback path |
| `LogoUrl` / `ProfileImageUrl` | **Neither moves** | review (Q12) | Migrating them — `ProfileImageUrl` is Clerk-owned and `LogoUrl` has no bytes |
| A7.5 | **`.NET` only** | migration plan §6.1, adopted | Both SDKs, which puts a Cloudinary secret in a second store for no capability |

## Consequences

- `Media:Provider=cloudinary` and `Media:DualWrite=false` are the documented defaults; a
  database-backed production host refuses to start unless
  `Media:AllowDatabaseProviderInProduction=true`, which logs at `Warning`.
- Rollback from the Cloudinary tier is `Media:Provider=database` while dual-write is on, and
  `Media:ReadFromCloudinary=false` for the protected read path. **Once `DualWrite=false` and
  `ImageData` is dropped there is no `bytea` copy to fall back to** — that is stage 3, the point
  of no return, and it is S8's job under an explicit confirmation and the database owner's sign-off.
- `MessageAttachment.Url` deliberately does not change: it stays the authenticated Aveline route,
  because a Cloudinary URL is not a stable value to persist and every reader uses the field rather
  than building a path.
- `Conversations:AttachmentRetentionDays` (7) and `Conversations:AttachmentRetentionMaxPerRun`
  (500) are the retention surface; the job runs every 6 hours under a distributed lock, and a
  provider failure keeps the row for the next run.
- `noeviction` is an **operational prerequisite**: under `volatile-lru` an evicted
  `vision.analyze` nonce silently weakens the single-use guarantee — a fail-open, which is the
  opposite of what the protected tier requires.
- The catalog delivery URL carries exactly one `w_{Media:CatalogDisplayWidth}`, `f_auto` and
  `q_auto`. Adding a second width is the fastest way to lose the Free plan, and the URL-shape test
  fails the moment someone does.
- The operational rollout surface — every `Media:*` and `Conversations:*` key, its default, what
  it gates and how to roll back — is tabulated in
  [`docs/architecture/media-rollout-flags.md`](../architecture/media-rollout-flags.md).
- F2/F5/F8 remain known residuals (LOW). The S6 review carried no HIGH or MEDIUM finding, and this
  ADR is where its explicit carry-forward conditions are discharged.
- **Deferred, not shipped:** Wave 5 / S8 — the catalog delete path wired to `DeleteAsync`, the
  `CloudinaryOrphanReconciliationJob`, `Media:DualWrite=false`, and the `ImageData` column drop
  from both tables. It is gated on production proof and the database owner's confirmation, and it
  is irreversible.
</content>
