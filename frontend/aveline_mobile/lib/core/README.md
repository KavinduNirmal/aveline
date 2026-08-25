# Core — App-wide Technical Infrastructure

This folder contains shared technical infrastructure used across all features.
It has no UI components and no business logic.

## Subfolders

### `config/`
App-wide constants and configuration:
- `app_config.dart` — API base URL, Clerk publishable key, feature flags
- `environment.dart` — Reads from `.env` using `flutter_dotenv` or compile-time constants

### `network/`
HTTP client setup:
- `api_client.dart` — Configured `Dio` instance with base URL, auth interceptor, error interceptor
- `auth_interceptor.dart` — Attaches Clerk JWT bearer token to every request
- `error_interceptor.dart` — Maps HTTP errors to typed app exceptions

### `router/`
Navigation/routing:
- `app_router.dart` — Route definitions using `go_router` (or chosen routing package)
- `route_guards.dart` — Auth guards that redirect unauthenticated users to login

### `theme/`
App-wide visual design:
- `app_theme.dart` — `ThemeData` definition (colors, typography, component themes)
- `app_colors.dart` — Brand color palette constants
- `app_text_styles.dart` — Typography scale

## Rules

- Code here must NOT import from any `features/` folder
- `config/` must NOT contain secrets — only safe constants
- `network/` should have a single `Dio` instance (use a DI solution or singleton)
