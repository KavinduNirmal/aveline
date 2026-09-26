# Your Dashboard

Your dashboard is the boutique's workspace: a sidebar of sections on the left, and a header with
the things you reach from anywhere. Every section has its own address, so any of them can be
bookmarked and will survive a refresh.

The address is `/app/b/<your-boutique>/<section>`. Opening the boutique without a section lands on
**Overview**.

---

## The sidebar

At the top is your boutique — its logo or initials, its name, and the plan you are on.

Below that is the navigation. **What you see depends on your role**: a section your role cannot
open is not shown at all, and typing its address by hand returns you to Overview rather than
opening a panel you should not see.

| Section | What it is for | Roles that see it |
|---|---|---|
| **Overview** | The day's numbers and your first stop. | All |
| **Salon** | Client conversations and the Aveline concierge. | All |
| **Customers** | Your client book. | All |
| **Catalog** | Pieces, lookbooks, sourcing and ateliers. | All |
| **Income** | What the shop took from its clients. | Manager, Supervisor, Owner |
| **Approvals** | Discounts and orders waiting on a decision. | Staff, Supervisor, Owner |
| **Integrations** | WhatsApp, Instagram and payment gateway. | Owner |
| **Team** | Staff, roles and invitation codes. | Manager, Supervisor, Owner |
| **Usage** | Blossom balance and plan usage. | All |
| **Billing** | Plan, statement of account and top-ups. | Manager, Owner |
| **Settings** | Boutique profile and API keys. | Owner |

At the bottom sits your own account card, with **Settings** (Owner), **Billing & plan** and
**Sign out**.

## The header

| Control | What it does |
|---|---|
| **Switch boutique** | Appears when you belong to more than one active boutique. Choose one to move to it. |
| **Blossom balance** | How many Blossoms remain this period. Hovering shows *n of m Blossoms left*. If the server did not report a balance it reads **Balance unavailable** — never a zero. |
| **Top up** | Opens the top-up dialog for an Owner: choose a pack, pay through the payment provider's own checkout, and the balance moves only once the server confirms the payment. A role that cannot buy Blossoms is sent to Billing instead. |
| **Reporting window** | One window for the whole dashboard: **Last 7 days**, **Last 30 days**, **Last 90 days**, **This month** or **This year**. Every panel on the page follows it, so two figures on one screen can never describe different periods. |
| **Notifications** | See below. |
| **Aveline chat** | The blossom button opens the concierge chat without leaving the section you are in. See [Salon](/docs/salon). |

### Notifications

The bell carries a count of unread notifications, capped at **9+**. Opening it lists each
notification with its title, its message and how long ago it arrived — *just now*, *5m ago*,
*2h ago*, *3d ago*, or a date for anything older.

- **Click a notification** to mark it read.
- **Mark all read** clears the whole list when something is unread.
- **Dismiss** (the × on a row) removes it.
- When nothing is waiting it reads **You're all caught up.**

Notifications arrive in real time while you are signed in, and pop up as a message as they land.

---

## The Overview page

**Overview** greets you by name, shows your plan, and starts with the figures every role may see.

### The Takings card

Every role sees **Takings**: two labelled figures, always both — **Collected** (money confirmed as
taken, net of refunds) and **Billed, unconfirmed** (what the orders say was sold). They are never
added together, because one is money and the other is a promise. If nothing was measured for the
window, the card says so instead of showing a zero.

### The detailed strip

If your role holds the reports permission — Manager, Supervisor or Owner — a strip of figures
follows:

| Figure | What it means |
|---|---|
| **Gross order value** | Billed value, not cash. |
| **Average order** | Not measured when there are no orders to average. |
| **Collected** | Confirmed money, excluding refunds and outstanding requests. |
| **Margin** | Total minus recorded cost. Flagged **cost data incomplete** when an order carries no wholesale cost. |
| **Outstanding** | Requested and not yet confirmed. Not a receivable. |
| **Refunded** | Money returned, with the number of refunds. |
| **Clients on file** | How many clients you hold, and how many have visited twice or more. |
| **Items listed** | Pieces in the catalog, and how many are low on stock. |
| **Pending approvals** | Orders waiting on a decision. |
| **Open conversations** | Threads that are open in the Salon. |
| **Stock value at cost** | At cost, never retail value. |

Any figure the server did not measure reads **not measured**. `0` means the server measured a
zero.

Below the strip, a **What these figures do not say** card lists what the numbers cannot tell you —
for example that no payment rows exist yet, so the cash figures come from counter sales only.

### Previewing what your staff see

An owner, manager or supervisor can click **Preview the staff view**. The detailed figures are
hidden and the page says so, exactly as a staff member experiences it. This is a preview only: it
sends no request and gives you no new data. **Show my full view** turns it back on.

### The cards at the bottom

- **Blossoms** — the balance remaining this period, with your plan.
- **Pending approvals** — the count waiting on a decision, or a note that it is visible to an
  approver.
- **Clients active** — how many clients visited recently, against the inactivity threshold the
  server reports for the boutique.

## When something is not on screen

| What you see | What it means |
|---|---|
| **Could not load the boutique's takings.** | The read failed. Press **Try again**. |
| **The detailed figures could not be loaded. The takings above are unaffected.** | The strip failed while the Takings card succeeded. |
| **Balance unavailable** | The server did not return a usage summary for this period. |
| **Boutique not found** | The address names a boutique that does not exist. Use **Back to your dashboard**. |
| **No access** | Your account holds no membership that may open this dashboard. |

## Limits

- A section your role cannot open is not rendered, and a hand-typed address falls back to
  Overview. There is nothing to click that answers "not allowed".
- The window you choose applies to the whole dashboard; there is no per-panel period.
- Figures cover the selected window only. Anything older is outside it, and the dashboard says so
  rather than extrapolating.
