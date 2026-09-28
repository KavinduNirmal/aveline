# Image URL Fetch Security Review — Aveline

> **Issue:** S6 (pasted image URL) · **Date:** 2026-09-22 · **Reviewer:** U4.2 wiring/review agent
> (lane L1 for this unit) · **Scope:** the server-side fetcher for a client-supplied image URL —
> `Aveline.Api/Modules/Conversations/Media/**` (unit U4.2), its DI registration, and the
> `SendMessageAsync` caller-side wiring added by this unit.
>
> **Revision reviewed.** Reviewing tree `74ff7f1`. U4.2's files, by SHA-256, so this review is
> pinned to bytes rather than to a file name:
>
> | File | SHA-256 |
> |---|---|
> | `IImageUrlFetcher.cs` | `cf4f869c62072c297a2055d2af82307636d84ea2c0df9fdd0d1ab698587cc1c6` |
> | `IImageUrlHostResolver.cs` | `d69ec76d142ffbfa9f2f07d172570335356bf63e3f3b302e2358d88cb2ce7184` |
> | `ImageUrlAddressPolicy.cs` | `4091b2c0159f0fff5ee491a2de86df52c52563a17687ad1fa1af81b6f46308c1` |
> | `ImageUrlFetcher.cs` | `1ca078ad9ecaa69f7ebeb2e628b3a51001e0db8428abf88184ffa90790b90744` |
> | `ImageUrlFetcherRegistration.cs` | `672e8d5ad7a40ff70cdadff1bdf70bfe65974351d4c34083b363a94485aa178f` |
> | `ImageUrlFetchException.cs` | `70281fc3ee7d1d7d224ecf3574e6dc6375f1f26627659b1a1121010a4723200d` |
> | `ImageUrlPinningHandler.cs` | `333c8432a9db007d07b42896d90503b8463aac1df5bcb22ee11f00f8c2e61f14` |
>
> This is the S6 gate the strategy names (strategy §7 check 9 / §5.1 S6): *"S6's gate is a recorded
> security review, not a green suite. If it cannot be scheduled, the falsification is 'the slice
> ships Option A' — an explicit `400` — and everything else stands."* This document is that review;
> the recommendation is in [Recommendation](#recommendation).

## Methodology

- **Manual adversarial read, line by line**, of all seven files above plus the caller wiring in
  `Aveline.Api/Endpoints/ConversationEndpoints.cs` (`ResolvePastedImageAsync` and
  `SendMessageAsync`), `Program.cs` (`AddImageUrlFetcher`), `MessageDtos.cs`
  (`SendMessageRequest.ImageUrl`), and the two collaborators the fetcher trusts
  (`Common/Media/MediaContentTypes.cs`, `Modules/Media/MediaOptions.cs`).
- **Trace-based review.** Every guard was followed to its failure path, not just its happy path:
  what the HTTP stack does if the guard is absent, what the fetcher does when the stack throws,
  and what the caller does when the fetcher refuses.
- **Test-coverage audit.** The test cases in `Aveline.Api.Tests/ImageUrlFetcherTests.cs` (60 test
  methods expanding to **117 cases**, matching U4.2's claim) and
  `Aveline.Api.Tests/PastedImageUrlTests.cs` (16 cases) were read and checked against each of the
  fourteen behaviours. A behaviour is "covered" below only when a test exercises the behaviour and
  would fail if the implementation were removed.
- **Caller-side exercise.** `PastedImageUrlTests` runs the real endpoint pipeline
  (`WebApplicationFactory<Program>`, real auth, real store adapter, real binding) with the fetcher
  and the store replaced at their DI seams, so the caller's refusal discipline is observed rather
  than asserted against the fetcher's own tests.
- **What I did not do is listed explicitly in [Non-coverage](#what-this-review-does-not-cover).**
  There was no live OTLP/proxy inspection, no penetration test, and no test against a real hostile
  DNS server.

## Threat model and attack surface

The feature exists because a staff member may have only a URL (salon plan §7.1). The client sends
`SendMessageRequest.ImageUrl`; the server dereferences it. That sentence is the whole threat
model: **authenticated staff input causes a server-initiated outbound HTTP request**, and the only
thing that stands between the input and any host reachable from the API process is this fetcher.

| # | Threatened asset | Attack | Who can attempt it | Guard |
|---|---|---|---|---|
| T1 | Internal network / loopback services | SSRF: name a URL whose host is (or resolves to) an internal address | Any org member who can send a Salon message | Resolve-ourselves + all-addresses-public rule (`ImageUrlAddressPolicy`), no redirects followed by the stack |
| T2 | Cloud instance metadata (`169.254.169.254` et al.) | SSRF to the credential endpoint | Same | `169.254.0.0/16` and `fe80::/10` are denied; a named test (`FetchAsync_ToTheCloudMetadataAddress_IsRefused`) exercises it |
| T3 | A host that passes validation, then changes DNS | DNS rebinding: validate public, connect private | Same | The connection is **pinned** to the validated `IPAddress`; the stack never resolves |
| T4 | Redirect-based guard bypass | 302 to `http://169.254.169.254/…`, to a non-allow-listed host, or to a non-http scheme | Same | Redirects are followed by hand, `AllowAutoRedirect=false`, and steps 1–6 re-run on every hop |
| T5 | Our Cloudinary account / storage | Payload smuggling: arbitrary bytes behind `Content-Type: image/jpeg` | Same | Magic-byte sniff; the sniffed type is stored, and a non-image sniff is refused |
| T6 | API process memory / sockets / time | Resource exhaustion: a huge body, a slow-loris peer, a redirect loop | Same | Streaming cap read at `cap + 1`; a single total budget shared across hops; `ConnectTimeout` |
| T7 | API credentials, cookies, ambient identity | Credential/cookie leakage: the fetched host harvests the API's own cookies or auth header | The fetched host, or a compromised one | No cookie jar, no delegating handler, no default headers, `Authorization` never copied |
| T8 | Operator logs / SIEM | Log injection and secret leakage: a URL carrying `?token=…`, newlines, or control characters | Any caller | Only host + a truncated SHA-256 of the URL are logged; the URL is never echoed |
| T9 | The message itself | A failed fetch silently changing or failing the user's message | A caller sending a bad URL | Refusal leaves the text intact, adds no attachment, and does not fail the send |

The kill switch narrows the window further: `Media:ImageUrlUploadEnabled` defaults **`false`**
(`appsettings.json:115`, `MediaOptions.cs:49`), so in the shipped configuration the whole surface
is closed and a pasted URL is answered with an explicit `400` (strategy §4 C7). T1–T8 are therefore
live only in a deployment that deliberately turns the feature on.

## The fourteen behaviours

"Independent" is my assessment after reading the implementation and the tests, not a restatement of
the plan. Several items are implemented by one line that is easy to remove by accident; where the
test suite would not notice, I have said so and this is recorded in the findings.

| # | Behaviour (salon §7.5) | Implementation | Test that proves it | Independent assessment |
|---|---|---|---|---|
| 1 | **Parse strictly** — `Uri.TryCreate(…, Absolute)` or reject | `ImageUrlFetcher.ParseAbsolute` (`:390-391`), refused at `:76-81` | `FetchAsync_WithARelativeValue_IsRefusedAsUnparseable` (4 cases) + the null case | **Strong.** `file://`-shaped relative values are caught by behaviour 2 rather than by this guard, so the parse guard can only ever refuse — it cannot admit. |
| 2 | **Scheme allow-list** — https only; http only behind `Media:AllowInsecureImageFetch` (default false); reject ftp/file/gopher/data/everything | `EnsureScheme` (`:229-253`) | `FetchAsync_WithADisallowedScheme_IsRefused` (5 cases), the two http cases, the https case | **Strong.** The default-deny arm is explicit, not a fallthrough. `AllowInsecureImageFetch` defaults false in both `MediaOptions.cs:61` and `appsettings.json`, so an https-to-http redirect is refused under the shipped config. |
| 3 | **No credentials, no ports** — reject `UserInfo`; allow only the scheme default port | `EnsureNoCredentials` (`:255-263`), `EnsureDefaultPort` (`:265-279`) | `FetchAsync_WithUserInfo_IsRefused` (2), `FetchAsync_WithANonDefaultPort_IsRefused` (3), the http/8080 case, the two default-port acceptance cases | **Strong.** `Uri.Port` returns the scheme default when the URL names none, so `host` and `host:443` are both admitted and every other port refused — correctly, including `https://host:80`. |
| 4 | **Optional host allow-list**, empty = any public host, following the `Webhook:AllowedIps` convention | `EnsureHostAllowed` (`:281-305`); `MediaOptions.FindMalformedAllowlistEntry` | Empty-list admit; listed-host admit (3, case-insensitive); unlisted refuse (3, including `cdn.example.com.evil.example` suffix confusion); malformed entry fails closed before resolving | **Strong.** Exact host equality (plus a trailing-dot trim), never suffix matching, so `candidate.example.com` cannot pass an `cdn.example.com` entry. A malformed list denies everything rather than silently becoming "any host". |
| 5 | **Resolve the host ourselves; reject if *any* resolved address is non-public** | `DnsImageUrlHostResolver` (`IImageUrlHostResolver.cs:25-34`) called at `:206`; all-addresses loop at `:214-222` | `FetchAsync_WithANonPublicResolvedAddress_IsRefused` (30 addresses) + public-address admittance (4) + `FetchAsync_WhenAnyResolvedAddressIsNonPublic_IsRefused` + metadata-IP + unresolvable | **Strong for the ranges that matter.** The range list is the special-use registries, not a hand-picked few, and IPv4-mapped IPv6 is unwrapped (`ImageUrlAddressPolicy.cs:78-81`) so `::ffff:10.0.0.1` is not a bypass. Gaps are test gaps, not deny-list gaps: `100::/64`, `5f00::/16` and `192.88.99.0/24` are on the deny list but have no test that names them (finding **F4**). |
| 6 | **Pin the connection to the validated IP**; original `Host` preserved | `ImageUrlPinningHandler.ConnectPinnedAsync` (`:62-88`); the pin is set per request at `ImageUrlFetcher.cs:93` | `FetchAsync_PinsTheConnectionToTheValidatedAddress` (the option is set), `FetchAsync_WhenAnyResolvedAddressIsNonPublic_SetsNoPin`, and the real-socket `TheShippedHandler_ConnectsToThePinnedAddressAndKeepsTheOriginalHostHeader` | **Strong, with a named residual.** The real-socket test proves a request whose URI names an unresolvable host still reaches a server that exists only on loopback, and that the server saw the original authority — i.e. the connect used the pin, not a second DNS answer. See [The rebinding story](#the-rebinding-story) for the connection-reuse residual (**F2**). |
| 7 | **Do not follow redirects automatically**; follow at most `Media:ImageUrlMaxRedirects` (default 2) manually, re-running 1–6 per hop; reject a redirect to a different scheme or a non-public address | Manual loop `:87-138` with `ValidateAsync` per hop (`:90`); `IsRedirect` (`:383-388`); `AllowAutoRedirect=false` (`ImageUrlPinningHandler.cs:53`) | Single redirect followed; two followed; third refused; `maxRedirects: 0` refuses the first; redirect to a private host refused; redirect to `https://169.254.169.254/` refused; redirect to `ftp://` refused; redirect to an allow-list miss refused; a redirect status with no `Location` refused; non-2xx refused | **Strong.** This is the best-covered behaviour in the file (9 cases). The re-validation is structural, not duplicated: the loop body starts with `ValidateAsync` for every hop including the first. |
| 8 | **Timeout budget** — `Media:ImageUrlFetchTimeoutSeconds` (default 10) as a *total* budget, plus a header timeout, so a slow-loris peer cannot hold a request open | Linked CTS at `:83-84`, `CancelAfter` before the redirect loop; the same token bounds `SendAsync` and the body read; `ConnectTimeout` on the handler (`ImageUrlPinningHandler.cs:55`) | `FetchAsync_WhenThePeerDoesNotSendHeadersWithinTheBudget_IsRefusedAsATimeout`, `FetchAsync_WhenTheBodyStalls_IsRefusedAsATimeout`, `FetchAsync_WhenTheCallerCancels_PropagatesTheCancellation` | **Strong.** The budget is created **once, outside** the redirect loop, so redirects cannot multiply it — a redirect chain that stalls still dies at the configured ceiling. Caller cancellation is correctly distinguished from a budget expiry (`:145-155`). |
| 9 | **Enforce the size cap while streaming** — read at most `MaxFileBytes + 1`, abort when crossed, never `ReadAsByteArrayAsync` | `ReadWithinCapAsync` (`:351-381`), called at `:330`; declared `Content-Length` pre-check at `:322-328` | Over-cap body refused **and** `BytesRead == MaxFileBytes + 1`; exactly-cap body accepted; declared over-cap length refused before reading a byte | **Strong.** The counting-stream test proves the reader stops at the boundary rather than reading the body and checking afterwards, which is the property that makes the cap real. The cap is per hop, not per fetch — see **F3**. |
| 10 | **Require an allow-listed content type** — reuse `MediaContentTypes.IsImage`, no second policy | `ReadImageAsync` (`:314-320`) | `FetchAsync_WithANonImageContentType_IsRefused` (`text/html`), `FetchAsync_WithNoContentType_IsRefused`, `FetchAsync_WithAPdfContentType_IsRefused` | **Strong and correctly scoped.** A PDF is deliberately refused on this path even though it is storable elsewhere: a pasted "image" URL is an image path, and the test names that intent. |
| 11 | **Sniff the magic bytes**; the sniffed type is what is returned | `MediaContentTypes.Sniff` at `:334`; the non-image sniff refused at `:335-340`; the sniffed type returned at `:343` | HTML behind `image/jpeg` refused; script behind `image/png` refused; real image returns its type; **PNG bytes behind a JPEG header return `image/png`** | **Strong in the fetcher; the caller inherits it.** The cross-type test is the one that matters: it proves the declared header cannot influence what is stored. The *separate* guard that a `%PDF-`-headed body is refused is real (the sniff returns `application/pdf`, which `IsImage` then rejects) but has no test — finding **F4**. |
| 12 | **No cookies, no ambient credentials** — no auth delegating handler | `ImageUrlPinningHandler.Create` (`:51-59`): `UseCookies=false`, `AutomaticDecompression=None`, no `Authorization`; registration adds **no** delegating handler and clears default headers (`ImageUrlFetcherRegistration.cs:38-49`) | `FetchAsync_SendsNoAuthorizationOrCookieHeader`; the real-socket `TheShippedHandler_DoesNotStoreOrResendAResponseCookie`; `AddImageUrlFetcher_RegistersTheFetcherWithNoAmbientAuthorization` | **Strong.** The cookie test is a two-request test on a real socket (receive `Set-Cookie`, then send again), which is the only way to prove a jar is absent rather than merely that one request was clean. The registration test resolves the real named client and asserts no ambient `Authorization`/`Cookie`. |
| 13 | **Never log the full URL** — host + hash only; query strings can carry tokens | `HostFor` (`:396-412`) and `UrlHash` (`:414-421`) are the only URL-derived values logged (`:130-135`, `:167-171`, `:180-187`); exception messages are static (`ImageUrlFetchException.cs:9-12`) and the transport exception's *type* only is logged (`:156-165`) | Success path, failure path and transport-throw path all assert the URL, the secret, the path and `token=` are absent while `host=` and `urlHash=` are present; the exception-message test | **Strong.** The scrub is by construction: no code path interpolates the input URL into a log line or an exception message, and the URL's hash is a truncated SHA-256 used only for correlation. **Also see the caller**: the new endpoint Warning logs only the reason code. |
| 14 | **Fail closed, never fail the send** — a refusal leaves the text intact, adds no attachment, logs at `Warning`, returns | Fetcher: typed refusal for every failure, including the catch-all (`:156-165`), `Disabled` first (`:67-74`). Caller: `ResolvePastedImageAsync` catches and returns `null` for every reason except `Disabled`, logs at `Warning`, and the send proceeds | `FetchAsync_WhenTheTransportThrows_WrapsItAsATypedRefusal`, `…ThrowsUnexpectedly_StillFailsClosed`, `FetchAsync_EveryRefusalIsATypedExceptionLoggedAtWarning`; caller-side `ARefusedPastedUrl_LeavesTheTextIntact_AddsNoAttachment_LogsAtWarning_AndStillSends` (9 reasons) | **Strong, and the caller half is what this unit added.** The caller test asserts all four properties per reason: HTTP 200, the text block unchanged, no attachment block, no stored row, and a `Warning` carrying the reason and no URL. The one reason that is *not* "send without it" is the kill switch, argued in [Kill switch](#the-kill-switch-and-how-it-fails-closed). |

### Behaviour 14 and the one deliberate exception

The plan's item 14 says a rejected fetch "leaves the message text intact and adds no attachment; it
logs at `Warning` and returns". It does **not** say every rejection must return `200`, and strategy
§4 C7 (adopted, §7 check 9) makes the disabled feature an explicit `400`. The caller's mapping,
therefore, is:

| Refusal reason | Response | Why |
|---|---|---|
| `feature-disabled` | `400` `{code:"feature-disabled"}`, "Upload the image file instead." | The deployment is answering a capability question the client can act on. There is no image to silently drop, and silently sending would make a disabled deployment look like a flaky one. |
| `unparseable`, `scheme-not-allowed`, `insecure-scheme-not-allowed`, `credentials-not-allowed`, `port-not-allowed`, `host-not-allowed`, `allowlist-malformed`, `unresolvable-host`, `non-public-address`, `too-many-redirects`, `invalid-redirect`, `timeout`, `too-large`, `content-type-not-allowed`, `content-signature-mismatch`, `fetch-failed` | `200`, the message is stored without the image, `Warning` logged | The URL is bad, not the deployment. Item 14's whole point is that a URL the server would not fetch must not cost the user their message. The alternative — a `400` per reason — would make every typo a failed send and would turn the endpoint into a finer-grained SSRF oracle. |

The kill-switch case is also the only refusal that performs **no write at all**: it is answered
before the store is reached, so a `400` never leaves an orphan attachment behind. That is asserted
(`KillSwitchDisabled_…_AndStoresNothing`).

## The rebinding story

Validation-then-connect with a second DNS lookup is the classic rebinding hole: the attacker's
resolver answers `1.2.3.4` when the guard asks and `169.254.169.254` when the socket library asks.
The design closes it by removing the second question.

1. `ValidateAsync` (`ImageUrlFetcher.cs:197-227`) calls `IImageUrlHostResolver.ResolveAsync`
   **once** and receives *every* address (`Dns.GetHostAddressesAsync`). If any address is
   non-public, the whole resolution is refused — a single public answer beside a private one is the
   shape a rebinding answer takes, and it is refused (`:214-222`).
2. Only then is `addresses[0]` returned and written to the request under
   `ImageUrlPinningHandler.PinnedAddressKey` (`:93`).
3. `SocketsHttpHandler.AllowAutoRedirect=false` and the stack's own name resolution is bypassed
   because `ConnectCallback` is supplied: `ConnectPinnedAsync` reads the pin and opens the socket to
   exactly that `IPEndPoint` (`ImageUrlPinningHandler.cs:75-82`). The URI is left untouched, so TLS
   SNI and the `Host` header still name the original host (proven on a real socket by
   `TheShippedHandler_ConnectsToThePinnedAddressAndKeepsTheOriginalHostHeader`).
4. The pin is per **request**, read from `context.InitialRequestMessage.Options` — so a pooled,
   shared handler is still pinned per request rather than per client.

**What happens if `ConnectCallback` throws.** It throws `InvalidOperationException` when the request
carries no pin or a null pin (`:67-73`). That escapes the handler, is wrapped by the stack as an
`HttpRequestException`, and lands in the fetcher's catch-all, where it becomes a typed
`fetch-failed` refusal (`ImageUrlFetcher.cs:156-165`) — fail closed, no socket opened. A direct
caller of the named client that forgets to pin therefore cannot reach anything: the only two
outcomes are "connect to a validated address" and "refuse".
`TheShippedHandler_RefusesToConnectWhenNoAddressWasPinned` asserts exactly that (it asserts the
inner `InvalidOperationException`), and `FetchAsync_WhenAnyResolvedAddressIsNonPublic_SetsNoPin`
asserts that a poisoned resolution never even builds a request.

**What if no pin is set but the caller is the fetcher.** Not reachable: `ValidateAsync` returns a
non-null `IPAddress` (or throws) and the very next statement sets it, before `SendAsync`.

**TLS, SNI and `Host` stay bound to the hostname, not the address.** Because the request URI is
never rewritten to the IP, the TLS handshake still sends SNI for `example.com` and the request still
carries `Host: example.com`. The socket simply terminates at the pinned address. That is the correct
construction: rewriting the URI to the IP would break SNI, break the `Host` header, and (with
certificate validation on) fail the handshake for every real CDN. `SocketsHttpHandler` validates the
server certificate against the **hostname in the URI**, so a rebinding attacker who can answer DNS
can still only present a certificate for the hostname they claimed — they cannot make us accept a
certificate for `169.254.169.254`. The real-socket test
`TheShippedHandler_ConnectsToThePinnedAddressAndKeepsTheOriginalHostHeader` asserts the preserved
authority; the certificate property follows from the URI being untouched and was not separately
tested (it needs a TLS server with a certificate, which is out of scope here — recorded in
non-coverage).

**Residual (F2).** The pin's authority lives on the request, not in the handler, so the guarantee
"every socket goes to the address validated for *this* request" holds only while the handler's
connection pool cannot hand a socket for authority *X* to a request pinned to a *different*
address for the same *X*. In this codebase that cannot happen — the fetcher pins
`addresses[0]` for `uri.DnsSafeHost`, and two requests for the same authority would have to observe
two different DNS answers within one connection lifetime (`PooledConnectionLifetime` = 2 min) while
the pool still held the first socket. It is not exploitable as written; it is recorded because the
next caller of the named client may not be as careful: it is the **request option**, not the
handler, that decides where the socket goes, and it is not enforced anywhere in the type system.
The robust form is to partition the pool by address (a per-hop throwaway handler, or an address in
the pool key) so a mismatch is impossible rather than merely unreachable. This is a design note, not
a defect: no test can currently construct the mismatch because the resolver is called exactly once
per hop and all its answers must be public.

**What pinning does *not* claim (F8).** Pinning defeats DNS rebinding; it does not authenticate the
peer. Once the address is judged public, the fetcher trusts it: it inspects no certificate for
identity beyond the transport's own hostname check, and it treats the response as an image only if
the bytes say so. An attacker who controls a *public* host can therefore serve us an image; that is
the feature working as designed, not a bypass.

## The streaming cap and the timeout budget

**Cap.** `MediaContentTypes.MaxFileBytes` is 5 MB. The reader never asks for more than
`cap + 1 - buffered` bytes and stops the instant `cap + 1` bytes have arrived
(`ImageUrlFetcher.cs:351-381`). The declared `Content-Length` is checked first so an honest
over-cap body is refused before a byte is read (`:322-328`). **Peak memory on this path is
therefore bounded by the cap, independent of how large the peer's body actually is** —
`ReadAsByteArrayAsync` is nowhere on the path, which matters because the same unbounded pattern
already exists at `WhatsAppService.cs:102` (salon §7.3 notes it as a hardening item).

I measured the streaming path rather than asserting it. A temporary probe (since deleted) served a
body of unknown length that streamed twice the cap while counting what the fetcher pulled. The
result is the decisive number:

```
refusal=too-large
hops with bodies=1
body[0] pulled=5242881 (cap+1=5242881)
total allocated bytes=16912024 (= 16.1 MB)
cap=5242880 (5.0 MB)
```

So the reader stopped at exactly `MaxFileBytes + 1` bytes against a body twice that size, which is
what distinguishes a streaming cap from a post-hoc check. The same run measured **≈16.1 MB of
transient allocation for one capped 5 MB read** (the 5 MB read buffer, the `MemoryStream` doubling
to just past the cap, and the `HttpContent`'s own buffers — roughly 3.2× the cap). That is managed,
garbage-collected memory, not held residency; it is the price of never calling
`ReadAsByteArrayAsync` and is bounded by the cap.

**The per-hop question, answered by the same probe.** `ReadWithinCapAsync` is called once per
**successful** hop, so in principle a redirect chain could buffer a cap-sized body per hop. It
cannot in practice, and the probe shows why: `ImageUrlFetcher` branches on the status code
(`:99-126`) and a redirect hop's body is **never read at all** — `hops with bodies=1` even with
`maxRedirects = 2` and a server that streams a full body on every response. Only the final,
non-redirect hop's body reaches the reader. **Therefore the peak is one cap-sized read per fetch,
not `(maxRedirects + 1)` of them**, and the redirect chain does not multiply the cap. The residual
is the ≈16.1 MB transient allocation above, not a per-hop accumulation. See **F3**.

**Timeout.** The total budget is `Media:ImageUrlFetchTimeoutSeconds` (default 10), applied as one
linked `CancellationTokenSource.CancelAfter` created **before** the redirect loop
(`:83-84`), so the budget covers every hop, the header wait, and the body read together. Redirects
cannot buy more time. On top of that, `ConnectTimeout` bounds the TCP handshake independently
(`ImageUrlPinningHandler.cs:55`), so a peer that accepts nothing releases its pool slot rather than
holding a request open until the total budget expires. `OperationCanceledException` raised by the
caller's own token is rethrown untouched (`:145-149`); a budget expiry becomes a typed `timeout`
refusal (`:150-155`). Two tests cover header-stall and body-stall (slow-loris) separately.

**What hostile peers can still cost us, enumerated.**

| Peer | Cost ceiling |
|---|---|
| Slow-loris (headers never arrive) | One socket attempt + one budget (10 s); `ConnectTimeout` frees the handshake independently. Test: `FetchAsync_WhenThePeerDoesNotSendHeadersWithinTheBudget_…`. |
| Slow-loris on the body (headers then silence) | Same single budget, because the token bounds the body read too. Test: `FetchAsync_WhenTheBodyStalls_…`. |
| Infinite body | 5 MB read, then a `too-large` refusal at `cap + 1`; measured above. The peer's remaining bytes are never requested. |
| Redirect loop | `maxRedirects + 1` requests, each fully re-validated; refused as `too-many-redirects` at the cap. Test: `FetchAsync_RefusesAThirdRedirect`. |
| Many parallel sends by an authenticated member | One in-flight fetch, ≤ ~16 MB transient allocation, ≤ 10 s, ≤ 3 hops each. Not per-org quota'd (see **F6**); bounded by the authenticated route's existing rate limiting and by the feature flag. |
| DNS that never answers | `Dns.GetHostAddressesAsync` runs inside the same budget token, so resolution is bounded too. |

There is **no per-org quota on this path**; the operator's controls are the kill switch, the
allow-list, and the platform rate limiter. That is a standing operational consideration, not a
finding against this code, and it is called out in the recommendation.

## Payload smuggling and content policy

The question is sharp: **can a stored asset ever be something other than what the sniff said?** The
allow-list is a statement about what may be *stored*, and the sniff is what makes it true. The path
is:

1. **Declared type** — `MediaContentTypes.IsImage(response.Content.Headers.ContentType?.MediaType)`
   (`ImageUrlFetcher.cs:314-320`). This is the sender's *claim*; it exists only to reject an obvious
   non-image early (`text/html`), and it can never cause a body to be accepted, because:
2. **Declared `Content-Length`** over the cap is refused before the body is read (`:322-328`).
3. **The streaming read** stops at `cap + 1` (measured above).
4. **Magic-byte sniff** — `MediaContentTypes.Sniff(bytes)` (`:334`), then
   `sniffed is null || !MediaContentTypes.IsImage(sniffed)` is refused (`:335-340`).
5. **The returned type is the sniffed one** (`:343`), and the caller stores exactly that.

So the answer is **no for the image path**, and I checked the two ways it could be "sort of yes":

- **A `%PDF-` body behind `Content-Type: image/jpeg`.** `Sniff` returns `application/pdf`
  (`MediaContentTypes.cs:210-213`, a bounded leading-window search), and `IsImage` is false, so it
  is refused as `content-signature-mismatch`. The PDF arm of the shared sniffer is deliberately
  *storable but not an image*, and this path is an image path, so the type lands in the refusal
  branch rather than the store. It is guarded; it is not **tested** (finding **F4a**), which is the
  one thing I would change.
- **A real image whose first ~1 KB contains the bytes `%PDF-`** (for example, a JPEG comment
  segment carrying a PDF header). The shared `Sniff` classifies it as `application/pdf`, so the
  fetcher refuses an image that a browser would render. That is a false refusal for the *fetcher*
  (fail closed — a denial of the feature, not a smuggling hole), but the same bounded-window search
  is used by the **upload/sniff path**, where a JPEG with an embedded `%PDF-` marker in its first
  1 KB would be stored as `application/pdf` while the bytes are a JPEG. The consequence there is a
  content-type mismatch, not script execution: `application/pdf` is on the storage allow-list, the
  serve path re-checks with `SafeServe` and sends `nosniff` (`ConversationEndpoints.cs:545`), and a
  JPEG cannot be a polyglot PDF in any useful sense. I record this as a cross-cutting note on
  `MediaContentTypes.Sniff` (finding **F9**) rather than a fetcher defect, because the fetcher
  refuses it either way.

Beyond the type: because the type stored is the sniffed type, and the bytes stored are the bytes
sniffed, the stored `(ContentType, bytes)` pair cannot disagree by construction. The one remaining
smuggling surface is not in this code — it is whether a consumer downstream trusts the *declared*
type instead of the stored one, which is outside this review's scope (and is why the sniffed type
is what is persisted).

## The kill switch and how it fails closed

`Media:ImageUrlUploadEnabled` defaults `false` in `MediaOptions.cs:49`, in `appsettings.json:115`,
and in the fact that every unit test that wants the feature sets it explicitly. It is checked
**first** in the fetcher (`ImageUrlFetcher.cs:67-74`): before parsing, before resolution, before any
socket, and before any log line that could name the URL. `FetchAsync_WhenTheFeatureIsDisabled_…`
asserts all four (typed `feature-disabled` refusal, no resolver call, no handler request, a
`Warning`).

The caller treats `feature-disabled` as the one refusal it must not absorb: `SendMessageAsync`
catches it and returns `400` with `code = "feature-disabled"` and an instruction to upload the file
instead, before any row is written. The default is not changed by this unit, and no configuration
file was touched. Disabling the feature is therefore a complete rollback with no data consequence
(the strategy's Wave 4 rollback: `Media:ImageUrlUploadEnabled=false`, "none").

**Every other fail-closed case, enumerated** (each is a refusal type, never an exception that
escapes to the global handler, and never a silent success):

| Case | What happens | Evidence |
|---|---|---|
| Feature disabled | `feature-disabled` before parsing, resolution, socket or URL logging; caller answers `400` | `FetchAsync_WhenTheFeatureIsDisabled_FailsClosedWithoutResolvingOrConnecting`; caller test `KillSwitchDisabled_…_AndStoresNothing` |
| Malformed `Media:ImageUrlAllowlist` entry | `allowlist-malformed` **before** resolution (`EnsureHostAllowed:285-290`), so a list that cannot be parsed denies everything instead of silently becoming "any host". Startup also refuses it (`MediaOptionsValidator.ValidateImageUrlAllowlist`) | `FetchAsync_WithAMalformedAllowlistEntry_FailsClosedBeforeResolving` (asserts the resolver was not called) |
| Host resolves to nothing | `unresolvable-host` — an empty answer is refused rather than retried against a fallback | `FetchAsync_WhenTheHostResolvesToNothing_IsRefused` |
| Resolver throws (SERVFAIL, timeout, bad name) | The exception propagates out of `ResolveAsync` into the catch-all, which logs the exception **type** and raises `fetch-failed` | `FetchAsync_WhenTheTransportThrowsUnexpectedly_StillFailsClosed` (an `InvalidOperationException` from the handler becomes a typed refusal with no message leakage) |
| Any resolver exception that is *not* an `ImageUrlFetchException` | Same catch-all: `fetch-failed`, fail closed | Same test; the `catch (Exception)` is the last arm in the fetcher |
| A non-public answer next to a public one | `non-public-address` for the whole resolution; **no request is built and no pin is set** | `FetchAsync_WhenAnyResolvedAddressIsNonPublic_IsRefused` + `…_SetsNoPin` |
| Caller cancellation | Deliberately **not** a refusal: the `OperationCanceledException` propagates untouched so the caller's own cancellation is not disguised as a fetch failure | `FetchAsync_WhenTheCallerCancels_PropagatesTheCancellation` |

## The URL-logging scrub, and what an operator can still see

**Never logged:** the URL's path, query, fragment, user info, or any substring of the input.
`ImageUrlFetchException.Message` is static per reason (`ImageUrlFetchException.cs:9-12`), so even a
caller that surfaced the message would leak nothing; the transport exception is logged by **type
name only** (`ImageUrlFetcher.cs:156-165`), explicitly because `HttpRequestException.Message` may
quote the URL.

**Logged (Information on success, Warning on refusal):** `host` (`uri.DnsSafeHost`), a 16-hex-char
truncated SHA-256 of the full URL (`urlHash`), the byte count, and the sniffed content type.
`FetchAsync_NeverLogsTheFullUrl_…` asserts the URL, the query secret, the path segment and `token=`
are all absent on the success, refusal and transport-throw paths, and that the host and hash are
present. The caller-side Warning logs only the reason code.

**What an operator can still see, honestly:**

- **The host.** A DNS name is inherently observable; it is also written to disk by the provider and
  is the only thing that makes a refusal diagnosable. If a URL's host were itself a secret, this
  would leak it — that is not a realistic input for this feature.
- **A stable correlation handle.** `urlHash` is unsalted SHA-256 truncated to 64 bits; it
  correlates two lines about one URL without revealing it, and a 64-bit space makes brute-forcing a
  full URL from the log line infeasible but not information-theoretically impossible for a
  low-entropy URL. It is a log correlation id, not a confidentiality control.
- **The response's sniffed content type and byte count.** Not sensitive.
- **The API's normal request logging** still records the *route* (`/…/messages`) and the acting
  user; the URL travels in the request **body**, which is not logged by the endpoint. I did not
  inspect live OTLP/proxy egress (see non-coverage), so if a proxy or the telemetry exporter dumps
  request bodies, this scrub would not help — that is outside the fetcher's control.
- **Out-of-band logging.** The fetcher cannot stop the *fetched host* from logging the request IP,
  and it cannot stop a corporate egress proxy from logging the full URL. The first is inherent; the
  second is an operational property of the deployment.

**Log injection: measured, not assumed.** The only caller-controlled value that reaches a log line
is the host, and the only other caller-derived value is the URL *hash* (hex, generated). `Uri`
normalisation strips or percent-escapes CR/LF from the authority, and `DnsSafeHost` returns a
canonical host or `(unparseable)`. I probed the two shapes that matter, with a capturing logger:

```
# success path, URL = https://example.com/path/SUPERSECRETCANARY?token=SUPERSECRETCANARY
LOGGED: Pasted image URL fetched. host=example.com urlHash=daf31d0c17184846 bytes=4 contentType=image/jpeg
contains secret=False        contains newline-in-message=False

# URL = https://example.com/photo%0d%0aINJECTED-LOG-LINE.jpg
MSG: Pasted image URL fetched. host=example.com urlHash=09c344579b7ad413 bytes=4 contentType=image/jpeg
any raw newline=False
```

A percent-encoded CRLF in the path is neither logged (the path is not logged at all) nor able to
forge a line, and the information-level line contains no caller-controlled text beyond the
canonical host. **No log-injection finding.** The caller-side Warning is likewise a constant format
string plus the reason code (a compile-time constant from `ImageUrlFetchReasons`), so it carries no
caller-controlled text either.

## Findings

Severities: **HIGH** (blocks ship), **MEDIUM** (ship with a recorded decision), **LOW**
(documented/informational). A finding is a defect in the reviewed code only if it is marked as
such — several rows are test gaps or design notes and are labelled that way.

| ID | Finding | Severity | Disposition |
|---|---|---|---|
| **F1** | **`ImageUrlUploadEnabled` defaults `false` everywhere, so the entire attack surface is closed in the shipped configuration.** Nothing to fix; recorded because the review's severity assessment for T1–T8 depends on it. The default is a configuration fact (`appsettings.json:115`, `MediaOptions.cs:49`) and a fetcher behaviour asserted by `ImageUrlFetcherTests.FetchAsync_WhenTheFeatureIsDisabled_FailsClosedWithoutResolvingOrConnecting`. The caller-side tests drive the refusal through the fetcher seam, so they prove the caller's `400` mapping, not the default value. | LOW (informational) | Accepted. Do not flip the default in this slice. Operators enabling it should set `Media:ImageUrlAllowlist` as well. |
| **F2** | **The pin's authority is the request, not the handler** (`ImageUrlPinningHandler.cs:40-42`, `:62-88`). A future caller of the named client could pin address *A* for authority *X* while the pool still holds a socket for *X* opened for address *B*; the pinned address is then not what the socket uses. **Not exploitable as written**: the fetcher calls the resolver once per hop, requires every answer to be public, and always pins `addresses[0]` for `uri.DnsSafeHost`. | LOW (design note, not a defect) | Record in ADR-022. If a second caller is ever added, either pin via a per-hop throwaway handler or partition the pool by address. No code change requested from U4.2. |
| **F3** | **Per-hop cap and its transient allocation: measurement, not a defect.** I measured the streaming path with a temporary probe (deleted; reproduction below). Findings: (a) the reader pulled exactly `MaxFileBytes + 1 = 5,242,881` bytes from a body twice the cap, so the cap is enforced *while* reading, not afterwards; (b) `hops with bodies = 1` with `maxRedirects = 2` — **redirect hops never read a body**, because the code branches on the status code at `:99` before `ReadImageAsync` is reached, so a redirect chain does **not** multiply the cap; (c) one capped 5 MB read allocated **16,912,024 bytes (≈16.1 MB)** in total — the 5 MB read chunk, the `MemoryStream` doubling to just past the cap, and `HttpContent`'s own buffers, i.e. ≈3.2× the cap, all GC-eligible and released when the fetch ends. **Peak per fetch is one cap-sized read, bounded and transient.** The only residual is the ≈16 MB-per-capped-fetch transient allocation, which is inherent to buffering a 5 MB attachment in memory and is bounded by the cap. | LOW (bounded resource, informational) | Accepted. No code change. If the transient peak is ever worth reducing, stream the bytes straight into the store instead of returning a `byte[]`; that is a U4.2/architecture change, so it is **reported, not fixed**. |
| **F4** | **Two test gaps in security-critical guards.** (a) The **guard that a PDF is refused on the fetch path** — a body whose sniff returns `application/pdf` is rejected by `ReadImageAsync:335-340` because `IsImage("application/pdf")` is false — has **no test**: the existing PDF test only covers a declared `Content-Type: application/pdf`, not `%PDF-` bytes behind an image header. (b) Three entries on the address deny list (`100::/64` discard-only, `5f00::/16` SRv6, `192.88.99.0/24` 6to4 relay anycast) have no test naming them. Both are gaps in proof, not in behaviour: the code as reviewed refuses all four inputs. | LOW (test gap) | Recommend one test per guard before U4.2's slice is considered frozen: a `%PDF-`-headed body must produce `content-signature-mismatch`, and the three ranges must produce `non-public-address`. **Do not edit U4.2's test file from this lane**; recorded for the owning lane. |
| **F5** | **`FetchedImage` does not encode the invariant that its type is an image.** A caller that stored `fetched.ContentType` without its own `IsImage` check would accept `application/pdf`, because `ImageUrlFetcher`'s guard is a separate statement. The caller added by this unit does **not** re-check and is safe only because the fetcher checked. Reproduction of the latent hazard: change `ImageUrlFetcher.cs:335` to `sniffed is null` and the fetcher will happily return `application/pdf` for a `%PDF-` body; nothing in the type system stops a caller from storing it. | LOW (hardening) | No action for S6 (the guard is present and the single caller is safe). Suggested for U4.2's next touch: make the image check structurally impossible to skip — e.g. a `FetchedImage.TryCreate`-style factory, or a `MediaContentTypes.ImageType` value object. |
| **F6** | **Caller-side: the pasted-URL path has no per-org rate limit or dedup of its own.** An org member can cause one outbound fetch, one 5 MB buffer and one attachment row per send. There is no `ImageUrl`-specific quota; the controls are the existing authenticated-route rate limiting and the feature flag. With `ImageUrlUploadEnabled=true` this is a small, authenticated amplification surface (our egress, not the attacker's). | LOW (operational) | Accepted for S6 — the same member can already upload 5 MB files. Recorded so the S9 rollout note names `Media:ImageUrlAllowlist` and the rate limiter as the operator's controls. |
| **F7** | **Caller-side: the new endpoint code is not covered by a "cross-tenant" test.** `ResolvePastedImageAsync` stores only after `IConversationRepository.GetVisibleToUserAsync` resolves the conversation the send already requires, so an invisible conversation yields `null` here and the existing `404` from `SendStaffNoteAsync`; the route's `BoutiqueConversationAccessPolicy` and org scope are unchanged. I could not find a caller-side test that sends a pasted URL into another org's conversation and asserts nothing was stored. | LOW (test gap) | The existing `ConversationEndpointsIntegrationTests.NonMember_IsDenied` covers the route gate, and the store is only reached after the visibility read, so the risk is low. Recommend a cross-org pasted-URL case be added by the owning lane when the endpoint tests are next touched. |
| **F8** | **The pin governs *which address is connected to*, not *which IP is spoken to*; and the probe confirms redirect bodies are never read.** Pinning defeats DNS rebinding by removing the second lookup, and TLS SNI/`Host` stay on the hostname (proven on a real socket). The residual properties, stated so they are not mistaken for guarantees: the connected IP is trusted once it has been judged public (the transport still validates the certificate against the URI hostname, so a rebinding attacker cannot present a certificate for an address they do not own); and each redirect hop builds a fresh `HttpRequestMessage` with no headers, so no cookie or `Authorization` can ride a redirect forward (see the cookie section). | LOW (informational) | Accepted. Record the trusted-IP property in ADR-022 so a future "verify the image is from a known CDN" requirement is not assumed to be already met. |
| **F9** | **Cross-cutting: `MediaContentTypes.Sniff`'s bounded PDF window can misclassify an image.** `SniffPdf` searches the first 1024 bytes for `%PDF-` (`MediaContentTypes.cs:272-287`), so a JPEG/PNG whose first kilobyte happens to carry that marker is classified as `application/pdf`. On the fetch path this is a *false refusal* (fail closed, no smuggling). On the **upload/sniff path** it would store the row as `application/pdf` over image bytes. Impact is a content-type mismatch only: `application/pdf` is storable, the serve path re-checks with `SafeServe` and sends `nosniff`, and a JPEG cannot execute as a PDF. Not a fetcher defect and not introduced by this unit; reported because the review found it and because a future consumer that branches on `application/pdf` (for example a PDF-specific pipeline) would be misled. | LOW (informational, pre-existing shared helper) | Out of this unit's lane and out of S6's scope. Record for the media workstream's next touch of `MediaContentTypes`: prefer an anchor at offset 0 plus a tight, documented tolerance window, or mark the sniff result ambiguous rather than choosing a type. |

**No HIGH or MEDIUM finding.** I tried to falsify the two controls that carry the slice — the pinned
connect (T3) and the all-addresses-public rule (T1/T2) — and could not: the resolver is the only
place a name is resolved, the connect callback is the only place a socket is opened, and both are
bounded by an allow-list of addresses rather than a deny-list of names. The residual risks are the
nine LOW rows above, and F2/F3/F5/F8/F9 are the ones a future change is most likely to turn into a
real defect, which is why they are named rather than omitted.

**Reproducing F3.** The probe that produced the numbers in [the cap section](#the-streaming-cap-and-the-timeout-budget)
was a throwaway `HttpContent` whose `TryComputeLength` returned `false` (so the declared-length
pre-check could not fire) and whose stream counted the bytes the fetcher pulled, placed behind a
handler that answers `302` for the first two hops and a counting body for the last. Its temporary
test file has been deleted; the four output lines quoted above are the whole artefact. To re-run it,
copy the `CountingImageStream` pattern from `ImageUrlFetcherTests.cs:1154-1216`, make
`TryComputeLength` return `false`, and wrap `GC.GetTotalAllocatedBytes(precise: true)` around the
`FetchAsync` call.

## What this review does not cover

- **No live OTLP, proxy or packet inspection.** I did not observe actual egress, so I cannot attest
  that no proxy or telemetry exporter records request bodies containing the pasted URL. The claim
  "the URL is not logged" is a claim about the reviewed code and the `ILogger` calls it makes.
- **No penetration test and no hostile DNS server.** The rebinding story is argued from the
  resolver/connect structure and from the existence tests, not from an attack against a real
  attacker-controlled resolver. There is no test in the suite that changes a DNS answer between
  validation and connect (see F2 for the related residual).
- **No TLS handshake inspection.** That the transport validates the server certificate against the
  URI's hostname (which is what keeps a rebinding attacker from presenting a certificate for a
  pinned address) is a property of the un-rewritten request URI and `SocketsHttpHandler`'s defaults,
  not something I observed: testing it needs a real TLS server with a certificate chain, which is
  out of scope here. The preserved `Host`/SNI is proven on a plaintext loopback socket.
- **No IPv6 connectivity test.** The address policy's IPv6 deny list was reviewed by reading and by
  unit tests over `IPAddress.Parse`, but no test dials a real IPv6 host or a 6to4/Teredo peer.
- **No Cloudinary-backed run.** The caller-side tests run with `Media:Provider=database`. The
  `CloudinaryAttachmentStore` path (tags, `source:url`, dual-write) is covered by that adapter's own
  tests, not by this review.
- **No load or concurrency test.** Timeout/streaming/pooling behaviour under many simultaneous
  fetches was reasoned about, not measured. The cap and allocation numbers in this review come from a
  single-request probe, since deleted.
- **No review of files outside the scope above.** In particular `WhatsAppService.GetMediaAsync`
  (salon §7.3's hardening note) and the `MediaAccessService` token path were not re-reviewed here;
  F9 touches `MediaContentTypes.Sniff`, which is shared with the upload path, and was reviewed only
  as far as the fetch path consumes it.
- **No configuration/deployment review.** Whether a given environment sets
  `Media:ImageUrlUploadEnabled=true`, and what it sets `Media:ImageUrlAllowlist` to, is a
  deployment fact outside this review.

## Recommendation

**SHIP S6.** No HIGH or MEDIUM finding; all fourteen behaviours are implemented and independently
assessed as strong; the shipped default is closed (`false`); every refusal except the disabled
feature leaves the user's message intact and adds nothing; the caller-side suite (16 cases: the
success path, the kill switch, and nine refusal reasons) and the 117-case fetcher suite are green,
as is the rest of the filtered conversation suite
(`Passed! - Failed: 0, Passed: 151, Total: 151`). **Option A is not required** — the fetcher's guard
is the stronger answer, and the kill switch remains the fallback if the feature must be withdrawn.

Conditions attached to "ship", none of which blocks the merge:

1. **Keep `Media:ImageUrlUploadEnabled=false`** in the shipped configuration. Operators enabling it
   should also set `Media:ImageUrlAllowlist`; that is the documented mitigation and the S9 rollout
   note should say so (F1, F6).
2. **Record F2 (the pin is a request option, not a handler guarantee), F5 (the image invariant is
   not in the type) and F8 (the connected IP is trusted, not authenticated) in ADR-022** as known
   residuals, so the next caller of the named client cannot silently invalidate the pin.
3. **Add the two tests in F4** (a `%PDF-`-headed body refuses; the three untested ranges refuse) in
   U4.2's owning lane, before that lane's slice is treated as frozen. They are proof gaps, not
   behaviour gaps.
4. **Carry F9 (`MediaContentTypes.Sniff`'s PDF window) to the media workstream**, not to S6. It is
   pre-existing, it fails closed on the fetch path, and it needs a shared-helper decision rather
   than a lane-local patch.

If the deadline forces a choice, the fallback remains strategy §7 check 9's: ship Option A — an
explicit `400` telling the client to upload the file — and keep the fetcher unreachable. Nothing
else in this review depends on that choice.
