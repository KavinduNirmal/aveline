# ADR-005: State Management for the Flutter App

## Status
Accepted

## Context
The Flutter mobile app (associates) uses clean-architecture feature modules
(`features/*/data|domain|presentation`). It needs auth state (Clerk), role-aware
screens, and API calls with a JWT. The chosen stack must be testable and light.

## Options Considered
1. **Provider + ChangeNotifier** (chosen) — the Clerk SDK's `ClerkAuthState` is
   already a `ChangeNotifier`, so Provider composes naturally. Lightweight,
   first-party testable, minimal ceremony.
2. **Riverpod** — compile-safe DI, more modern. Pros: stronger. Cons: heavier
   ceremony and an additional mental model for a small team.
3. **BLoC** — explicit event/state. Pros: structured. Cons: boilerplate for an
   auth-centric app.
4. **setState + InheritedWidget** — Pros: zero deps. Cons: manual plumbing.

## Decision
**Provider + ChangeNotifier**, with `AuthRepository` (implemented by
`ClerkAuthRepository` over the Clerk SDK) and `go_router` for auth-gated
navigation (`RouteGuards.redirectForAuth`). HTTP is dio with the auth
interceptor; DI wires a single `AuthRepository` + `Dio` via `MultiProvider`.

## Consequences
- Auth state flows from `ClerkAuthState` (ChangeNotifier) through Provider to
  screens and the router (`refreshListenable`).
- If feature complexity grows (caching, streams), migrate to **Riverpod**
  incrementally — the repository/domain layer is already provider-agnostic.
