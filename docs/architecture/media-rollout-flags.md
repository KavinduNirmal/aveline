# Media Rollout Flags — the Operational Surface

> **Audience:** the on-call engineer who has to decide, at 03:00, what a media flag does and how to
> put it back. Every key below is the **real** key and default read from
> `Aveline.Api/Modules/Media/MediaOptions.cs`, `Aveline.Api/appsettings.json`,
> `Aveline.Api/Modules/Media/MediaOptionsValidator.cs` and
> `Aveline.Api/Modules/Conversations/Services/ConversationAttachmentRetentionJob.cs`.
>
> The decision record is [ADR-022](../ADR/ADR-022-media-storage-and-access.md); the access model is
> [docs/security/media-access.md](../security/media-access.md).

---

## How to change a value

- **Locally / docker compose:** environment variables, `__` for `:` — `Media__Provider=cloudinary`,
  `Conversations__AttachmentRetentionDays=30`. `docker-compose.yml` passes the four Cloudinary
  credential names through (`CLOUDINARY_URL`, `CLOUDINARY_API_KEY`, `CLOUDINARY_API_SECRET`,
  `CLOUDINARY_CLOUD_NAME`); the `Media:*` keys themselves come from `appsettings.json` unless the
  deployment overrides them.
- **Production:** the deployment's secret/configuration store, using the same names as
  `appsettings.json` (`Media:Provider`) or their double-underscore environment form
  (`Media__Provider`). The three Cloudinary credentials are **never** bound into the committed
  `appsettings.json`; they are resolved once at startup by `CloudinaryUrlParser`.
- **A malformed value is a startup refusal, not a first-request error.**
  `MediaOptionsValidator.ValidateOrThrow` runs at boot and throws `InvalidOperationException` on
  the first rule that cannot hold.

## `Media:*`

| Key | Default | What it gates | How to roll back / notes |
|---|---|---|---|
| `Media:Provider` | `database` | Where bytes live. `database` = the row's `bytea`; `cloudinary` = the provider seam | Set `Media:Provider=database`. **A Production host with `database` refuses to start** unless `Media:AllowDatabaseProviderInProduction=true`. An unrecognised value refuses the boot |
| `Media:ReadFromCloudinary` | `false` | The protected read path's per-row dispatch: honour a Cloudinary reference on a row that carries one | Set `false` for an immediate, free rollback of the protected read path; every read falls back to the row's bytes while dual-write is on |
| `Media:DualWrite` | `false` | Whether a provider write also keeps the bytes on the row (the second copy that makes `Provider=database` free) | Set `true` to restore the stage-1 property. **`false` is the migration's point of no return only once `ImageData` has been dropped** (S8, deferred) |
| `Media:SigningKey` | `""` | The HMAC key for every media token. Base64, exactly 32 bytes | Required when `Provider=cloudinary`; `MediaOptionsValidator` refuses a blank, non-base64 or wrong-length value. Rotating it invalidates every outstanding token |
| `Media:PublicBaseUrl` | `""` | The absolute origin the token URLs handed to external fetchers are built from | Required when `Provider=cloudinary`; must be an absolute `http(s)` URL or the boot is refused |
| `Media:AllowDatabaseProviderInProduction` | `false` | Whether a Production host may run `Provider=database` | The documented rollback escape hatch (Q11). Setting `true` logs at `Warning`; the provider still stores bytes in the database |
| `Media:VisionTokenTtlSeconds` | `600` | The default lifetime of a `vision.analyze` token | Clamped by the signer to a hard cap of 1800 s; a caller cannot exceed it |
| `Media:AttachmentTokenTtlSeconds` | `900` | The default lifetime of an `attachment.view` token | Clamped by the signer to a hard cap of 3600 s |
| `Media:ClockSkewToleranceSeconds` | `30` | How much clock skew an `exp` check tolerates | Negative values are treated as 0 in the signer |
| `Media:VisionUsePrivateDownload` | `false` | The Option-C′ fallback for the agent path | Off by default; leave off unless a private-download path is deliberately enabled |
| `Media:ImageUrlUploadEnabled` | `false` | Whether a pasted image URL may be fetched at all | **The kill switch.** `false` closes the whole SSRF surface and answers a pasted URL with `400 code:feature-disabled`. Flip to `false` for a complete rollback with no data consequence |
| `Media:ImageUrlAllowlist` | `""` | An optional comma-separated host allow-list; empty means "any public host" | A malformed entry (scheme, port, path, wildcard) **fails closed at startup and in the fetcher**. An operator enabling `ImageUrlUploadEnabled` should set this |
| `Media:ImageUrlMaxRedirects` | `2` | How many redirects the fetcher follows, re-validating each hop | `0` refuses the first redirect |
| `Media:ImageUrlFetchTimeoutSeconds` | `10` | The **total** fetch budget, covering every hop, the header wait and the body read | One linked CTS created before the redirect loop, so redirects cannot buy more time |
| `Media:AllowInsecureImageFetch` | `false` | Whether `http` is tolerated on the pasted-URL path | `false` keeps the fetcher on HTTPS; an https-to-http redirect is then refused |
| `Media:CatalogMaxFileBytes` | `2097152` (2 MB) | The catalog tier's per-file cap, checked on **both** catalog write paths before anything is written | Tighter than the 5 MB attachment cap. Raising it raises the Free-plan storage rental; the 5 MB ceiling is not a plan |
| `Media:CatalogDisplayWidth` | `800` | **The one and only** delivery width inserted into a catalog delivery URL | `0` (or less) refuses the boot. **Do not add a second width**: `f_auto` derives a format per width, and the derivation count is the Free-plan limit that breaks first |

