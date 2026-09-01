# Aveline Admin Dashboard (`frontend/web`)

React + Vite + TypeScript admin dashboard for boutique **owners and managers**
(approvals, customers, inventory, analytics). It consumes the **Aveline.Api**
ASP.NET Core API only — never the Python agent service directly.

## Stack

- **Vite 8** + **React 19** + **TypeScript**
- **Tailwind CSS v4** + **shadcn/ui** — design system components
- **@clerk/react** — authentication, sessions, prebuilt sign-in/sign-up
- **react-router-dom v7** — declarative routing with a `ProtectedRoute` guard
- **axios** — shared API client (JWT interceptor added in Issue #13)
- **oxlint** — linting

## Design system

The UI follows the "Serene Concierge" brand (`.agents/brain/DESIGN.md`):
warm oatmeal surfaces, deep maroon primary, and Playfair Display + Hanken
Grotesk typography. Brand **colors, typography, and radius** are mapped to the
shadcn theme tokens in `src/index.css` (the DESIGN spacing tokens are not used).

## Getting started

```bash
cd frontend/web
bun install
cp .env.example .env.local   # then fill in VITE_CLERK_PUBLISHABLE_KEY
bun dev
```

Environment:

| Variable | Required | Purpose |
|---|---|---|
| `VITE_CLERK_PUBLISHABLE_KEY` | yes | Clerk publishable key (`pk_...`) |
| `VITE_API_BASE_URL` | no | Defaults to `http://localhost:5091` |

## Scripts

| Command | Description |
|---|---|
| `bun dev` | Start the Vite dev server |
| `bun run build` | Type-check (`tsc -b`) and production build |
| `bun run lint` | oxlint |
| `bun run preview` | Preview the production build |

## Structure

```
src/
├── main.tsx                 # Entry: ClerkProvider > BrowserRouter > App
├── App.tsx                  # Route table (protected tree + auth pages)
├── index.css
├── components/              # ProtectedRoute, SignOutButton
├── lib/                     # env.ts (typed env), api.ts (axios client)
└── routes/                  # RootLayout, Dashboard, SignInPage, SignUpPage
```

## Routing & auth

- `/sign-in`, `/sign-up` — Clerk prebuilt components (multi-step flows via `/*` wildcard).
- Everything else sits behind `ProtectedRoute`, which redirects unauthenticated
  users to `/sign-in`. On sign-in/up the user lands on `/` (dashboard).
- Session persistence, refresh, and sign-out are handled by Clerk.
- Role-based admin enforcement is Issue #12; the JWT API interceptor is Issue #13.
