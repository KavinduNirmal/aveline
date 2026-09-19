# Feature: Messages

> **Domain:** Conversations — the Salon, and the threads with clients

## Status

The tab reads as the boutique's message inbox: the shop's name in the title, a
field that searches the threads, and then the threads themselves.

**The inbox draws no read state; the thread writes one.** There is no unread badge
and no unread summary on the list, on purpose (D2 = c). The thread itself owns a
`ConversationReadState` row per `(organization, user, conversation)` and advances it
through `PATCH …/read` with a marker that never moves backwards; the inbox simply
does not draw it yet. When it wants a badge it reads that table's aggregate rather
than growing a second model, so nothing has to be un-shipped to get there.

**The Salon is pinned at the top, always.** Every other thread is with a person.
The concierge is the one channel that is always there and always answers, so it is
drawn as a card rather than as one more row - a blossom, a tinted fill and a
`YOUR CONCIERGE` overline - and it keeps that position while a search is in force.
A search is about finding a person; sliding the concierge out from under the thumb
to do it would be a surprise.

Under it come the client threads, newest word first, then the notices. Each row is
laid out the way a phone's message inbox is: the face, the client's name, the last
word, and when it landed.

Three details are worth pointing at:

- **The last word says who said it.** `You: The blouse is pinned and ready` is not
  the client reporting progress, and a preview that read as though it were would be
  misread every time. Staff and agent messages are prefixed; a client's own message
  is not.
- **A thread waiting on a signature wears a marker.** `AwaitingSignOff` is the one
  status where the thread is waiting on the associate rather than the other way
  round, so it earns a mark a plain row cannot give it.
- **A client has one colour.** The avatar tint comes from the shared
  `avatarTintFor`, the same one the client book uses, so a face does not change
  shade between the two screens.

Tapping the pinned card opens the Salon. Tapping a client row opens the thread
with that client.

## The thread with a client

A client's row opens their thread: the whole exchange, read from the oldest word
downwards, opening at the newest. The list is reversed, which is what keeps the
newest message at the foot of the screen and keeps it there when something is
sent, rather than scrolling the thread by hand.

Two axes decide how a message looks, and they are deliberately independent:

- **The side is who spoke.** The client is on the left, the boutique on the right.
  That is the only thing the alignment means.
- **The treatment is where it went.** A message that reached the client is a
  filled bubble wearing its delivery tick. A note that never left the shop is
  tinted, labelled `NOT SENT`, and carries no tick. A reply an agent staged is
  labelled `AWAITING APPROVAL` and carries its own decision, in the exact place
  the message will sit once it goes out.

That distinction matters more here than in a chat between two people: this thread
is the shop's record of what it told a client, and a note read as a sent message
would be the record lying.

Ticks follow the API's own lifecycle - one tick for accepted, two for delivered,
two in the brand's colour for read - because a second glyph would read as a
different thing rather than as the same thing further along. A tick is a delivery
claim, so it is drawn only for a status that claims delivery: a note and an approved
SignOff carry none.

## Opening on a specific message

A notification can name the thread it is about (`Notification.Data` →
`{conversationId, messageId}`, read by `ThreadDeepLink.fromNotificationData`). When it does, the
thread opens **on the page that holds that message** rather than on the newest words: the
request carries `around`, the server decides which page that is and echoes it, so earlier
history stays reachable through the ordinary "load earlier" control and the client never has to
guess where it stands. An anchor that is not in the conversation, or a notification that named
no message, opens on the newest words as usual.

The ids travel in the payload rather than a route parameter, because the router re-parses its
location whenever the auth or profile listenable fires and `extra` does not survive that.

The tap is routed by `notificationRouteFor`: a notification carrying a `conversationId` opens
`/conversations/thread/{id}` (with `?messageId=` when it named one), and one carrying only a
`customerId` still opens the client book. That route resolves the row through
`ConversationRepository.fetchConversation` before showing the thread - the inbox already holds
the row, a notification knows only the id - and keeps three outcomes apart: the row arrives (the
thread), the row is gone or not visible (its own state), and the organization id is not known
yet (a "not yet" with a retry).

## Briefing blocks

A message is not plain text. Every persona publishes `kind: Note` and puts its
category in the **content blocks** (`docs/architecture/inbox.md` §5), so the thread
draws the blocks rather than the kind. The vocabulary and what the thread does with
each one:

