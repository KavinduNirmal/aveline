# Flutter Salon UI — port of the updated web Salon

> **Branch:** `feature/flutter-salon-ui-update`
> **Web source of truth:** `0460db1 feat(salon): tiles, mentions and the WhatsApp channel marks`,
> squash-merged into `development` as `6c2f404` (PR #392). The web files were diffed
> against that commit and are byte-identical to it.

## What was ported

The web Salon gained three rendering rules. The Flutter Salon was a generation
behind them — in fact it had no block renderer at all: `SalonMessage.fromJson`
read the first `text` block and a `choice` block and silently dropped everything
else, so a `piece`, a `look` or a forwarded customer message never reached the
screen. Porting the three rules therefore meant giving the Salon the block
pipeline they render through.

### 1. Product tiles

A run of consecutive `piece` / `look` blocks is laid out as one row rather than
stacked, so a curated set reads as a set.

- `lib/features/salon/domain/tile_blocks.dart` — `isTileBlock`,
  `withoutBorrowedLookImages`, `hasTileRow`, `groupTileBlocks`. A piece is always a
  tile; a look only while it carries its own photograph; a look's borrowed
  photograph (the first matched piece's, copied by an older composer) is dropped so
  one saree is not shown twice.
- `lib/features/salon/presentation/widgets/salon_blocks.dart` — `_TileGrid` counts
  columns the way the web's `auto-fit` does (`11rem` tracks, empty tracks
  collapsed, a lone tile capped at `16rem`), `_PieceTile`, `_LookBlock`.
- `message_bubble.dart` gives a message that carries a tile row the bubble's
  definite `78%` width, which is what the row counts its columns against.

### 2. Entity mentions

`@Samantha Arias` / `#0771234567` are lifted into pills covering exactly the span
the resolver read (ADR-019).

- `lib/features/salon/domain/mentions.dart` — a mirror of the resolver's grammar
  (`agnet-service/app/customer_resolution/mentions.py`), case-for-case with the
  web's `lib/mentions.ts`, including the resolver's own `#` (single backslash) vs
  `@` (odd run) escaping asymmetry.
- `lib/features/salon/presentation/widgets/mention_text.dart` — the paragraph, with
  the pill tinted for the bubble it sits in, and phone mentions set in tabular
  figures. Drawn on `text` and `suggestion` blocks; deliberately **not** on a
  customer's own words.

### 3. The WhatsApp channel marks

An inbound `client_message` is drawn as the message it arrived as.

- `lib/core/theme/channel_colors.dart` — the `--whatsapp*` tokens, both themes.
- `lib/features/salon/presentation/widgets/whatsapp_mark.dart` — the official
  `simple-icons` WhatsApp path.
- `ClientMessageCard` in `salon_blocks.dart` — the mark in its brand-green badge,
  the handle, the "WhatsApp" pill in accessible deep ink, and the customer's words
  in an inbound bubble on the channel canvas. The card states its own surface and
  ink whatever bubble holds it.
- `lib/shared/utils/phone_formatter.dart` — `formatLkPhone`, mirroring the web's
  `lib/boutique.ts`, so `94763475058` reads `+94 76 34 75 058` and a non-Sri Lankan
  handle is left exactly as the channel gave it.

### Supporting work

- `SalonMessage` now carries the message's `blocks`; `contentBlocks` shapes the block
  a locally-composed message would have arrived as, so there is one rendering path
  rather than two that can drift. `streamsAsText` mirrors the web's
  `shouldStreamContent`: only a purely textual reply types out, because streaming
  collapses content to the first `text` block and would hide a rich answer's cards.
- `ConversationApi.fetchAttachmentBytes` plus the now-public
  `ThreadAttachmentCard`, so a Salon attachment and a client-thread attachment —
  the same wire block — are drawn by the same card instead of two that could drift.
- The block vocabulary the Salon never drew before (`at_a_glance`, `sign_off`,
  `payment`, `courier`, `choice`, `attachment`, and unknown types) now renders,
  the unknown case falling back to a one-line summary rather than to an empty bubble.

## Tests

| Suite | Covers |
| --- | --- |
| `test/features/salon/mentions_test.dart` | The resolver grammar — the web's `lib/mentions.test.ts` cases |
| `test/features/salon/tile_blocks_test.dart` | Tile classification, borrowed-photograph normalisation, row grouping |
| `test/features/salon/salon_blocks_test.dart` | 28 widget tests: the row, the lone-tile cap, the look note, the channel card, pills and their tints, and the remaining block vocabulary |
| `test/features/salon/message_bubble_test.dart` | The tile row's definite bubble width, shrink-to-fit cases, streaming vs blocks |
| `test/features/salon/salon_screen_test.dart` | A note typed into the real screen reaching the block renderer end to end |
| `test/features/salon/conversation_api_test.dart` | Attachment bytes through the authenticated route, and the cache |
| `test/shared/utils/phone_formatter_test.dart` | LK phone formatting |

`flutter analyze` is clean and the full suite is green (1114 tests).

### A pre-existing test that was passing vacuously

`salon_screen_test.dart › composer appends a staff note to the thread` asserted
`find.text('Please draft a note for Mrs. Perera.')` after tapping send without a
frame in between. The send control enables from the field's `onChanged`, so the tap
landed on a disabled button, the message was never appended, and the assertion
passed off the text still sitting in the `TextField`. The test now pumps the frame
that carries the typed text and asserts the thread actually grew. No production
behaviour changed; the `onChanged` rebuild was already correct.

## Deviations from the web

Fuller prose lives in `frontend/aveline_mobile/lib/features/salon/README.md`. In
short:

1. **A mention pill does not break across lines.** Flutter cannot paint a rounded,
   padded background per line fragment the way `box-decoration-clone` does.
2. **The hairline between block groups tracks its group, not the bubble.** A Flutter
   `Divider` expands to its constraint, which would force every multi-block bubble to
   its full cap.
3. **A customer's channel message is the message surface, not a card in the bubble.**
   Web nests its channel figure inside the thread's bubble; on a phone that is two
   frames for one message and sets the customer's words a level deeper than everyone
   else's. The surface substitutes for the bubble instead.
4. **A tile plate is a definite 4:3 box, not an `AspectRatio`.** The equal-height row
   is measured by `IntrinsicHeight` and `Image` reports no intrinsic size, so the grid
   resolves the track width and hands it down.
5. **An attachment's bytes come from the route built out of the ids**, not the block's
   stored `url`, matching what `ApiThreadRepository` already does.
6. **A `sign_off` block draws no buttons in the Salon**, because the Salon hands no
   decision callback; wiring an inert control would be worse than none.

## Design pass (after review on device)

Running it on the hardware surfaced three things the port had not addressed, and a
second pass fixed them:

- **The client thread had its own, plainer bubble.** It rendered most blocks as
  one-line summary chips — a `piece` read `Emerald Green Georgette Saree · Rs 75,000`
  in a grey pill — and it drew a relayed customer message as a plain bubble with the
  raw `94763475058` above it. It now renders the same blocks with the same renderer
  the Salon uses: a piece is the 4:3 tile, a look is its photograph or its styling
  note, a table is a table, and a customer's message is the channel surface. This is
  what "like the frontend" meant, and it is why the renderer moved into
  `features/conversations` — the two threads share the vocabulary, so neither may own
  a copy of it.
- **The type was too large for a chat surface.** Transcript prose is now the web's
  14px (`text-sm`); names and overlines stay at 11px and 10px. The app's 16px body
  stays for the forms and panels it was chosen for.
- **The composer was a pill.** It inherited the app's fully rounded form field, which
  at a composer's width reads as a button. Chat fields now share a rounded rectangle
  (`lib/shared/widgets/chat_input.dart`) with the padding a single line needs.

Each persona also keeps its own accent on the client thread now (Elle's gold, Ava's
rose, Lina's wine) rather than every agent's name wearing the same wine, which had
made a three-agent thread read as one voice.

Two test updates were consequences of the pass rather than regressions: `opens at the
newest word` needed a viewport the compact transcript actually overflows, and the
channel-handle assertion now expects the formatted `+94 77 12 34 567` the surface
draws instead of the raw channel string.

### Bugs the device run found

Running it surfaced four defects, three of them in the live-activity lifecycle and none
of them reachable from the existing tests:

1. **A reply that arrived before the send's own response left a spinner up for good.**
   Logged order on the device, every time:
   `SEND start -> REPLY arrived -> SEND response received`. The hub publishes the agent's
   answer the moment the workflow answers, while `sendMessage`'s HTTP response is still in
   flight, so `_send`'s post-response block opened a "Thinking…" bubble for a run that had
   *already answered* — and the only thing that closes a bubble is a reply that had already
   been consumed. Fixed with a run-generation counter: the response opens the bubble only
   when no reply has landed since the send.
2. **A replayed agent message skipped the run's bookkeeping.** The hub publishes to more
   than one group the Salon belongs to, and a reconnect can replay a message it already
   holds. The duplicate guard returned before clearing the activity, so a replayed reply
   left the bubble spinning. A replay is still not appended twice, but it now settles the
   run either way.
3. **A run that paused for a decision never ended its activity.** The agent service
   publishes `waiting` as the *terminal* state of a run that stopped for owner approval
   (ADR-024) — `agents.py` sends it in place of `success` when the status is
   `PausedForApproval`. The app only treated `success`/`error`/`response` as terminal, so a
   paused run left the spinner up for as long as the approval was outstanding. `waiting`
   now ends the run's activity while still being non-terminal for the header, which must go
   on reading "Awaiting your decision…". Pinned by `AgentState.endsRun` tests.
4. **A 60-second backstop** collapses an activity bubble that neither a state nor a reply
   ever confirms, because the bubble is opened optimistically and depends on a later event
   that a dropped socket can take away.

Two smaller rendering fixes came out of the same pass: the Aveline avatar and the activity
bubble both drew `Blossom` without a colour, and `Blossom` falls back to the ambient icon
colour and then to black — so both rendered as black blobs rather than the brand mark.

### Sign-off decisions

`sign_off` blocks now carry Approve/Reject in the Salon. `ConversationApi.decideSignOff`
posts to the existing `…/messages/{id}/sign-off` route with the content hash the approver
was shown, so the whole message travels with the callback rather than just the boolean.
The decision is offered only while the message is actually `AwaitingSignOff` — the API
refuses a second decision — and a second tap while the first is in flight is ignored.

### Tile track

The row's track minimum was the web's `11rem`, which needs 360px for two cards; a phone's
bubble is around 256px, so a curated set collapsed to a single column — the stacking the
row exists to prevent. The track now shrinks to whatever fits two across, never below
`6rem`, and stays `11rem` wherever `11rem` genuinely fits, so a wide thread still lays out
exactly as the web does. Verified on the device: two pieces side by side with their prices
on one line.

## Dependency change

`flutter_svg` was promoted from a transitive dependency (it is already in the tree
through `clerk_flutter`, and already resolved in `pubspec.lock` at 2.3.0) to a direct
one. It draws the official WhatsApp mark from `simple-icons`' own path. The
alternative — hand-drawing the glyph — would have been an imitation of a third-party
brand mark, and no new package was fetched for it. Per rule 19, this is the only
dependency change and no unrelated upgrades were made.

## Verification

- `flutter analyze` — clean.
- `flutter test` — 1114 passing.
- Rendered on a real Android device (SM A055F, Android 15) to confirm the tile row,
  the look note, the mention pills and the channel card lay out as intended.

## Open questions for review

1. **The Salon screen has no seam for its realtime path.** Three of the four defects the
   device run found live in the activity lifecycle, which can only be exercised with a
   connected API and a live hub: the screen reads `UserProvider`/`Dio`/`AppConfig` and
   builds its own `ConversationRealtimeService`, so `salon_screen_test.dart` can only ever
   reach the disconnected paths. `client_thread_screen.dart` already takes an injectable
   realtime service; giving the Salon the same (plus the conversation ids) would let the
   send/reply ordering and the backstop be pinned by tests instead of by a device run.
   That is a Salon-level refactor rather than part of this port, so it is raised rather
   than done.
2. **`integration_test` is deferred**, as agreed. The app has no harness and no device
   runner in CI, and the repo's idiom for flow-level coverage is the screen widget test.
3. **A tile still carries its own border inside an agent bubble's border.** The web nests
   the same way, so this is faithful; on a phone it reads as a double frame. Flattening the
   bubble when it holds tiles is a design change, not a port fix.
4. **The `suggestion` block's DRAFT card is nested the same way**, and there it is a
   deliberate mobile affordance (the associate copies the draft out of it) rather than a
   port of anything on the web.
