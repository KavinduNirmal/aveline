# Feature: Catalog

Dock tab for visual intelligence & sourcing (Slice 2).

## Status

The catalog reads as a boutique's floor: the shop's name in the title, a
catalog-scoped search, the shop's own tag row, the filter options, and a
two-column grid of pieces that pages itself in as the associate scrolls. The
data is a demo pool; create, edit, import and the sourcing pipeline are not
built yet.

## Layers

### `presentation/screens/`
- `catalog_screen.dart` — the Catalog dock tab: the `{boutique} - Catalog`
  title in Playfair Display (fading at the trailing edge only when the name
  genuinely overflows), a search field scoped to this catalog rather than the
  header's global search, the shop tag row, the filter entry point, and the
  infinite-scrolling grid. The title and the field themselves live in
  `shared/widgets` (`BrandSectionTitle`, `SectionSearchField`) because Customers
  carries the same header; see `shared/README.md`.
- `catalog_filter_screen.dart` — the search filter options, pushed from the
  field row. It edits a draft and returns it on "Show results", so the catalog
  owns the applied value.
- `catalog_product_screen.dart` — a single piece, opened by tapping a card.
  Cards down the page: the piece at a glance, pricing (the tag price at display
  size, then cost, margin, allowed discount and floor price), the five actions,
  a table of every field `InventoryItemDto` carries, the boutique's own notes,
  and the description.
  The route carries only the id: an earlier version pushed the piece as route
  `extra` for an instant first frame, but the router re-parses a location
  whenever the auth or profile listenable fires and `extra` does not survive
  that, which left the screen with nothing to draw. Resolving from the id makes
  the screen a pure function of the location.

  The actions are local until the inventory API grows mutations: **Create a
  hold**, **Mark unavailable** and **Mark sold out** move the piece's status,
  **Request a supply** and **Mention to Aveline** record that they were asked
  for. Each carries its own tone, and each is inert once the piece's state no
  longer allows it. **Mention to Aveline** wears the brand's own atmosphere (the
  drifting blobs and blossoms from `BlossomWash`) while it is still available to
  press, because it is the one action that hands the piece to the agent.

### `presentation/`
- `catalog_products_controller.dart` — pages the catalog: owns the loaded
  pages, the in-flight guard, the end-of-catalog flag, and the stale-reply
  guard that drops a page for an abandoned query.

### `presentation/widgets/`
- `catalog_product_card.dart` — the grid card: image, price, stock, copy,
  dominant-colour dot and sizes. The whole card is the affordance; there is no
  button on it.
- `catalog_product_image.dart` — the photograph, or the colour-keyed stand-in
  while the boutique has none.
- `catalog_tag_row.dart` — the shop's tags as a horizontally scrolling row.

### `domain/`
- `catalog_product.dart` — a piece, and where it sits in stock.
- `catalog_tag.dart` — a tag a boutique defines for its own pieces.
- `catalog_filters.dart` — the grouped filter options and the immutable
  `CatalogFilters` value the screen edits.

### `data/`
- `catalog_product_repository.dart` — the paging contract, the by-id lookup the
  detail screen resolves through, and the query a page is fetched against.
- `demo_catalog_product_repository.dart` — a boutique's worth of pieces, paged
  and narrowed in memory.
- `demo_catalog_tags.dart` — placeholder stand-in for a shop's own tag list.

## Data

The boutique name comes from `BoutiqueProvider`
(`core/providers/boutique_provider.dart`), which reads `GET /api/v1/orgs/my`.
It falls back to the brand name so the title still reads as a title before the
name loads or when no boutique is linked.

## Paging

`CatalogProductsController` fetches eight pieces at a time, and the grid asks
for the next page once it comes within 400 logical pixels of its end. The footer
carries the loading spinner, a retry when a page fails, and "That is the whole
catalog." once the last page is in. A reply for a query that has since been
replaced is dropped rather than appended.

## Tags, filters and search

Tags are per-shop: every boutique curates its own vocabulary, so the row is fed
by the shop's tag list rather than a shared set. It is placeholder data until
the inventory slice is wired, as are the filter options (availability,
category, fabric, size, price; availability is the only single-select group).

Search, tags and filters all narrow the same paged query, so the grid, the
empty state and the filter badge always agree.
