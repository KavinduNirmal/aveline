# Feature: Messages

> **Domain:** Conversations — the Salon, and the threads with clients

## Status

The tab reads as the boutique's message inbox: the shop's name in the title, a
field that searches the threads, and then the threads themselves.

**No read state ships.** There is no unread badge and no unread summary, on
purpose (D2 = c): the release carries no per-user read model, so a mark that could
never clear was removed rather than left asserting `All caught up` over a field no
server sends.

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
different thing rather than as the same thing further along.

Sending is optimistic. The words appear before the API answers, marked
`Sending…`, because an associate who has just tapped send is owed an answer about
whether their words went anywhere. A refusal leaves the message on screen marked
`Failed to send` with a **Try again** that keeps the text.

The header names the client and opens their profile in the client book. What is
deliberately not on this screen: the client's grade, their history, and anything
else the client book already says better. This screen is the conversation.

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

_(It carries no unread count, and none is coming: D2 = c removed the badge rather than
leaving it unfillable.)_

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
  the tick, and the decision a staged reply is waiting on.
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
  `docs/api/openapi.yaml`, including the sign-off endpoint. It refuses to decide a
  draft it has no hash for rather than sending a blank one the API would reject
  with a 400 nobody can act on.
- `demo_thread_repository.dart` - the exchanges the inbox's previews promise, so
  opening a thread lands on the conversation that was advertised rather than on an
  empty room.

### `domain/`
- `conversation.dart` - one thread, mirroring `ConversationDto` and the richer row
  the inbox needs. Also the conversation's `status` and who spoke last.
- `thread_message.dart` - one message, mirroring `MessageDto`. It promotes a
  forwarded client message to the client's own, marks a published staff message as
  a note the client never saw, and carries the hash a sign-off is bound to.

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

- **No read state at all.** There is no unread badge, no unread summary and no
  per-user read model behind them (D2 = c). A thread cannot be marked read because
  nothing tracks it; if the client-thread plan lands a read model, the badge can
  return as its own slice consuming that aggregate.
- **The list is live; a thread is not.** The inbox opens its own hub connection and
  applies `ReceiveConversationChanged` tiles in place, re-reading the first page when
  the tile names a thread it does not hold. The Salon already proves the per-thread
  path (`ConversationRealtimeService`, `JoinSalon`, `ReceiveMessage`); a client
  thread needs the same wiring, and the thread on screen needs to take a message
  that arrives while it is open. Those are the client-thread plan's surfaces.
- **Rich blocks render as their text.** `Look`, `Piece`, `AtAGlance`, `Payment`,
  `Courier` and `SignOff` all arrive as content blocks with more than a string in
  them, and the thread draws the first text block. Each deserves its own card.
- No compose action on the inbox. Search narrows the threads that are loaded; it
  is not a server-side search.
- The inbox loads one page at a time and the footer says `Showing <loaded> of
  <total>` until every thread is in. Search still narrows only what is loaded, and
  the no-matches copy says so; a server-side `q` is the next step if that becomes
  a real complaint.
- `MessageStatus.draft` and `failed` are modelled but never produced by the
  backend, and a message stuck in `AwaitingSignOff` can only be decided from the
  thread it is in - there is no queue of everything waiting on the associate.
