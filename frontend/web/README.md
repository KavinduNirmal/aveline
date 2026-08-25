# Web Dashboard — Frontend

> **⚠️ Technology TBD**
>
> The web frontend technology stack has not yet been finalized.
> This folder is reserved for the React (or alternative) web dashboard.
>
> Once the technology is confirmed, this folder will be scaffolded with the
> appropriate project structure and this README will be updated.

## Planned purpose

The web dashboard is the **React application for boutique owners and managers**.

It provides:
- Pending approval queue (approve/reject/revise orders)
- Customer relationship overview
- Inventory and sourcing management
- Analytics and business rule configuration

## Planned users

| Role | Access |
|---|---|
| Owner | Full access — approvals, analytics, business rules |
| Manager | Approvals and operational views |

## Auth

Authentication will use **Clerk** — same JWT tokens as the Flutter app and ASP.NET Core API.

## API

The web dashboard consumes the **ASP.NET Core Web API** exclusively.
It does NOT call the Python agent service directly.

---

*Do not add any code to this folder until the technology is decided and the team has agreed.*
