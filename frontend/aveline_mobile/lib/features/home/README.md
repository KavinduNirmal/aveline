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
  five most active clients, with "See all" for the rest. The whole section is
  wrapped in `PermissionGuard(customers:view)` at the Home screen, so a role
  without that grant hides it rather than rendering a row whose only possible
  answer is `403`.
- `client_highlight_tile.dart` — the circular avatar and the value badge that
  rides its bottom edge. There is **no** activity dot: the schema has no read
  marker, so a dot could never clear (decision D2). A client with no grade wears
  no badge rather than an invented `LVL 1`.
- `client_status_ticker.dart` — the status behind the client row: one client's
  activity at a time, rotating on its own, tap to move on. Ambient, so it holds
  its frame under reduced motion.
- `blossom_usage_card.dart` — how many Blossoms are left this cycle, a meter, and
  the request action. The card wears the brand's drifting blobs, clipped to its
  own corners. It carries **loading** and **error-with-retry** states: a balance
  that could not be read says so rather than rendering `0`, which would be a
  different and false statement about the shop. The low-water note uses the
  server's `lowBalanceThresholdPercent`, not a client constant. The request
  action is UI only: it says what would happen and sends nothing, because there
  is no approval record for the owner to act on yet.
- `data/blossom_repository.dart` / `data/api_blossom_repository.dart` — the
  balance read. The API repository maps the decimal projection onto
  `BlossomUsage` (allowance = limit + granted − adjusted, `blossomRemaining`
  preferred over deriving, `periodEnd` formatted as `1 October`) and turns a
  refusal into `BlossomBalanceUnavailable` so the card can distinguish "not
  available" from "none left".
- `all_clients_sheet.dart` — every client, with the activity that put them here.
- `quick_add_client_sheet.dart` — the counter-side walk-in form.
- `more_actions_sheet.dart` — the quick-action overflow sheet. Every row that
  names a route that exists pushes it; only genuinely unbuilt slices keep the
  honest "not on mobile yet" copy.

### `domain/`, `data/`
- `domain/focus_task.dart` — one thing waiting on the user, plus the
  `FocusDomain` that selects its chip colour. Carries `dueAtUtc` (the source of
  truth for "next") and `sourceKey` (the id of the fact behind the docket, which
  a dismissal is keyed to). `timeLabel` is an optional presentational fallback;
  `displayTimeLabel` picks whichever is present. `FocusDomain.fromWire` returns
  `null` for an unknown domain so a new server value drops one item rather than
  throwing.
- `data/home_feed_repository.dart` / `data/api_home_feed_repository.dart` — the
  focus feed read and the dismissal write. The API repository posts the docket's
  `sourceKey` with a per-request `Idempotency-Key` and maps the card's verb onto
  the wire decision (`Approve` → `approve`, `Mark ready` → `markReady`, else
  `signOff`).
- `domain/client_highlight.dart` — Home's own view of a client (name, value
  tier, activity). The customers slice owns the full entity and maps into this;
  Home does not import that feature's domain layer.
- `data/home_repository.dart` — the one source Home reads: a `HomeSnapshot`
  (deck, clients, balance) and the completion write. The interface exists so the
  screen and the controller are built and tested before the endpoints land, and
  so no production path can reach a demo constant as a silent fallback.
- `data/demo_home_repository.dart` — the stand-in source `app.dart` wires until
  S3/S4/S5 replace it with the API repository.
- `presentation/home_controller.dart` — owns the screen's deck, client row and
  balance, with loading, error and optimistic-completion state. Every block on
  the page reads it, so the header count, the pile, the strip and the row move
  together.
- `data/demo_focus_tasks.dart`, `data/demo_client_highlights.dart` and
  `data/demo_blossom_usage.dart` — **test fixtures**, injected explicitly by the
  suite; production reads the repository.
- `domain/blossom_usage.dart` — the cycle's Blossom allowance and what has been
  spent against it.

## Notes
- Pull-to-refresh is wired once, in `StaffAppShell`, so every tab the shell hosts
  shares one gesture. The mark is a blossom that unfurls as the pull arms and
  turns while the work runs; `RefreshIndicator.noSpinner` owns the arming and the
  release, because hand-rolling that is easy to get subtly wrong.
- A pull re-fetches the account and the shop name, and it moves
  [`RefreshScope`]'s revision; `HomeScreen` observes that and asks the
  `HomeController` to re-read the screen. The reload is scheduled after the
  frame, because the controller notifies its listeners and notifying during a
  build is not allowed.
- A first read is a loading card and a failed read is an error card with a
  retry. Home has **no** demo fallback: a failure is shown as a failure rather
  than as a plausible screen of fiction.
- Completion is optimistic. The docket leaves the pile the moment it is signed
  off and is put back exactly where it was if the server refuses; the toast
  fires only once the server has accepted (or reports the refusal). The server
  records a **dismissal** keyed to the docket's `sourceKey` and bound to a hash
  of its content, so signing off is an act rather than a local list edit and a
  changed fact reappears instead of staying suppressed.
- `TodayStrip` takes its "next" from `dueAtUtc` before any clock label, because
  the pile cycles: the docket in hand is not necessarily the next thing due, and
  a display string is a category error at the wire level.
- Each tab's list uses `AlwaysScrollableScrollPhysics`, so the pull still arms on
  a tab whose content is shorter than the viewport.
- Swiping a docket sideways sends it to the bottom of the pile, so the deck
  cycles instead of discarding: the docket behind rises to the front and the
  work keeps its order for the next pass. Signing off is the only way a docket
  leaves.
- `HomeController` owns the deck and reloads it only when the signed-in role
  changes or the shell asks for a refresh, so clearing a docket is a real state
  change and an ordinary rebuild does not disturb the pile. The header count and
  the strip read that one list, which is why signing a docket off moves all three
  at once. `FocusTask` stays the wire shape (see S4 for `dueAtUtc`/`sourceKey`).
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
- A client tile (and a `See all` row) pushes `/customers/<id>`: the profile is a
  real route, so a toast saying otherwise would be a dead end dressed up as an
  answer.
- Every org-scoped call Home makes builds its path from
  `BoutiqueProvider.organizationId`, which comes from the **same active
  membership** as the boutique name and role (`GET /api/v1/orgs/my`). The JWT's
  `org_id` claim is deliberately not used: it can be stale, and the server's
  `OrganizationScopeAuthorizationHandler` ignores it too. A null id is a hard
  stop, not an error card.
- The `Direct client link` section is permission-gated on `customers:view`, which
  `AppRoles.staff` does not hold. It is the first production user of
  `PermissionGuard`; the honest answer for an unauthorised role is no section,
  not a section that can only `403`.
- Ambient motion on this screen — the veil's blobs, the corner blossom, the
  status rotation — holds its frame when the platform asks for reduced motion.
  Note that `AnimatedSize` asserts in its own layout at a zero duration, so the
  deck drops the wrapper entirely under reduced motion instead of shortening it.
- The client row caps at `ClientLinkSection.rowLimit` and mutates locally, so a
  walk-in added at the counter leads the row immediately. The create call goes
  through the Home repository to the tenant customer surface, and the tile
  carries the **server's** id: an id invented on the device would resolve to no
  profile. A row with no create path adds nothing rather than fabricating one.
- `data/api_customer_tenant_repository.dart` reads the shared customer surface:
  the book the log-visit picker groups into sections, the highlights the row
  shows (mapping the nullable wire level onto `ClientTier?`), and walk-in
  creation. `HomeScreen` only needs the book, so that dependency is the narrow
  `CustomerBookSource` rather than the whole customers repository.