| Block | Drawing |
|---|---|
| `text` | the bubble's words |
| `client_message` | the client's words, on the left, with `from` printed as the channel handle |
| `sign_off` | the overline and the decision row (`AWAITING APPROVAL` / `APPROVED` / `DISMISSED`); its `reason` or `amount` stands in when the message has no text |
| `suggestion` | a draft card with a **Copy** action - Ava's `draft_response` |
| `choice` | the prompt and its options; tapping one calls `POST …/select-customer` and re-reads the thread, so the header's name follows |
| `piece` | one line: the name and, when priced, the price |
| `look` | one line: its name, or "A new look" |
| `at_a_glance` | one line: "N details" |
| `payment` | one line: the amount, or the status |
| `courier` | one line: the carrier and the status |
| any other type | the same one-line fallback (its own `text`, then "Update"), so a block-only message is **never** an empty bubble |

A message may carry several blocks: the text is drawn first and the cards follow
inside the same bubble. A reply whose parent is in the window draws one quoted line
above the bubble; a parent outside the window is omitted rather than fetched.

**D2 = A pins the composer's honesty.** A staff note is stored `Published`, which
means it is visible in the Salon and reached no customer channel, so it keeps the
`NOTE · NOT SENT` label. A test asserts that label, because it is a decision and not
an accident: outbound WhatsApp delivery is a channel-integration plan of its own.
An approved `SignOff` is classified by **kind first**, so it reads `APPROVED` rather
than the note label its `Published` status would otherwise earn. Approval is narrowed
to `approvals:approve`: the server enforces it through the org-scoped
`BoutiqueConversationApproval` policy, and the client only *draws* the **Revoke**
action for a membership that holds it. Revoking appends a `revoked` row to the
immutable decision log and returns the SignOff to `AWAITING APPROVAL`, so it can be
decided again and the inbox's `approval` marker lights again.

Sending is optimistic. The words appear before the API answers, marked
`Sending…`, because an associate who has just tapped send is owed an answer about
whether their words went anywhere. A refusal leaves the message on screen marked
`Failed to send` with a **Try again** that keeps the text. Each composed message
carries one client-generated `clientMessageId` (a UUIDv4 from
`shared/utils/uuid.dart`), and a retry reuses it, so a send that timed out after the
row was stored comes back as that row rather than as a second copy of the sentence.

The header names the client and opens their profile in the client book. A channel
thread whose client is not identified yet prints the handle it was opened from
(`externalRef`) as the stand-in name, rather than a bare "Client", so the associate
can see who wrote; resolving the thread with a `choice` block replaces the stand-in
with the real name. What is deliberately not on this screen: the client's grade,
their history, and anything else the client book already says better. This screen is
the conversation.

## What the API carries today

