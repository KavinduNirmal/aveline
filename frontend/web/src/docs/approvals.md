# Approvals

**Approvals** is the queue of discounts and orders waiting on a decision from your team.
Approving keeps the price the customer was quoted, rejecting cancels the order, and revising
rewrites it.

Open it from the sidebar at `/app/b/<your-boutique>/approvals`. The section is available to a
**Staff**, **Supervisor** or **Owner** account.

---

## The queue

Each row shows:

| Column | What it shows |
|---|---|
| **Order** | The customer's name and the order total. When the order is no longer readable, the row says **Order unavailable** and shows the order reference instead. |
| **Type** | What kind of approval this is, plus an **over threshold** badge when the request broke a configured limit. |
| **Reason** | Why the approval was requested. |
| **Status** | `pending`, `approved`, `rejected` or `revised`. |
| **Decision** | The decision buttons your role may use. |

The newest request is at the top. **Filter by status** narrows the list to All statuses,
`pending`, `approved`, `rejected` or `revised`.

The card's description tells you what your role may do — *You may approve* when that is the whole
of it, or *You may view this queue, but no decision verb is available to your role.* when it is
not.

## Deciding

Click the verb you want on the row, add a note if it helps, and confirm.

| Verb | What it does | Dialog says | Role needed |
|---|---|---|---|
| **Approve** | The order proceeds, and the discount the customer was quoted stands. | *The order proceeds. The discount the customer was quoted stands.* | Staff, Supervisor, Owner |
| **Reject** | The order is **cancelled**. | *Rejecting cancels the order. This cannot be undone from the dashboard.* | Supervisor, Owner |
| **Revise** | The order's discount and total are **rewritten**. | *Revising rewrites the order's discount and total. The money figures change.* | Supervisor, Owner |

Two details of the decision dialog:

- **Reason (optional)** is sent with the decision. A note is never required, but it is what a
  colleague will read later.
- **Revised discount** appears only for **Revise**, and only takes effect when you fill it in.
  Leave it empty and the order's money is left alone while the request is still marked revised.

Confirm with **Confirm approve**, **Confirm reject** or **Confirm revise**. There is no Cancel
button in the dialog — press Escape or click outside it to back out.

When the decision succeeds, the dialog closes and the queue refreshes. If the server refuses it,
its own explanation is shown inside the dialog and the dialog stays open, so the note and the
discount you typed are not lost.

### Verbs your role does not have are not shown

A button your role cannot use is **absent**, not disabled and not a button that fails when you
press it. The same split is enforced on the server, so the queue and the API agree about who may
do what.

| Role | Verbs available |
|---|---|
| **Staff** | Approve |
| **Supervisor** | Approve, Reject, Revise |
| **Owner** | Approve, Reject, Revise |
| **Manager** | No Approvals section. A Manager holds the permission to reject and revise orders but not the approval permission the section requires. |

## When something is not on screen

| What you see | What it means |
|---|---|
| **Nothing waiting. An order that exceeds a discount threshold appears here.** | The queue is empty for the selected status. |
| **Could not load the approval queue.** | The read failed. Press **Try again**. |
| **You may view this queue, but no decision verb is available to your role.** | The section is readable, but your role holds no decision permission. |
| **Order unavailable** | The order behind the request can no longer be read, so only its reference is shown. |

## Limits

- The queue is the whole surface. There is no separate detail page for a request.
- A rejection cannot be undone from the dashboard.
- A Manager cannot approve: approving is the Staff-and-above permission, while rejecting and
  revising additionally require order management.
