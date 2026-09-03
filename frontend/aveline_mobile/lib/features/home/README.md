# Feature: Home

Post-auth landing screen shown after sign-in.

## Layers

### `presentation/screens/`
- `home_screen.dart` — greets the signed-in user, shows role claims, and offers
  sign-out (the router redirects back to the auth screen on sign-out).
