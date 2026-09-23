# Roles & Permissions

Every person in your boutique holds one role: **Owner**, **Supervisor**, **Manager** or **Staff**.
The role decides which sections of the dashboard open, which buttons appear inside them, and what
the boutique's server will accept.

Your role belongs to your membership in one boutique. If you belong to more than one, the header's
boutique switcher moves you between them, and you may hold a different role in each.

---

## What each role can do

| What you want to do | Staff | Supervisor | Manager | Owner |
|---|:--:|:--:|:--:|:--:|
| See the dashboard and its Overview | ✓ | ✓ | ✓ | ✓ |
| Work in the Salon and talk to Aveline | ✓ | ✓ | ✓ | ✓ |
| Read the client book | ✓ | ✓ | ✓ | ✓ |
| Add, edit or remove a client | | ✓ | ✓ | ✓ |
| Log a visit or interaction | ✓ | ✓ | ✓ | ✓ |
| Read the catalog, compose looks, print floor tags, work sourcing | ✓ | ✓ | ✓ | ✓ |
| Record a counter sale | ✓ | ✓ | ✓ | ✓ |
| Add, edit or remove a piece | | ✓ | ✓ | ✓ |
| Reduce stock or mark a piece out of stock | | ✓ | ✓ | ✓ |
| Edit or delete a lookbook | | ✓ | ✓ | ✓ |
| Open the income register | | ✓ | ✓ | ✓ |
| Open the approval queue | ✓ | ✓ | ✓ | ✓ |
| **Approve** an order | ✓ | ✓ | ✓ | ✓ |
| **Reject** or **revise** an order | | ✓ | ✓ | ✓ |
| See the Blossom balance | ✓ | ✓ | ✓ | ✓ |
| See usage against plan limits, burn rate and consumption | | | ✓ | ✓ |
| See API consumption | | ✓ | ✓ | ✓ |
| Open Billing | | | ✓ | ✓ |
| Top up Blossoms | | | | ✓ |
| Open Team and manage staff and codes | | ✓ | ✓ | ✓ |
| Open Integrations | | | | ✓ |
| Open Settings and manage API keys | | | | ✓ |

## How the roles differ, in one line each

- **Owner** — full authority: the only role that reaches Billing, Settings and the integrations
  credentials. One owner is created when the boutique is set up.
- **Manager** — runs the shop day to day: clients, catalog, income, staff and the sourcing side of
  orders. Notably a Manager cannot **approve** an order, and never sees your channel credentials or
  Billing.
- **Supervisor** — oversees the counter: clients, catalog, the income register and the full set of
  approval decisions.
- **Staff** — the counter: clients, visits, orders and the Salon, including approving a customer's
  order. No register, no staff management, no settings.

## Approvals need two different permissions

Approvals are the one place where the split is worth spelling out, because the three decisions do
different things:

- **Approve** keeps the price the customer was quoted and confirms the order. Staff, Supervisor and
  Owner hold this.
- **Reject** cancels the order. Supervisor and Owner hold this.
- **Revise** rewrites the order's discount and total. Supervisor and Owner hold this.

A Manager holds the permission to change orders but not the one to approve them, so a Manager does
not see the Approvals section at all. A button your role does not hold is **not shown** rather than
shown and refused. See [Approvals](/docs/approvals).

## Changing someone's role

1. Open [Team](/docs/team).
2. Find the member and click **Change role**.
3. Choose Supervisor, Manager or Staff, and click **Save role**.

A role change takes effect on that member's next request, so they see their new permissions the
next time they load the dashboard.

Two rows are deliberately locked:

- **Your own** — you cannot change your own role or remove yourself.
- **An Owner's** — an owner membership cannot be suspended or removed, and a member cannot be
  promoted to Owner from this dialog.

## What the dashboard hides is not what enforces it

The dashboard shows only the sections and buttons your role holds, so you are never offered an
action that cannot work — but the appearance is not the rule. Every request is authorised again on
the server against your membership in the boutique being addressed, including the permission that
route needs.

That is also why a role change takes effect on the member's next request: the session stays valid,
but the permissions behind it are read fresh from their membership.

## Related

- [Team](/docs/team) — where roles are changed and invitation codes are minted.
- [Joining a Boutique](/docs/joining-a-boutique) — how a new colleague arrives with a role attached.
- [Privacy & Security](/docs/privacy-security) — how tenant isolation and identity work.
