# Feature: Salon (Conversations)

> **Domain:** Conversations — the concierge thread

The full-screen Salon where staff and Aveline's agents (Aveline, Ava, Elle, Lina)
participate in one persistent thread. It is opened from the dock's centre launcher,
and the dock is hidden while it is open.

## Status

The thread is live: `GET/POST …/conversations/{id}/messages` carries the history and
the staff's notes, `/hubs/conversations` streams agent states and replies, and a
message is rendered from its **content blocks** rather than from a string.

**The Salon is a block renderer, not a text field.** An agent's answer is a list of
typed blocks — `text`, `piece`, `look`, `client_message`, `suggestion`,
`at_a_glance`, `sign_off`, `payment`, `courier`, `choice`, `attachment` — and each
one is drawn as what it is. That is what lets a curated set read as a set, a
customer's WhatsApp message read as the message it arrived as, and a staff note's
`@name` read as the entity the resolver looked up.

This is a port of the web's `components/conversation/blocks.tsx`, `tileBlocks.ts`,
`Mentions.tsx` and `lib/mentions.ts`. The web is the source of truth for the design;
the deviations that survive the port are listed under [Deviations](#deviations) and
each one says why.

## Product tiles

A recommendation set is a row, not a column. Elle emits its `piece` blocks back to
back and its `look` blocks after them, and a one-block-per-line list stacked a
curated set into a single visible card that read as the whole answer. So a run of
consecutive tiles is laid out as one row, sized the way the web's `auto-fit` grid
sizes it: as many `11rem` tracks as fit the bubble, collapsing empty tracks so a
short run fills the width.

Three rules decide what is a tile at all:

- **A piece always is.** Photograph or not.
- **A look is one only while it has a photograph of its own.** Without one it is
  Elle's styling note about the row — prose, not a card — and it is read at the
  row's full width. It is never given a blank plate to fill.
- **A borrowed photograph is dropped.** Elle's composer used to hand a look the
  first matched piece's photo as its own, so the Salon showed one saree twice. New
  answers borrow nothing, but every message already stored carries the copy, so it
  is normalised away on the way to the renderer. The piece keeps the photograph; the
  look becomes the note it always was.

The bubble's own width is part of the rule. A message that carries a row of tiles
takes the bubble's definite `78%` width, because a row needs a definite width to
count its columns against — under a shrink-to-fit bubble the row resolves to one
column and the pieces stack again. A lone tile is left shrink-to-fit beside its own
`16rem` cap, so a single piece is a card rather than a band.

The grouping, the normalisation and the row test live in `domain/tile_blocks.dart`
and are pure, so they are pinned without pumping a widget.

## Entity mentions

`@Samantha Arias` and `#0771234567` are how staff point the resolver at an exact
customer instead of letting it guess from prose. Written as plain text they read as
stray punctuation, so a mention is lifted into a pill covering exactly the span the
resolver read — the marker plus the captured name or number, and not the prose a
greedy capture dropped and then trimmed back off.

`domain/mentions.dart` is a mirror of the resolver's own parser
(`agent-service/app/customer_resolution/mentions.py`), and it has to stay one: the
pill claims "this is the entity the lookup read", so a span wider or narrower than
the resolver's would be a lie about what was resolved. The word list is copied from
that module, and the cases in `mentions_test.dart` are the web's
`lib/mentions.test.ts` cases, so a change to either parser fails on both platforms.

Two asymmetries are deliberate and are the resolver's own rather than tidied up:
a `#` is escaped by a single preceding backslash (`(?<!\\)#`), while a `@` is
escaped by an odd run of them (`_is_escaped`).

A pill is only drawn on the staff's own words. A customer's at-sign is a handle they
typed, not an entity any lookup read, so a `client_message` body is deliberately
plain text.

## The customer's channel

An inbound customer message is relayed from WhatsApp into the thread as a
`client_message` block, and it is drawn as the message it arrived as: the WhatsApp
mark and the handle name the channel, and the words sit in an inbound chat bubble on
the channel's own canvas.

The card paints its own surface and states its own ink, whatever bubble the thread
placed it in. A tint of the associate's bubble ink would read as Aveline talking,
and the whole point of the block is that she is not.

The channel is named in ink, not in the brand green: white on the brand green fails
contrast at label size, and the mark beside it is already the brand. The tokens
(`whatsapp`, `whatsapp-foreground`, `whatsapp-deep`, `whatsapp-canvas`) mirror the
`--whatsapp*` custom properties in the web's `src/index.css`, both themes, and live
in `core/theme/channel_colors.dart`. The app is light-only today; the dark set is
carried so the Salon is already correct on the day that changes.

The handle is spaced the way the rest of the app spaces numbers — `94763475058`
becomes `+94 76 34 75 058` — but only for a Sri Lankan number. Forcing the local
country code onto a foreign one would invent an address the customer does not have.

## Layers

### `domain/`

- `salon_message.dart` — the wire `MessageDto` as the Salon draws it: author kind and
  agent key, the derived `text`, and the ordered `blocks` the renderer walks. A
  message this device composed carries no blocks, so `contentBlocks` shapes the one
  it would have arrived as; that keeps a single rendering path rather than two that
  can drift.

The block vocabulary itself is the conversation domain's, not the Salon's:
`mentions.dart` (the resolver's grammar) and `tile_blocks.dart` (which blocks are
tiles and when a run is a row) live in `features/conversations/domain`, because the
thread with a client renders the same blocks and neither feature may own the other's
copy of them.

### `data/`

- `conversation_api.dart` — gets-or-creates the Aveline Salon, reads its history,
  sends a note with its attachments, resolves a `choice`, and reads one attachment's
  bytes through the authenticated client.

### `presentation/screens/`

- `salon_screen.dart` — the full-screen Salon: app bar, thread, composer. Staff notes
  are optimistic, and Aveline's live reasoning is an activity bubble until her reply
  lands.

### `presentation/widgets/`

- `message_bubble.dart` — one message: persona attribution, the bubble's surface and
  ink, the definite width a tile row needs, the customer's channel surface, and the
  footer.
- `salon_composer.dart` — the message input, the attachment tray, and the send control.

The renderer is shared with the thread with a client, and lives with the vocabulary it
draws in `features/conversations/presentation/widgets`: `message_blocks.dart` (the
block renderer, the tile grid, the piece and look tiles), `client_channel_surface.dart`
(the channel message), `mention_text.dart` (a paragraph with its mentions lifted into
pills), `bubble_tone.dart` (which bubble a block sits in, which fixes its surface and
ink), and `whatsapp_mark.dart` (the official glyph, drawn from `simple-icons`' own path
so the app shows the mark the web shows rather than an imitation of it). Persona
metadata moved to `lib/shared/persona.dart`, because both threads credit agents.

## Shared with this slice

Every block here is the same wire block a client thread carries, so the two threads
render them with one renderer rather than two that can drift: the tile grid, the
mentions, the channel surface, the attachment card, and the persona accents are all
`features/conversations`'. The Salon contributes what is its own — the activity
bubble, the composer, the streaming typewriter — and nothing else.

## Deviations

Each of these is a place where the Flutter rendering cannot be the web's byte for
byte, or where it deliberately is not:

- **A mention pill does not break across lines.** The web styles its pill with
  `box-decoration-clone`, so a mention longer than a line gets a rounded plate on
  each line fragment. Flutter cannot paint a rounded, padded background per line
  fragment, so the pill is an atomic inline box. On a phone the bubble is narrow
  enough that only an unusually long greedy capture could reach it, and the two
  alternatives — a rounded box that cannot break, or a square-cornered tint that
  can — are both worse lies about the entity than a pill that wraps whole.
- **The hairline between block groups tracks its group, not the bubble.** Web puts a
  full-width separator between groups. A Flutter `Divider` expands to whatever
  constraint it is handed, which would force every multi-block bubble to its full
  cap, so the rule is carried by each group's own top border. The spacing is the
  web's: an 8px stack gap, then 8px, a hairline, and 8px.
- **A customer's channel message is the message surface, not a card in the bubble.**
  Web wraps its channel figure in the thread's bubble. On a phone that is two frames
  for one message, and it sets the customer's words a level deeper than everyone
  else's — the opposite of what naming the channel is for. So the surface substitutes
  for the bubble: it states its own ink, and anything else the message carries rides
  on its canvas.
- **A tile is a definite 4:3 box, not an `AspectRatio`.** The row that gives every
  card one height is measured by `IntrinsicHeight`, and `Image` reports no intrinsic
  size; an `AspectRatio` plate would let the row collapse to its text. The grid
  resolves the track width and hands it down, so the plate measures itself.
- **An attachment's bytes are fetched from the route built out of the ids**, not from
  the block's stored `url` as the web does. That is what
  `ApiThreadRepository.fetchAttachmentBytes` already does, so both threads hit one
  endpoint rather than two spellings of it.
- **A `sign_off` block draws no buttons in the Salon.** The Salon hands no decision
  callback, so the block reads as the approval it is waiting on rather than offering
  an inert control. Wiring the decision is the order/approval flow's job
  (ADR-024), not this port's.

## The type scale

Chat surfaces read at 14px (`text-sm`), which is the web's size and the size a
transcript wants: the app's 16px body belongs to forms and panels, where a screen
holds a handful of labelled fields rather than a conversation. Names and overlines
stay at 11px and 10px, and the composer's field is a rounded rectangle rather than the
app's pill — a fully rounded box at that width reads as a button rather than as
somewhere to type.

## Known gaps

- **The Salon does not draw read state.** There is no unread marker on the thread, as
  elsewhere in the app.
- **A tile row needs 368px to hold two cards.** The web's track minimum is `11rem`,
  and a phone-width bubble is around 256px, so on a phone the row resolves to one
  column and the pieces stack — which is what the web's own `auto-fit` does at that
  width, but it does mean the row is mostly a wide-thread affordance today. Reducing
  the track minimum for narrow bubbles is a design call, not a port decision, so it
  is left as it is and raised rather than changed.

## Related

- `docs/architecture/inbox.md` — the block vocabulary and the message pipeline.
- `docs/frontend/` — the web components this is a port of.
- `features/conversations/README.md` — the threads with clients, which share the
  block vocabulary and the attachment card.
