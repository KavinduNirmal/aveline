# Context-aware action bars on AI message blocks

**Branch:** the feature lands as one change across three surfaces — the tenant dashboard
(`frontend/web`), the Flutter client (`frontend/aveline_mobile`) and the API (`Aveline.Api`).

## What this does

Every AI message in the chat UI is a list of typed content blocks. Each block now carries an action
rail fused to its bottom edge, offering only what that kind of block can do.

| Block (`type`) | The brief's name | Actions |
| --- | --- | --- |
| `suggestion` | suggestion | Copy · Send to customer · Regenerate |
| `piece` | item | Forward |
| `look` | lookbook | Copy · Forward · Regenerate |

Nothing else draws a rail: `text`, `at_a_glance`, `sign_off`, `payment`, `courier`,
`client_message`, `attachment`, `choice` and any unknown type keep rendering exactly as they did.
The brief's own names (`item`, `lookbook`) are accepted as aliases, so a payload written either way
resolves to the same rail.

**There is no Share.** The brief lists it among the general actions but gives it no home in the
per-type table, and every candidate behaviour either duplicated Forward or produced something a
customer never receives. It was dropped rather than shipped as dead chrome.

## The correction that shaped it, and why it matters

The first cut wired "Send to customer" and "Forward" to the existing `POST …/messages` route. That
is wrong, and wrong in a way that would have been invisible: that route writes a staff **note** into
the Salon. The note reaches no customer, and on a client-bound thread it wakes the agent — so a
"send" that looked like it delivered would have left an associate believing a client had been
messaged when nobody had.

Delivery is now its own path, and the two are separate endpoints because they are separate promises:

```
POST /api/v1/orgs/{organizationId}/conversations/{conversationId}/deliver
  body: { text, clientMessageId? }
  200 -> DeliveryResultDto { delivered: true, channel, providerMessageId, message }
  404 -> conversation_not_found
  409 -> no_customer | no_channel_handle | channel_not_connected | channel_unsupported
  502 -> provider_refused
```

`ICustomerDeliveryService` resolves the thread's client, finds a channel the tenant has actually
connected, hands the words to `IWhatsAppService` (Meta WhatsApp Cloud API), and **only after the
provider accepts** records the row with `status: Sent`. Nothing is recorded on a refusal — a stored
`Sent` row is a claim that a customer was messaged, and the transcript is what an associate reads to
check. The new `deliver` route is deliberately *not* the messages route, so a caller cannot reach one
by asking for the other.

**Send to customer** is the client on the open thread; **Forward** is a client the associate picks.
Both confirm before anything leaves, because a message on a client's phone cannot be recalled. The
picker offers only threads whose client can actually be reached (a `customerId` or a channel
`externalRef`); the client-less concierge is not a destination.

**Regenerate** re-runs the question, not the answer:

```
POST …/conversations/{conversationId}/messages/{messageId}/regenerate  ->  202
```