## `Conversations:*`

| Key | Default | What it gates | How to roll back / notes |
|---|---|---|---|
| `Conversations:AttachmentRetentionDays` | `7` | The window after which a **bound** conversation attachment, and its remote asset, are deleted | Set it high (or stop the job) for a complete rollback. `0` or a negative value refuses the boot. The window is measured from the attachment's own creation, not the conversation's last activity |
| `Conversations:AttachmentRetentionMaxPerRun` | `500` | The bounded-per-run ceiling of the retention job (the `CustomerSalonBackfill.DefaultMaxCustomers` pattern) | Not validated at startup; a backlog drains across runs. The job runs every 6 hours under a distributed lock, and a store failure keeps the row for the next run |

## `Cloudinary:*` (non-secret)

These are the only Cloudinary settings that live in the committed `appsettings.json`. The credential
is resolved separately (below).

| Key | Default | What it gates |
|---|---|---|
| `Cloudinary:FolderRoot` | `aveline` | The folder every public id is rooted under |
| `Cloudinary:CatalogDeliveryType` | `upload` | The delivery type for the public catalog tier (the tier is decided by the delivery type, not the caller) |
| `Cloudinary:ProtectedDeliveryType` | `authenticated` | The delivery type for the protected conversation tier |
| `Cloudinary:UploadTimeoutSeconds` | `30` | The provider upload timeout |
| `Cloudinary:MaxConcurrentUploads` | `10` | The documented starting upload concurrency |
| `Cloudinary:UploadRetryAttempts` | `3` | Retry attempts for a Cloudinary `420` or 5xx response |

## Credentials (root configuration, never committed)

| Name | Default | What it gates |
|---|---|---|
| `CLOUDINARY_URL` | empty | The primary credential form: `cloudinary://<key>:<secret>@<cloud_name>`, parsed once at startup |
| `CLOUDINARY_API_KEY` | empty | The discrete fallback; must be set together with the secret |
| `CLOUDINARY_API_SECRET` | empty | The discrete fallback's secret |
| `CLOUDINARY_CLOUD_NAME` | empty | The discrete fallback's cloud name (not a secret; also carried by `CLOUDINARY_URL`) |

`MediaOptionsValidator` refuses a boot when `Media:Provider=cloudinary` and no complete credential
can be resolved (neither `CLOUDINARY_URL` nor the `API_KEY` + `API_SECRET` pair), and the
implementation never reads the SDK's ambient global.

## Operational prerequisites (not configuration keys)

| Prerequisite | Why |
|---|---|
| **Redis `maxmemory-policy noeviction`** | Under `volatile-lru`, an evicted `vision.analyze` nonce silently makes a single-use token replayable — a **fail-open** in an access control. See [media-access.md §9](../security/media-access.md#9-noeviction-is-an-operational-prerequisite) |
| **A live Cloudinary run** | A7.2 (PDF delivery on the product environment) and the two measured numbers (average delivered size, real derivation count) are product/usage facts, not code facts. They are read from a live run's output and no run is recorded in the repository |