`Aveline.Api/Modules/Conversations` models one kind of conversation: the `Salon`.
`ConversationDto` now carries the whole list row: `id`, `kind`, `customerId`,
`customerName`, `externalRef`, `threadId`, `status`, `lastMessageAt`, a block-aware
`lastMessagePreview`, `lastMessageKind`, `lastMessageBlock` (the row's category),
`lastMessageAuthor`, `lastMessageAgentKey` and the actionable `markers` set over
`approval | choice | draft`.

`kind` keeps its declared meaning - the thread's nature and audience - and customer
context is the separate `customerId` axis. The client classifies by that context first:
`customerId`, then `externalRef` (a channel thread whose customer is not yet identified),
and only then a general `Salon`. That makes exactly one thread per caller the pinned
concierge, whatever `kind` says.

The client tolerates every optional field being absent, so a row can be drawn from the list
endpoint, from the realtime broadcast, or from a fixture without the screen knowing which.

_(It carries no unread count, and none is drawn: D2 = c removed the badge, and the
per-user read model the thread writes behind it is deliberately not surfaced here yet.)_

## Layers

### `presentation/screens/`
- `conversations_screen.dart` - the Messages dock tab, reachable from the dock's
  side panel and Home's quick actions, both routing to `AppRoutes.conversations`.
  It owns its controller over an injectable `ConversationRepository`, the same
  seam `CustomersScreen` uses. It draws the pinned card whatever the search is: the
  concierge is not one of the results.
- `client_thread_screen.dart` - the thread with one client. Its own `Scaffold`
  rather than a shell body, because a thread is a place you go rather than a tab
  you sit on.

### `presentation/`
- `conversations_controller.dart` - owns the inbox and the order it is read in,
  which is the point of the class. The repository hands back an *unordered* list
  and the controller pins the Salon above it and sorts the rest by the newest word,
  so no source has to remember the rule. The search narrows the client threads and
  the notices and deliberately leaves the Salon where it is.
- `client_thread_controller.dart` - owns one thread: its history, the page it is
  reading from, the message in flight and the draft waiting on the associate.
  History is served oldest first and paged from the oldest end, so it opens on the
  *last* page and walks backwards one press at a time, which keeps the window
  contiguous from the newest message down.

### `presentation/widgets/`
- `aveline_conversation_tile.dart` - the pinned Salon card.
- `conversation_tile.dart` - one client thread, and the sign-off marker.
- `conversation_avatar.dart` - initials on the client's own tint, or the blossom
  for Aveline.
- `thread_message_bubble.dart` - one message in a thread: the side, the treatment,
  the kind-first overline, the quoted parent, the blocks, the tick, and the decision
  a staged reply is waiting on.
- `thread_blocks.dart` - the briefing cards a message carries: the `suggestion`
  draft with its copy action, the `choice` question with its options, and the
  one-line summary every other block type falls back to.
- `thread_composer.dart` - the input at the foot of a thread.

### `data/`
- `conversation_repository.dart` - the inbox contract: every thread the caller can
  see, deliberately unordered. A null organization id surfaces as
  `OrgContextUnavailable`, the shared "not yet".
- `api_conversation_repository.dart` - the inbox contract over `Dio`. This is what
  the app constructs: it reads the active membership's org id through a callback
  at call time, because the id arrives from `GET /orgs/my` after the shell mounts.
- `demo_conversation_repository.dart` - a fixture rather than a production
  fallback: the Salon, six client threads and a notice, with a thread awaiting a
  signature, and ages measured from an injectable clock. No production path
  constructs it; the screen falls back to `EmptyConversationRepository`.
- `empty_conversation_repository.dart` - an inbox with nothing in it, so a screen
  built with no injection renders the honest empty state rather than a seed.
- `thread_repository.dart` - the thread contract: a page of history, a send, and a
  sign-off decision. The decision takes the whole message rather than its id,
  because the API binds it to the hash of the content the approver was shown.
- `api_thread_repository.dart` - the thread contract over `Dio`, following
  `docs/api/openapi.yaml`, including the sign-off endpoint. It reads the active
  membership's org id through a callback **at call time**, because the id arrives
  from `GET /orgs/my` after the shell mounts; a null or blank id is the shared
  `OrgContextUnavailable`, which the controller keeps as a loading state rather
  than an error. It validates that a conversation or message id is UUID-shaped
  before it builds a path, so a bad id names itself instead of returning the
  route constraint's opaque 400. It refuses to decide a draft it has no hash for
  rather than sending a blank one the API would reject with a 400 nobody can act
  on.
- `empty_thread_repository.dart` - a thread with nothing in it, used wherever no
  real source was injected. The inbox used to fall back to a demo repository, so
  the registry's `const ConversationsScreen()` rendered eight invented exchanges
  on a production path; T0 replaced that fallback with this honest stand-in, which
  serves no history and refuses a send or a decision with something readable.
  `ClientThreadScreen.repository` is now **required**, because the screen is built
  only by the inbox and by tests, and both name what they are reading.

### `domain/`
- `conversation.dart` - one thread, mirroring `ConversationDto` and the richer row
  the inbox needs. Also the conversation's `status` and who spoke last.
- `thread_message.dart` - one message, mirroring `MessageDto`, with its ordered
  `ThreadBlock` list. It promotes a forwarded `client_message` to the client's own
  words (and keeps its channel handle), marks a published staff message as a note the
  client never saw, classifies a `SignOff` by kind first, and carries the hash a
  sign-off is bound to and the `clientMessageId` a retry reuses.

## Shared with this slice

- `shared/widgets/avatar_tints.dart` - the one palette, so the client book and the
  inbox agree on a client's colour.
- `shared/widgets/count_badge.dart` - the one count mark, worn by the shell's
  notification badge. The inbox does not use it: it has no count to show.

## Related

- The Salon itself: `lib/features/salon/`, and the dock's centre launcher in
  `shared/widgets/animated_blossom.dart`.
- Contract: `docs/api/openapi.yaml`, the `Conversations` tag.
- Backend: `Aveline.Api/Modules/Conversations/`.

## Known gaps

- **A document opens through the platform viewer.** An image attachment opens full
  screen in an `InteractiveViewer`; a PDF (or any non-image) writes its bytes to the
  app's temporary directory and hands the path to the OS viewer (`open_filex`), because
  a document cannot be previewed in-process. The chip shows its name and size and wears
  an open affordance.
- **Inbound customer media renders the same way.** A customer's WhatsApp image or PDF
  is fetched with the tenant's credentials, stored through the same `IAttachmentStore`
  and appended to the `client_message` as an `attachment` block, so it arrives on the
  left with the caption as the text. A message type outside the allow-list (audio,
  video) is logged and skipped without failing the webhook.

## Attachments

D8 (Q1 reversed) puts images and PDFs in scope in both directions. Storage sits behind a
small `IAttachmentStore` (`StoreAsync`, `OpenReadAsync`, `DeleteAsync`), implemented
today by `DatabaseAttachmentStore` (bytes in a Postgres `bytea` column) and later by a
**Cloudinary adapter, which is the named future provider**: the `MessageAttachments`
row reserves `StorageProvider`/`StorageKey` and carries the stored `Url`, so the swap is
a new adapter plus a config value rather than a read-path migration.

The flow is two-step, which is what lets a picker be cancelled without leaving a message
half-written:

1. `POST …/attachments` stores the file **unbound** and returns `{attachmentId, url, …}`.
   The allow-list is images plus `application/pdf` (the catalog's `ImageContentTypes`
   promoted to `MediaContentTypes`), the cap is 5 MB per file, and a refused upload is
   never stored.
2. The send carries `attachmentIds`; the same idempotent insert that creates the message
   binds them. A foreign, unknown or already-bound id fails the whole send with `400`,
   leaving the uploads sweepable. A replay returns the stored message with the same
   `attachment` blocks.

The response's `contentBlocks` hold the `text` block followed by one
`{type: "attachment", attachmentId, url, contentType, fileName, sizeBytes, width?,
height?}` block per file, **stored with the message**, so a re-list carries them with no
join. An upload that is never sent is deleted by `AttachmentSweepJob` after 24 hours.

On the wire the URL is **not anonymous** (the catalog's `AllowAnonymous` image GET is
deliberately not copied for a customer's file), so the bubble never uses
`Image.network`: it reads the bytes through the shared authenticated `Dio`, renders from
memory, and caches them by `attachmentId`. Tapping an image opens a full-screen
`InteractiveViewer`. The composer's paperclip offers the gallery or the camera
(`image_picker`, re-encoded under the cap), each file uploads as it is picked with the
tray showing progress, a failure is retryable with the text preserved, and the send
waits for every held file rather than naming bytes the server has not written. A
document's chip opens through the OS viewer once its bytes arrive, and the whole thing is
injectable (`attachmentPicker`, `attachmentOpener`) so a widget test never touches a
plugin.

- **The thread marks itself read; the inbox still draws no badge.** The thread owns a
  `ConversationReadState` row per `(organization, user, conversation)`, written through
  `PATCH …/read` with a marker that never moves backwards. Nothing on the inbox shows a
  count yet (D2 = c removed the badge), so this is a written-but-undrawn state: when the
  inbox wants a badge it reads this table's aggregate rather than growing a second model.
  A thread's marker is advanced on open, after a send and when a newer message arrives;
  it is best-effort and never a `local_*` id.
- **Both the list and the thread are live.** The inbox opens its own hub connection
  and applies `ReceiveConversationChanged` tiles in place, re-reading the first page
  when the tile names a thread it does not hold. The thread opens its own connection
  to `salon:{id}`, takes `ReceiveMessage` into the controller (deduped by id, the
  in-flight optimistic row adopted by `clientMessageId`, an older message inserted in
  `(createdAt, id)` order, a `Delivered`/`Read` tick replaced in place) and re-reads
  the window after a reconnect, because anything said while the socket was down was
  never delivered. The strip above the composer says only what the four-field
  `ReceiveAgentState` payload supports - working, searching, using a tool - credited
  to the persona; a malformed payload is dropped rather than read as idle.
- **The curated visual cards are summaries, not cards.** `Look`, `Piece`,
  `AtAGlance`, `Payment` and `Courier` are drawn as one-line summaries rather than
  the imagery and tables the web dashboard shows; the actionable blocks
  (`client_message`, `suggestion`, `choice`, `sign_off`) are full cards. A visual
  slice can promote them one at a time without changing the switch.
- No compose action on the inbox. Search narrows the threads that are loaded; it
  is not a server-side search.
- The inbox loads one page at a time and the footer says `Showing <loaded> of
  <total>` until every thread is in. Search still narrows only what is loaded, and
  the no-matches copy says so; a server-side `q` is the next step if that becomes
  a real complaint.
- `MessageStatus.draft` and `failed` are modelled but never produced by the
  backend, and a message stuck in `AwaitingSignOff` can only be decided from the
  thread it is in - there is no queue of everything waiting on the associate.
