# Feature: Home

Post-auth landing shell shown after sign-in.

## Layers

### `presentation/screens/`
- `main_shell.dart` — hosts the floating dock and swaps the active tab body
  (Home, Customers, Catalog, Profile). The center Salon launcher pushes the
  full-screen Salon route.
- `home_screen.dart` — the Home tab, composed of three blocks: the time-of-day
  greeting, the quick-action row, and today's focus deck. The old Blossoms /
  Approvals placeholder cards are gone.
- `staff_app_shell.dart` — header, drawer, and the floating Blossom launcher
  that wrap every in-shell screen.

### `presentation/widgets/`
- `quick_actions_row.dart` — the horizontally scrolling row of circular floor
  tools under the greeting (`QuickAction` carries each label, icon, and the
  honest message for slices that are not on mobile yet).
- `focus_deck.dart` — `Today's focus`: the docket pile, the swipe-to-the-back
  motion (and its settle-back), the local completion handling, and the empty
  state.
- `focus_task_card.dart` — one docket. Its depth in the pile is its
  `prominence`: `1` in hand, `0` a plain layer behind.
- `client_link_section.dart` — `Direct client link`: the walk-in slot plus the
  five most active clients, with "See all" for the rest.
- `client_highlight_tile.dart` — the circular avatar, the value badge that rides
  its bottom edge, the activity dot, and the dashed walk-in slot.
- `all_clients_sheet.dart` — every client, with the activity that put them here.
- `quick_add_client_sheet.dart` — the counter-side walk-in form.
- `more_actions_sheet.dart` — the quick-action overflow sheet.

### `domain/`, `data/`
- `domain/focus_task.dart` — one thing waiting on the user, plus the
  `FocusDomain` that selects its chip colour.
- `domain/client_highlight.dart` — Home's own view of a client (name, value
  tier, activity). The customers slice owns the full entity and maps into this;
  Home does not import that feature's domain layer.
- `data/demo_focus_tasks.dart` — the role-aware demo deck, to be replaced by
  the API.
- `data/demo_client_highlights.dart` — the demo clients, more than the row
  shows so "See all" has something behind it.

## Notes
- Swiping a docket sideways sends it to the bottom of the pile, so the deck
  cycles instead of discarding: the docket behind rises to the front and the
  work keeps its order for the next pass. Signing off is the only way a docket
  leaves.
- The focus deck is seeded once and mutated locally, so clearing a docket is a
  real state change on this screen. Swapping in the API means replacing
  `demoFocusTasks` with a notifier and keeping `FocusTask` as the wire shape.
- Dockets share one height, so the card clamps its own text scaling to 1.2.
- Only the docket in hand is hit-testable and in the semantics tree; the layers
  behind it are inert.
- Both horizontal rows wrap in the shared `TrailingFade`, which masks the
  trailing edge until the row is scrolled to its end. It keeps one widget shape
  and only changes its gradient: swapping the wrapper in and out would re-inflate
  the scroll view and throw the row back to offset zero.
- The client row caps at `ClientLinkSection.rowLimit` and mutates locally, so a
  walk-in added at the counter leads the row immediately. The create call belongs
  to the customer concierge API.
