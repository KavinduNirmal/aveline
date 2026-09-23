# Customers

**Customers** is your client book: everyone your boutique has on file, with their contact
details, what they have spent, when they last came in, and everything your team has recorded
about their visits.

Open it from the sidebar at `/app/b/<your-boutique>/customers`.

---

## What the list shows

| Column | What it means |
|---|---|
| **Client** | The client's name, or their nickname if no name is recorded. |
| **Level** | The grade your team set by hand: Level 1, Level 2, Level 3, VIP, or **Not graded**. |
| **Status** | Derived automatically from spend, visits and recency. You cannot set it by hand. |
| **Phone** | The client's number, or a dash when none is recorded. |
| **Last visit** | The date of their last counted visit, or **Never**. |
| **Visits** | How many times they have been counted in. |
| **Total spent** | Everything you have recorded against them. A figure that was never measured reads **not measured** rather than `0`. |

### How a status is decided

Status is worked out by the loyalty rule, not typed in:

- **Dormant** — no visit for 90 days or more. This takes precedence over everything else.
- **VIP** — more than LKR 50,000 spent **and** at least 5 visits.
- **Returning** — more than LKR 10,000 spent **or** at least 2 visits.
- **New** — everyone else.

If the rule decided the status rather than a person, the client's record shows **(tier
derived)** beside the level.

---

## Find a client

1. Type a name or phone number in the search box. The search runs on the server, so it covers
   your whole client book, not just the page in front of you.
2. Narrow by level with the **Filter by level** list: All levels, Level 1, Level 2, Level 3, VIP.
3. When you have more than 25 clients, the footer shows **Page 1 of N · N clients** with
   **Previous** and **Next**.

## Read a client's record

Click anywhere on a client's row to open their record. It shows their phone, email, level,
visit count, total spent and last visit, followed by every interaction your team has logged,
newest first. Each interaction says whether it counted as a visit or not.

Every interaction ends with one of two lines:

- **Counted as a visit** — the loyalty rule counted it.
- **Recorded, not counted as a visit** — it is on file but does not move the visit count.

## Add a walk-in client

Requires the Manager, Supervisor or Owner role.

1. Click **Add client**.
2. Fill in **Name**, and optionally **Phone** and **Nickname**.
3. Click **Add client**. The client appears at the top of the book.

Details worth knowing:

- A name is the only requirement. Everything else can be filled in later.
- If a client with that name is already on file, Aveline says **That client is already on file**
  and creates nothing. You are never given a second record for the same person.
- A phone number that another client already holds is refused, with the reason stated. Two
  clients cannot share an identity key.

## Log a visit or an interaction

Anyone who can open the Customers section can log a visit; no extra role is needed.

1. Open the client's record and click **Log a visit**.
2. Choose the **Channel** — In person, Phone, WhatsApp or Instagram.
3. Choose the **Direction** — Inbound or Outbound.
4. Set **When**. The server refuses a time in the future and refuses anything older than 30 days.
5. Optionally record the **Amount taken** and a **Note**.
6. Click **Record interaction**.

Two rules decide how it is counted:

- **Only an inbound, in-person interaction counts as a visit.** A phone call or an outbound
  message is recorded, but it does not move the visit count. The confirmation tells you which of
  the two happened.
- **No Blossoms are charged for a visit.**

An **Amount taken** joins the client's lifetime spend. Leave it empty when nothing was bought;
the dialog tells you exactly what the number you typed will do before you save.

## Edit a client

Requires the Manager, Supervisor or Owner role.

1. Open the client's record and click **Edit**.
2. Change **Name**, **Nickname**, **Phone**, **Email** or **Level**.
3. Click **Save changes**.

**Status is not editable.** The loyalty rule derives it from spend, visits and recency, so the
form says so rather than offering a control that cannot work. Two clients still cannot share a
phone number, and the same stated refusal appears here.

## Remove a client

Requires the Manager, Supervisor or Owner role.

Open the client's record and click **Remove**. There is no confirmation step, so this is a single
click — the record is hidden immediately. The confirmation reads **Client removed**, and their
orders stay readable.

Two things to expect:

- A client with orders that are still live is refused, with **This client has orders that are
  still live. Close or cancel them first.**
- Once removed, the record is indistinguishable from a client belonging to a different boutique:
  opening the old link shows **This client is not in this boutique.** That is deliberate. Aveline
  will not tell one boutique that a client exists somewhere else.

---

## Who can do what

| Action | Role needed |
|---|---|
| See the client book and open a record | Any role with Customers access (Staff, Manager, Supervisor, Owner) |
| Log a visit or interaction | Any role with Customers access |
| Add, edit or remove a client | Manager, Supervisor or Owner |

## When something is not on screen

| What you see | What it means |
|---|---|
| **No clients match this view.** | Nothing on file matches your search or level filter. A new walk-in appears here as soon as it is logged. |
| **Could not load the client book.** | The read failed. Press **Try again**. |
| **This client is not in this boutique.** | The client was removed, or the link names a client this boutique does not hold. |
| **No interactions recorded yet.** | The client's record is open and nothing has been logged against it. |

## Limits

- There is no export or import, and no bulk edit. Clients are added one at a time.
- There is no sorting control; the list is ordered by the server.
- **Tags** are shown on a record when they exist but cannot be added or removed here.
- The interaction list shows the most recent 20. The heading shows the client's full interaction
  count, which can be larger.
