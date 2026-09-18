# Shared — Reusable Widgets and Utilities

This folder contains **truly reusable** code that is not specific to any feature.

## Subfolders

### `widgets/`
Reusable UI components used by multiple features:
- `blossom_refresh.dart` — pull-to-refresh for the app shell: a blossom that
  unfurls as the pull arms, plus the `RefreshScope` revision that screens holding
  their own copy of the data re-seed from
- `brand_section_title.dart` — `{boutique} - {section}` in the brand serif,
  dissolving at the trailing edge only when the name overflows the line (the
  Catalog and Customers headers)
- `section_search_field.dart` — a screen's own search field, with the hint and
  the clear affordance; scoped to that screen rather than the header's global
  search
- `filter_pill.dart` — one selectable pill, shared by the catalog's tag row and
  filter groups and the customers level row so they cannot drift apart
- `app_button.dart` — Primary/secondary/destructive button variants
- `app_text_field.dart` — Styled text input
- `loading_indicator.dart` — Spinner / shimmer placeholder
- `error_display.dart` — Error state widget
- `empty_state.dart` — Empty list / no-results state widget
- `avatar.dart` — Customer or product avatar component

### `utils/`
Pure Dart utility functions (no Flutter dependencies):
- `greeting.dart` — Time-of-day salutations for personalized headings
- `date_formatter.dart` — Date/time display helpers
- `currency_formatter.dart` — LKR formatting
- `validators.dart` — Phone number, email, required field validators

## Rules

- Widgets here must NOT contain business logic or API calls
- Widgets must NOT depend on any feature-specific state or model
- If a widget is only used in one feature, it belongs in `features/<name>/presentation/widgets/`
- Utilities must be pure functions — no side effects, no global state
