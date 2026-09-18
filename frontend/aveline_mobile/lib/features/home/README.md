# Feature: Home

Post-auth landing shell shown after sign-in.

## Layers

### `presentation/screens/`
- `main_shell.dart` — hosts the floating dock and swaps the active tab body
  (Home, Customers, Catalog, Profile). The center Salon launcher pushes the
  full-screen Salon route.
- `home_screen.dart` — the Home tab, composed of five blocks: the time-of-day
  greeting, the quick-action row, `Today at a glance`, today's focus deck, the
  direct client link with its status card, and the Blossom meter. The old
  Blossoms / Approvals placeholder cards are gone, and so is the practice of
  printing a number with nothing behind it: every figure on the page now comes
  from a list on the page.
- `staff_app_shell.dart` — header, drawer, and the floating Blossom launcher
  that wrap every in-shell screen.

### `presentation/widgets/`
- `quick_actions_row.dart` — the horizontally scrolling row of circular floor
  tools under the greeting (`QuickAction` carries each label, icon, and the
  honest message for slices that are not on mobile yet).
- `today_strip.dart` — `Today at a glance`: what is inside today's focus, counted
  by kind of work (clients arriving, deliveries, intake) with an icon per column,
  plus the next commitment. Every figure is counted from the deck Home already
  holds, so the card cannot disagree with the pile under it. A faded, slowly
  turning blossom sits in its corner as a watermark.
- `focus_deck.dart` — `Today's focus`: the "N left" header, the docket pile, the
  swipe-to-the-back motion (and its settle-back), and the empty state.
- `focus_task_card.dart` — one docket. Its depth in the pile is its
  `prominence`: `1` is the lightest surface on the page with a soft shadow, and
  `0` is the recessed, outlined slab standing behind it.
- `client_link_section.dart` — `Direct client link`: the walk-in slot plus the
  five most active clients, with "See all" for the rest.
- `client_highlight_tile.dart` — the circular avatar, the value badge that rides
  its bottom edge, the activity dot, and the dashed walk-in slot.
- `client_status_ticker.dart` — the status behind the client row: one client's
  activity at a time, rotating on its own, tap to move on. Ambient, so it holds
  its frame under reduced motion.
- `blossom_usage_card.dart` — how many Blossoms are left this cycle, a meter, and
  the request action. The card wears the brand's drifting blobs, clipped to its
  own corners. The action is UI only: it says what would happen and sends
  nothing, because there is no approval record for the owner to act on yet.
- `all_clients_sheet.dart` — every client, with the activity that put them here.
- `quick_add_client_sheet.dart` — the counter-side walk-in form.
- `more_actions_sheet.dart` — the quick-action overflow sheet.

### `domain/`, `data/`
- `domain/focus_task.dart` — one thing waiting on the user, plus the
  `FocusDomain` that selects its chip colour.
- `domain/client_highlight.dart` — Home's own view of a client (name, value
  tier, activity). The customers slice owns the full entity and maps into this;
  Home does not import that feature's domain layer.
- `data/demo_focus_tasks.dart` — the role-aware demo deck (eight dockets a side),
  to be replaced by the API.
- `data/demo_client_highlights.dart` — the demo clients, more than the row
  shows so "See all" has something behind it.
- `domain/blossom_usage.dart` / `data/demo_blossom_usage.dart` — the cycle's
  Blossom allowance and what has been spent against it.

## Notes
- Pull-to-refresh is wired once, in `StaffAppShell`, so every tab the shell hosts
  shares one gesture. The mark is a blossom that unfurls as the pull arms and
  turns while the work runs; `RefreshIndicator.noSpinner` owns the arming and the
  release, because hand-rolling that is easy to get subtly wrong.
- A pull re-fetches the account, which is the only in-shell state the API owns.
  This screen's data is demo data, so "reload" means "seed again": `HomeScreen`
  and `ClientLinkSection` compare [`RefreshScope`]'s revision during build and
  re-seed when it moves. An inherited widget has no `didUpdateWidget` counterpart,
  which is why that comparison sits in `build`.
- Each tab's list uses `AlwaysScrollableScrollPhysics`, so the pull still arms on
  a tab whose content is shorter than the viewport.
- Swiping a docket sideways sends it to the bottom of the pile, so the deck
  cycles instead of discarding: the docket behind rises to the front and the
  work keeps its order for the next pass. Signing off is the only way a docket
  leaves.
- Home owns the deck (`_HomeScreenState._tasks`) and reseeds it only when the
  signed-in role or the caller's list changes, so clearing a docket is a real
  state change and an ordinary rebuild does not disturb the pile. The header
  count and the strip read that one list, which is why signing a docket off moves
  all three at once. Swapping in the API means replacing `demoFocusTasks` with a
  notifier and keeping `FocusTask` as the wire shape.
- `FocusDeck` keeps its own ordering of the list but not its membership: a
  docket leaves the pile when the caller drops it, which is what `onComplete` is
  for. A caller that ignores the callback will see the docket return at the
  bottom.
- The count lives in the section header, not on the card. The pile cycles, so a
  position (`1 of 4`) claimed a progress that never advanced; "N left" is a fact
  about the list. It is dropped rather than reading "0 left" beside an empty
  state that already says the same thing.
- The layer behind the docket in hand is a shape, never text: only the top
  `FocusDeck._lift` band of it is visible, and that band falls inside the card's
  own padding. Showing the next docket's title would need a ~60dp peek, which
  would turn the pile into a cascade.
- The card hugs its content rather than a fixed height, and the deck animates
  between heights. That is what lets the card grow with the platform text scale
  instead of squeezing the supporting line out.
- Only the docket in hand is hit-testable and in the semantics tree; the layers
  behind it are inert.
- Both horizontal rows wrap in the shared `TrailingFade`, which dims the
  trailing edge until the row is scrolled to its end. It keeps one widget shape
  and only changes its gradient: swapping the wrapper in and out would re-inflate
  the scroll view and throw the row back to offset zero.
- The fade band dims to a floor rather than erasing, because the item peeking
  under it is the row's only "there is more" affordance. Rows take their trailing
  padding from `TrailingFade.endPadding`, so the last item can still be scrolled
  clear of the band.
- Every size on this screen comes from the theme's text scale. The greeting and
  the strip's two figures are the only places that spend `displayLarge`, per
  `DESIGN.md`, which gives it to greetings and key numbers. Everything else on
  the card and the tiles is a text role, never a local `fontSize`.
- The client row is a glance at who is on the line; the sentence behind those
  activity dots lives in `ClientStatusTicker` underneath it, one client at a
  time, because a line under a 76dp avatar is cut to two words.
- Ambient motion on this screen — the veil's blobs, the corner blossom, the
  status rotation — holds its frame when the platform asks for reduced motion.
  Note that `AnimatedSize` asserts in its own layout at a zero duration, so the
  deck drops the wrapper entirely under reduced motion instead of shortening it.
- The client row caps at `ClientLinkSection.rowLimit` and mutates locally, so a
  walk-in added at the counter leads the row immediately. The create call belongs
  to the customer concierge API.
