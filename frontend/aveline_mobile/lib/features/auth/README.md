# Feature: Auth

Sign-in / sign-up, session management, and auth-token provisioning via Clerk.

## Layers

### `domain/`
- `auth_repository.dart` — `AuthRepository` contract (sign-in state, current user,
  token access). Extends `core/network/auth_token_provider.dart` so the HTTP layer
  depends on an abstraction, not on this feature.
- `auth_user.dart` — `AuthUser` model (identity + Aveline role claims).
- `auth_claims.dart` — `AuthClaims` parsed from the `jwt-aveline-v1` JWT body
  (`user_role`, `org_role`, `org_id`, `org_slug`).

### `data/`
- `clerk_auth_repository.dart` — `AuthRepository` backed by the Clerk Flutter SDK.
  Tokens are minted from the Aveline JWT template; sessions are persisted by the SDK.
- `auth_user_mapper.dart` — maps Clerk `User` onto `AuthUser`.

### `presentation/screens/`
- `auth_screen.dart` — sign-in / sign-up entry using the SDK's prebuilt UI.

## Notes
- The sign-in/sign-up methods shown are controlled by the Clerk Dashboard instance.
- Session persistence across restarts is handled by the Clerk SDK.
