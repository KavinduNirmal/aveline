# Feature: Messages

> **Domain:** Conversations — the Salon, and the threads with clients

## Status

The tab reads as the boutique's message inbox: the shop's name in the title, the
unread count under it, a field that searches the threads, and then the threads
themselves.

**The Salon is pinned at the top, always.** Every other thread is with a person.
The concierge is the one channel that is always there and always answers, so it is
drawn as a card rather than as one more row - a blossom, a tinted fill and a
`YOUR CONCIERGE` overline - and it keeps that position while a search is in force.
A search is about finding a person; sliding the concierge out from under the thumb
to do it would be a surprise.

Under it come the client threads, newest word first, then the notices. Each row is
laid out the way a phone's message inbox is: the face, the client's name, the last
word, and when it landed. Read rows recede; an unread row wears a count at the foot
of its trailing edge, so the column can be scanned for what still needs an answer
without reading a word of it.

Three details are worth pointing at:

- **The last word says who said it.** `You: The blouse is pinned and ready` is not
  the client reporting progress, and a preview that read as though it were would be
  misread every time. Staff and agent messages are prefixed; a client's own message
  is not.
- **A thread waiting on a signature wears a marker.** `AwaitingSignOff` is the one
  status where the thread is waiting on the associate rather than the other way
  round, so it earns a mark an unread count cannot give it.
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
There is no thread per client yet, and `ConversationDto` has only `id`, `kind`,
`customerId`, `threadId`, `status` and `lastMessageAt` - so a row drawn from the
API has no client name, no preview and no unread count.

`Conversation.fromJson` treats those three as optional rather than inventing them,
which is why the inbox can be designed against the demo repository today and
repointed at `ApiConversationRepository` without changing the screen. Two things
have to land on the server before that repoint is worth making:

1. A kind for a client thread, or enough of one for `Conversation.fromJson`'s
   classification rule to catch it. The rule already reads intent rather than
   copying the string: a `Salon` is Aveline's, anything bound to a client is a
   client's, and everything else is addressed to the shop - so it keeps working
   when the kind is added.
2. A name, a preview and an unread count on the list row. Without them the inbox
   is a column of "Client" with no last word.

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
  see, deliberately unordered.
- `api_conversation_repository.dart` - the inbox contract over `Dio`, with the
  three fields the endpoint does not carry yet documented at the top.
- `demo_conversation_repository.dart` - the Salon, six client threads and a notice,
  with read and unread rows, a thread awaiting a signature, and ages measured from
  an injectable clock.
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
- `shared/widgets/count_badge.dart` - the one count mark, worn by both the header's
  notification badge and this inbox's unread counts.

## Related

- The Salon itself: `lib/features/salon/`, and the dock's centre launcher in
  `shared/widgets/animated_blossom.dart`.
- Contract: `docs/api/openapi.yaml`, the `Conversations` tag.
- Backend: `Aveline.Api/Modules/Conversations/`.

## Known gaps

- **Opening a thread does not mark it read.** The inbox row keeps its unread count
  and the controller that owns it is a different object from the thread's, so this
  needs a seam between them rather than a call from one to the other.
- **No live messages.** The Salon already proves the realtime path
  (`ConversationRealtimeService`, `JoinSalon`, `ReceiveMessage`); a client thread
  needs the same wiring, and the thread on screen needs to take a message that
  arrives while it is open.
- **Rich blocks render as their text.** `Look`, `Piece`, `AtAGlance`, `Payment`,
  `Courier` and `SignOff` all arrive as content blocks with more than a string in
  them, and the thread draws the first text block. Each deserves its own card.
- No compose action on the inbox, and no search across threads.
- The inbox reads one page; `ApiConversationRepository` asks for up to fifty.
- `MessageStatus.draft` and `failed` are modelled but never produced by the
  backend, and a message stuck in `AwaitingSignOff` can only be decided from the
  thread it is in - there is no queue of everything waiting on the associate.
