# Shared — Reusable Widgets and Utilities

This folder contains **truly reusable** code that is not specific to any feature.

## Subfolders

### `widgets/`
Reusable UI components used by multiple features:
- `app_button.dart` — Primary/secondary/destructive button variants
- `app_text_field.dart` — Styled text input
- `loading_indicator.dart` — Spinner / shimmer placeholder
- `error_display.dart` — Error state widget
- `empty_state.dart` — Empty list / no-results state widget
- `avatar.dart` — Customer or product avatar component

### `utils/`
Pure Dart utility functions (no Flutter dependencies):
- `date_formatter.dart` — Date/time display helpers
- `currency_formatter.dart` — LKR formatting
- `validators.dart` — Phone number, email, required field validators

## Rules

- Widgets here must NOT contain business logic or API calls
- Widgets must NOT depend on any feature-specific state or model
- If a widget is only used in one feature, it belongs in `features/<name>/presentation/widgets/`
- Utilities must be pure functions — no side effects, no global state
