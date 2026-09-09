# Feature: Home

Post-auth landing shell shown after sign-in.

## Layers

### `presentation/screens/`
- `main_shell.dart` — hosts the floating dock and swaps the active tab body
  (Home, Customers, Catalog, Profile). The center Salon launcher pushes the
  full-screen Salon route.
- `home_screen.dart` — the Home tab: a quiet-luxury greeting and boutique
  overview cards, mirroring the web Overview section.
