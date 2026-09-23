# Team

**Team** is who works in your boutique and what each person may do. It holds the member list and
the invitation drawer that mints onboarding codes for new staff.

Open it from the sidebar at `/app/b/<your-boutique>/team`. The section is available to a
**Manager**, **Supervisor** or **Owner**; a Staff account does not see it.

---

## The member list

The list is the page. Search by name or email, or filter by status with **All statuses**,
**Active** or **Suspended**. Each row shows:

| Column | What it means |
|---|---|
| **Member** | The person's display name, with their email underneath. |
| **Role** | Owner, Supervisor, Manager or Staff. |
| **Status** | **Active** or **Suspended**. |
| **Joined** | The date their membership began. |
| **Actions** | Change role, suspend or activate, and remove. |

When the boutique has more than 20 members, the footer shows **Page 1 of N · N members** with
**Previous** and **Next**.

### Change a role

1. Click **Change role** on the member's row.
2. Choose **Supervisor**, **Manager** or **Staff** in **Boutique role**.
3. Click **Save role**. The confirmation reads **Role updated.**

A role change takes effect on the member's next request, so the new permissions apply immediately
after they next load the dashboard. The available roles are the three staff roles — a member
cannot be promoted to Owner from this dialog.

### Suspend and reactivate

Click **Suspend** to cut off access without removing the membership, and **Activate** to restore
it. Both take effect straight away and neither asks for confirmation.

### Remove a member

Click **Remove** and confirm. The dialog states that the person will lose access to this boutique.
The confirmation reads **Member removed.**

### Rows you cannot change

Two rows are deliberately locked, and each states its reason on the row rather than letting the
server refuse the action:

- **Your own row** — *You cannot change your own role or remove yourself.*
- **An Owner's row** — *An owner membership cannot be suspended or removed.*

If the server refuses an action for any other reason, its own message is shown in the dialog.

---

## Invitations

A new staff member joins with an invitation code. Codes are minted in a side drawer, opened from
either **Invite staff** or **Pending codes** in the page header.

### Generate a code

1. Click **Invite staff** (or **Pending codes**, then **New code**).
2. Choose the **Staff role**. The drawer explains each one:
   - **Manager** — *Runs the shop day to day: clients, catalog, income and staff, without the
     integrations credentials.*
   - **Supervisor** — *Approves and oversees the counter: clients, catalog and the income
     register.*
   - **Staff** — *The counter: clients, visits and orders, with no register or settings.*
3. Choose the **Code lifetime**: **24 Hours (Standard)**, **7 Days** or **30 Days**. After that the
   code stops working. The server clamps any lifetime to between one hour and 30 days.
4. Choose **How many**: **One**, or **Batch** for up to ten codes at a time.
   - With **One**, you can optionally enter an address in **Send to**. Leave it empty to copy the
     code and share it yourself.
   - With **Batch**, set **Codes to mint** between 1 and 10. Each code is unique and 12 characters
     long.
5. Optionally switch on **Email me a summary**. It goes to your owner address — and the code is
   never in the email.
6. Click **Generate code**.

The result panel shows the code once. Copy it or show its QR code:

- **Copy code** puts the code on your clipboard; **Copy link** copies a shareable invitation link.
- **Show QR code** displays a QR code to scan with a phone camera, which opens the invitation link
  directly.
- **Batch** shows every code with its own copy and QR controls.

> **The code is shown once.** It is stored hashed, so it cannot be looked up again — revoke it and
> mint a new one if it is lost. Copy or scan each code before you leave the drawer.

### Pending codes

The **Pending codes** view lists every code that has not been used, with two counters:
**Outstanding** and **Expiring soon** (a code with less than six hours left).

Each row shows who it was addressed to (or **Not addressed to anyone**), how long it has left
(**expires in 3 days**, **expires now**, and **- expiring soon** when it is close), the role it
grants and the date it was minted.

**Pending codes never show the code itself.** The list is a non-secret view, so there is nothing
to copy from it. If a code is lost, revoke it and generate a new one.

Click **Revoke** to cancel a code that has not been used. The confirmation reads **Invitation code
revoked.** A code that has already been accepted cannot be revoked.

---

## What the invited person does

Share the code, or the invitation link, with your new colleague. They sign up, choose **I'm
Staff**, and enter the code. See [Joining a Boutique](/docs/joining-a-boutique) for the steps to
send them.

## When something is not on screen

| What you see | What it means |
|---|---|
| **Could not load the member list.** | The read failed. Press **Try again**. |
| **No members match this view.** | Nothing matches the current search or status filter. |
| **No active codes** | No pending invitation exists. **Generate one** opens the code generator. |
| **Loading codes...** | The pending list is being read. A closed drawer issues no request. |

## Limits

- Codes are minted in batches of at most ten, and creating batches is rate-limited. If you generate
  too many in quick succession, the drawer asks you to wait a minute.
- A member cannot be made an Owner from the dashboard, and Owner rows cannot be suspended or
  removed.
- There is no import, and members are added one invitation at a time.
