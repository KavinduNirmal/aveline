# Feature: Customers (Slice 1)

> **Owner:** Student 1  
> **Domain:** Customer Concierge & Memory

## Status

The screen reads as the shop's own contact list: the boutique's name in the
title, a search field scoped to this screen rather than the header's global
search, the boutique's client levels as a row of pills, and then the book — a
circle, the name (or the nickname the floor uses), the id the shop keys them by,
and their grade, with an alphabet strip down the trailing edge that jumps to a
letter.

The clients are a demo book (`demo_customer_repository.dart`) standing in for the
concierge lookup, which is not wired yet. The floating **New customer** action is
honest about that: it points at Home's walk-in slot, which is the path that works
today, rather than opening a form that does not exist.

`customers_screen.dart` is reachable from the dock's side panel and from Home's
quick actions; both route to `AppRoutes.customers`.

## Layers

### `presentation/screens/`
- `customers_screen.dart` — the Customers dock tab. The title and the search
  field come from `shared/widgets` (`BrandSectionTitle`, `SectionSearchField`),
  so the header cannot drift from the Catalog's. The level row narrows by one
  level at a time, because a client holds exactly one grade; tapping the chosen
  level clears it.
  The book is a `CustomScrollView` whose header block is measured once and whose
  sections follow, one `SliverMainAxisGroup` per letter. The group is load
  bearing: sibling `SliverPersistentHeader`s in a plain sliver list accumulate
  their `overlap` and every letter already scrolled past stays on screen, stacked
  down over the rows. Inside a group, a letter pins to the top and is pushed off
  by the next one. The floating action sits in the same `Stack`, below the index
  rather than over it.

### `presentation/`
- `customers_controller.dart` — owns the loaded book, the in-flight guard, the
  error state and the stale-reply guard that drops a book for a narrowing the
  associate has moved on from.
- `customer_section_offsets.dart` — where each letter starts, computed rather
  than measured: the rows and the headers are both fixed heights, which is what
  lets the index jump to a letter that was never laid out.

### `presentation/widgets/`
- `customer_tile.dart` — one client, in a fixed-height row.
- `customer_avatar.dart` — the circle: initials on a tint picked from the name,
  ringed in wine for a VIP. The API carries no photograph.
- `customer_level_badge.dart` — the grade, tinted rather than filled so a long
  book does not become a wall of colour, plus the level colour ramp.
- `customer_level_row.dart` — the levels as a horizontally scrolling row, full
  bleed with a fading trailing edge.
- `customer_section_header.dart` — the letter, and the pinned-header delegate.
- `customer_alphabet_index.dart` — the strip: tap a letter, or drag along it to
  scan. Letters the book has nobody under are not offered.

### `data/`
- `customer_repository.dart` — the book contract. One call returns the whole
  narrowing rather than a page, because an alphabet index promises every letter
  is reachable.
- `demo_customer_repository.dart` — a boutique's worth of clients, sectioned and
  narrowed in memory, with each client's status derived by
  `CustomerLoyaltyService`'s rule.

### `domain/`
- `customer.dart` — one client, mirroring `CustomerProfileDto`. `nickname` and
  `level` are promoted from preferences/nowhere yet: the DTO carries neither.
- `customer_book.dart` — the book and its sections.
- `customer_level.dart` — the boutique's ladder (VIP, LVL 3, LVL 2, LVL 1,
  strongest first). A client's *level* is the shop's grade; their *status*
  (`new` / `returning` / `vip` / `dormant`, see `CustomerLoyaltyService`) is the
  API's lifecycle. The two are independent on purpose, and the demo book has
  clients whose grade and status disagree.

## data/

**Include:**
- `customer_api_client.dart` — `Dio`/`http` calls to `/api/customers` and `/api/customer-interactions`
- `customer_memory_api_client.dart` — calls to `/api/customer-memory`
- `customer_repository_impl.dart` — implements `CustomerRepository` interface from `domain/`
- `customer_dto.dart` — JSON-serializable models (use `json_serializable` or `freezed`)

## domain/

**Include:**
- `customer.dart` — The core `Customer` entity (pure Dart, no Flutter dependencies)
- `customer_repository.dart` — Abstract interface defining repository contract
- `get_customer_use_case.dart`, `save_customer_memory_use_case.dart` etc.

## presentation/

**Include:**
- `screens/` — Full-page screens (`CustomerListScreen`, `CustomerDetailScreen`)
- `widgets/` — Feature-specific widgets (`CustomerCard`, `InteractionTile`)
- `state/` — State management for this feature (provider/riverpod/bloc)

Do not import from other feature folders' `presentation/` or `domain/` layers.