It resolves the staff turn the block replied to (falling back to the block's own first block text),
triggers the agent with the thread's customer context, and answers `202` because the fresh content
arrives as `message.created` events over the Salon hub like every other agent reply.

## Files

### API

| File | Change |
| --- | --- |
| `Modules/Conversations/Services/ICustomerDeliveryService.cs` | **new** — the outbound path's contract, `DeliveryRefusal` and `DeliveryOutcome` |
| `Modules/Conversations/Services/CustomerDeliveryService.cs` | **new** — channel resolution, provider send, delivery record |
| `Modules/Conversations/Services/ConversationService.cs` | `RegenerateAsync`, plus a `FirstTextBlock` reader |
| `Modules/Conversations/Services/IConversationService.cs` | the `RegenerateAsync` member |
| `Modules/Conversations/DTOs/MessageDtos.cs` | `DeliverToCustomerRequest`, `DeliveryResultDto` |
| `Endpoints/ConversationEndpoints.cs` | the two routes and their refusal-to-status mapping |
| `Modules/Conversations/ConversationsModule.cs` | DI registration |

### Web

| File | Change |
| --- | --- |
| `conversation/blockActions.ts` | **new** — the mapping as data: allowed actions per type, availability reasons, the block→text payload, delivery targets |
| `conversation/BlockActionBar.tsx` | **new** — the joined rail |
| `conversation/ActionableBlock.tsx` | **new** — content + rail in one clipped box |
| `conversation/BlockActionDialogs.tsx` | **new** — forward picker and send confirmation |
| `conversation/useBlockActions.tsx` | **new** — per-block state, clipboard, delivery, regeneration |
| `conversation/blocks.tsx` | `suggestion`/`piece`/`look` render through `ActionableBlock`; `BlockList` threads `messageId` + bridge |
| `conversation/MessageBubble.tsx`, `MessageThread.tsx`, `SalonPanel.tsx`, `AvelineChatDrawer.tsx` | pass the bridge down and mount the dialogs |
| `contexts/ConversationsContext.tsx` | `deliverToClient`, `regenerate`, `regenerateAveline` |
| `lib/conversations-api.ts` | `deliverBlockToCustomer`, `regenerateMessage`, `DeliveryRefusedError` |

### Flutter

`frontend/aveline_mobile`: the block-action mapping, the rail widget, `deliver`/`regenerate` on the
thread repository, controller state and wiring, plus tests and the feature README. See the mobile
section of the diff for the exact files.

### Docs

- `docs/api/README.md`, `docs/api/openapi.yaml` — both new routes, `DeliveryResultDto`, and why
  delivery is not the messages route.
- `docs/frontend/tenant-dashboard.md` — the mapping, the rail's shape, the delivery decision, the
  per-block state model, the recorded deviation, and the tests.
- `frontend/web/src/docs/salon.md` — the tenant-facing page: what each card offers, what the actions
  do, and the limits (a delivery cannot be recalled; Regenerate adds rather than erases).
- `frontend/aveline_mobile/lib/features/conversations/README.md` — the mobile feature doc.

## UI decisions

**The rail is the card's bottom edge, not a row of buttons beneath it.** `ActionableBlock` holds the
content and the rail inside one `overflow-hidden` box, so the card's own radius clips the rail's
bottom corners; the segments are joined, separated by a single hairline (`divide-x`), and carry no
rounding of their own. Nothing here uses the shadcn `Button`, whose pill radius is precisely the
floating-chip look the rail is not. Inside a tile row (a `piece` card is ~11rem wide) the printed
word is dropped and the glyph carries the segment, but the accessible name and the tooltip do not
change — the case the brief's "tooltips for icon-only buttons" is written for.

**A disabled segment stays focusable** (`aria-disabled`, never the `disabled` attribute). The reason
an action is unavailable *is* its tooltip, and a truly disabled button leaves the tab order and takes
its explanation with it. Every unavailable segment says why in the app's voice: "This thread isn't
linked to a client yet.", "No other client to forward to yet.", "Aveline is still working on a
reply.", "Another action is already running on this block."

**The server's sentence wins on a refusal.** "This boutique has not connected WhatsApp yet." and
"That client has no WhatsApp number on file." are different things to do next; one generic failure
for both would be a worse answer than either.

**Per-block, per-surface state.** `useBlockActions` is instantiated once per thread surface, so one
surface's in-flight action cannot disable the other's; its pending map is keyed by message id, so one
block can be mid-delivery while another is mid-regenerate, and it disables the rest of *that block's*
segments while one runs.

## Verification

| What | Command | Result |
| --- | --- | --- |
| Web types | `npx tsc --noEmit -p tsconfig.app.json` | clean |
| Web build | `npx vite build` | built |
| Web lint | `npx oxlint src` | 0 errors, 76 warnings (77 before this change; none new) |
| Web tests | `npx vitest run` | 147 files, 1171 tests passed |
| API + delivery unit tests | `dotnet test --filter "ConversationServiceTests\|CustomerDeliveryServiceTests"` | 67 passed |
| New endpoint integration tests | `dotnet test --filter "…Deliver\|…Regenerate"` | 6 passed |
| Flutter analyze | `flutter analyze --no-pub` | `No issues found!` |
| Flutter tests | `flutter test --no-pub` | see below |
| Full backend suite | `dotnet test` | see below |

The Flutter toolchain's SDK cache is a read-only mount in this environment, so `flutter` aborts
before running; the results above were produced through a writable overlay of the SDK (launcher
scripts copied, heavy artifacts symlinked, `PUB_CACHE`/`HOME` pointed at writable directories),
which was removed afterwards.

### The backend suite needs two deployment settings

`dotnet test` fails **611 tests on a clean checkout** in this environment, none of them related to
this change: the test host cannot start, because the local configuration selects
`Media:Provider=cloudinary` while `Media:SigningKey` and `Media:PublicBaseUrl` are unset, so every
`WebApplicationFactory` test dies in `MediaOptionsValidator.ValidateOrThrow`. Supplying them, and
`Credentials:EncryptionKey` (which the credential store needs and delivery reads), drops the failures
to a small residue of Cloudinary-credential-dependent attachment tests:

```
Media__SigningKey=$(openssl rand -base64 32) \
Media__PublicBaseUrl="https://api.aveline.test" \
  dotnet test
```

I did not change any of these settings, and the pre-existing behaviour is worth a separate fix
(the test project should configure its own required options rather than inheriting a developer's
`.env`).

## Open questions

1. **Regenerate does not replace the block, and the brief asks it to.** Stored message rows are
   immutable history: the superseded block stays in the transcript and the fresh reply is appended.
   A view-level replacement would silently reappear on the next read — the transcript is re-fetched,
   not reconstructed — and the alternative, rewriting or deleting a stored row, trades the audit
   trail for a cosmetic detail. A real replacement needs a `supersededByMessageId` column on
   `Messages` and a read-path filter, which is a schema change and its own decision. **Recommendation:
   add that column and filter in a follow-up, then wire the rail's spinner to it.**

2. **Instagram delivery is not implemented, only reported.** `IntegrationType.Instagram` has
   credentials support but no provider and no webhook, so an Instagram-only boutique is answered
   `channel_unsupported` with "Instagram messaging is not connected yet." That is honest, and it is
   also a gap: the brief names Instagram alongside WhatsApp. **Recommendation: build the provider as
   its own slice, with its own webhook, rather than widening this one.**

3. **`ExternalRef` is treated as a WhatsApp handle.** It is whatever the inbound webhook keyed the
   thread by, which today is always a WhatsApp wa-id, so preferring it over the customer's stored
   number is right. If a second inbound channel ever lands, the conversation will need an explicit
   channel column; the resolution rule should move with it.

4. **The two clients differ on the brief's block names.** Web accepts `item` and `lookbook` as
   aliases for `piece` and `look` (unit-tested, defensive); Flutter accepts the wire names only and
   says so in its own module. Both resolve identically for every payload the API actually emits —
   the aliases are for a hand-written payload, not for the wire — but the divergence should be
   settled one way in a follow-up rather than left to drift.

5. **The mobile rail's default-open behaviour on a narrow phone is unverified on a device.** It is
   covered by widget tests, not by a screenshot on hardware.

6. **The mobile Salon cannot deliver.** `salon/presentation/widgets/message_bubble.dart` renders the
   same AI blocks, so it draws the rail, but the concierge thread has no client and the Salon screen
   has no inbox list, so Send to customer and Forward render disabled with their honest reasons.
   Wiring them needs the inbox repository plumbed into the Salon surface. Documented in the mobile
   feature README.

7. **The mobile forward picker reads only the first page of the inbox**, so a client beyond the first
   page is not offered as a destination. Documented in the mobile feature README.

8. **A provider success whose delivery row fails to write is logged and still reported as delivered.**
   The customer does have the message, so this is the honest report, but it leaves the transcript
   missing a row the customer can see. A reconciliation job reading the provider's message log would
   close it; that is a separate piece of work.
