# Aveline Mobile — Flutter App

Flutter app for boutique **associates** (customer concierge, inventory,
commerce). Clean-architecture feature modules under `lib/features/`, with
Clerk authentication.

## Prerequisites

- Flutter stable (Dart 3.13+)

## Run

```bash
cd frontend/aveline_mobile
flutter pub get
flutter run \
  --dart-define=CLERK_PUBLISHABLE_KEY=pk_test_... \
  --dart-define=API_BASE_URL=http://10.0.2.2:5091
```

| Dart define | Required | Purpose |
|---|---|---|
| `CLERK_PUBLISHABLE_KEY` | yes | Clerk publishable key (`pk_...`). |
| `API_BASE_URL` | no | Defaults to `http://10.0.2.2:5091` (host API from the Android emulator). Use the host LAN IP on a physical device. |
| `JWT_TEMPLATE_NAME` | no | Defaults to `jwt-aveline-v1` (mints `user_role` / `org_role` claims). |

## Authentication

- Sign-in / sign-up via the Clerk SDK's prebuilt UI (`features/auth/`).
- Sessions persist across restarts (SDK); the router redirects based on auth
  state (`core/router/route_guards.dart`).
- Every API call attaches `Authorization: Bearer <jwt>` and refreshes / signs
  out on 401 (`core/network/auth_interceptor.dart`).
- `AuthUser` + `AuthClaims` expose the Aveline role claims to role-aware screens.

## Structure

```
lib/
├── main.dart / app.dart       # entrypoint + composition root (ClerkAuth > DI > router)
├── core/                      # config, network (Dio + interceptor), router, theme
└── features/
    ├── auth/                  # sign-in/up, session, token provisioning
    ├── home/                  # post-auth landing screen
    ├── customers/  inventory/  commerce/   # domain slices
```

## Tests

```bash
flutter test
```

Covers the auth token interceptor, JWT claim parsing, route guards, and the
app theme (`test/`).

## Further reading

- Auth architecture: `docs/architecture/authentication.md`
- State management: `docs/ADR/ADR-005-state-management-flutter.md`
- JWT strategy: `docs/ADR/ADR-008-jwt-token-strategy.md`
