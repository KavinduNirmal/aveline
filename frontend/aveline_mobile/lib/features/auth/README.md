# Feature: Auth

Sign-in / sign-up, session management, and auth-token provisioning via Clerk.

## Layers

### `domain/`
- `auth_repository.dart` — `AuthRepository` contract (sign-in state, current user,
  token access). Extends `core/network/auth_token_provider.dart` so the HTTP layer
  depends on an abstraction, not on this feature.
- `social_auth_repository.dart` — `SocialAuthRepository`, the provider-consent half
  of the contract. Separate because it is the one part that needs a `BuildContext`
  (the provider's page is shown in a dialog over the calling widget), and because a
  fake that stands in for password sign-in should not have to implement it.
- `social_provider.dart` — `SocialProvider`, one enabled social connection, built
  from the instance's environment rather than hard-coded.
- `sign_up_field.dart` — `SignUpField`, the details an instance can still ask for
  after a provider has handed over an identity.
- `auth_user.dart` — `AuthUser` model (identity + Aveline role claims).
- `auth_claims.dart` — `AuthClaims` parsed from the `jwt-aveline-v1` JWT body
  (`user_role`, `org_role`, `org_id`, `org_slug`).
- `auth_failure.dart` — `AuthFailure` copy per Clerk error code, and
  `AuthCapabilities`, which carries what the instance allows (username, password
  length, CAPTCHA, enabled social providers).

### `data/`
- `clerk_auth_repository.dart` — `AuthRepository` and `SocialAuthRepository` backed
  by the Clerk Flutter SDK. Tokens are minted from the Aveline JWT template;
  sessions are persisted by the SDK.
- `auth_user_mapper.dart` — maps Clerk `User` onto `AuthUser`.

### `presentation/`
- `screens/auth_screen.dart` — sign-in / sign-up entry, using the SDK's custom-flow
  APIs rather than its prebuilt widgets.
- `widgets/sign_in_form.dart`, `widgets/sign_up_form.dart` — the password flows.
- `widgets/social_auth_buttons.dart` — the provider buttons, and the steps that
  finish a sign-up the provider could not complete on its own.
- `widgets/social_provider_mark.dart` — the brand marks, drawn locally from the
  same paths the web draws.

## Notes
- The sign-in/sign-up methods shown are controlled by the Clerk Dashboard instance.
  The password form, the username field and the provider buttons all read that
  instance rather than assuming one; production has Google enabled and Facebook
  disabled, the development instance has both.
- Session persistence across restarts is handled by the Clerk SDK.
- **A provider button needs the instance to authorise the app's redirect.** The SDK
  sends `com.clerk.flutter://callback` as the OAuth `redirect_url`, and Clerk
  rejects any redirect that is not the requesting origin or an authorised redirect
  URL. An instance with no authorised redirect URLs answers with
  `resource_missmatch` ("does not match an authorized redirect URI") and the
  provider page never opens. Add `com.clerk.flutter://callback` under Configure ->
  Redirect URLs in the Clerk dashboard; this is already set on production.
- The provider's consent page is the SDK's, not ours: it opens the provider in a web
  view, intercepts the redirect and applies the user agent Google's embedded-browser
  rules need. `ClerkAuth` must therefore stay above whatever shows the buttons,
  because the SDK looks up its overlay host from the widget it is handed.
