# ADR-004: State Management for the React Dashboard

## Status
Accepted

## Context
The web dashboard (owners/managers) is an auth-heavy SPA: approvals, analytics,
and role-gated routes. It consumes the ASP.NET Core API exclusively. The team
wants minimal dependencies and a small learning surface.

## Options Considered
1. **Server-driven state + lightweight client** (chosen) — React hooks, Clerk context, React Router v7, axios with a token interceptor, shadcn/ui + Tailwind. Client state is limited to routing/guard state; server data stays server-side.
2. **Redux / Zustand** — global client stores. Pros: familiar. Cons: boilerplate; most dashboard state is server state that benefits from a data-fetching layer instead.
3. **TanStack Query + Zustand** — Pros: server-state caching, invalidation. Cons: added dependency now; not needed until data-heavy pages exist.

## Decision
**React hooks + Clerk context + React Router** with `ProtectedRoute` /
`RequireAdmin` guards and an axios client (Bearer token via the Issue #13
interceptor). No global client-state library yet. UI is shadcn/ui components on
Tailwind v4 with the Aveline brand theme.

## Consequences
- Simple and testable; route/role guards are explicit components.
- When data-heavy screens land, adopt **TanStack Query** for server-state caching
  without changing the auth architecture.
- Client-only state management libraries are deferred until there is real client state to hold.
