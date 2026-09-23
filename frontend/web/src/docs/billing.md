# Plan & Billing

**Billing** is your boutique's account with Aveline: the plan you are on, the limits it grants,
what each period consumed, and a statement of every Blossom grant and deduction.

Open it from the sidebar at `/app/b/<your-boutique>/billing`. The section is available to a
**Manager** or **Owner**; a Staff or Supervisor account does not see it and is sent back to
Overview if the address is typed by hand.

---

> **This is a statement of account.** No payment provider is connected to Aveline, so nothing on
> this page is a demand for payment. There is no invoice document and no invoice number, and plan
> list prices are shown as *LKR list prices only*.

## Your plan

The **Plan** card carries four facts:

| Fact | What it shows |
|---|---|
| **List price** | Your plan's LKR list price, or **no list price configured** when none is on file. A missing price is never drawn as `LKR 0.00`. |
| **Seats included** | How many staff seats the plan allows. |
| **Period start** / **Period end** | The current billing period. |

Beside the plan name is a status badge. Each one means something specific:

| Badge | Meaning |
|---|---|
| **Active** | This boutique has a billing record for the current period. |
| **Trial** | A trial period is running; no charge is recorded during it. |
| **Past due** | A period payment is outstanding. No payment provider is connected, so nothing is collected automatically. |
| **Cancelled** | The subscription is cancelled and will not renew at the period end. |
| **Expired** | The subscription period ended without a renewal. |
| **No billing record** | No subscription row exists for this boutique yet. The plan and its limits come from the assigned tier, and nothing is charged. |

## Plan entitlements

**Plan entitlements** is the authoritative list of the limits your boutique has right now, with
the source each one resolved from and the date it took effect. Limits include the monthly Blossom
allowance, staff seats, active clients, API requests per month and per minute, WhatsApp messages
per month, and how long your statistics are kept.

This table, not the plan name, is what your account is actually operating under. A value the
server did not return reads **not measured**.

## Billing periods

**Billing periods** lists the most recent periods, newest first. Every column is reported
separately, so an adjustment is never folded invisibly into a grant:

**Period** (with an **open** badge while the period is running) · **Plan** · **Limit** ·
**Granted** · **Adjusted** · **Used** · **Remaining** · **Top-ups** · **List price**.

A period with no subscription row says **no subscription row** rather than naming a plan, and a
period whose list price is not configured shows **not configured**.

## Blossom statement

The **Blossom statement** is the ledger behind your balance. Each row carries **When**, **Entry**,
**Delta**, **Balance after** and **Expires**, and the header summarises the period as
*Opening n · closing n*. When the window the server measured is shorter than the one you asked
for, the statement says **window capped at n days**.

At the top of the statement is a reconciliation pill. It has three states and never collapses
them:

- **reconciled** — the server checked and the ledger agrees.
- **reconciliation status unknown** — no check was performed. Aveline says so rather than claiming
  a reconciliation nobody ran.
- **drift** — the check found a difference.

The statement is paginated with **Previous** and **Next** when there are more entries than fit on
a page.

## Top up Blossoms

Requires the **Owner** role.

1. Click **Top up Blossoms**.
2. Choose a **Pack**. Each option shows its Blossoms and its price, read from your boutique's price
   book, so a pack you are offered is a pack you can buy.
3. Optionally add a **Payment reference** — for example a transfer or provider reference. Without
   one the grant writes no revenue row, because a free grant is not revenue.
4. Click **Record the top-up**.

> **A top-up is a recorded grant, not a charge.** No payment provider is connected, so the
> reference you enter is the only evidence of payment Aveline stores.

The **Top up** button in the dashboard header takes you to this section as well. It is only useful
to a role that can open Billing.

## Change your plan

Plan changes are arranged with the Aveline team rather than applied instantly: staff seats, the
monthly Blossom allowance and the billing cycle all move together, and the effective date is
confirmed with you before anything changes. Use [Contact](/contact) to start one.

Usage and Billing each carry an **Upgrade plan** button. As shipped that button does not open a
plan-change page — it returns you to Overview. Contact is the reliable route today.

## The plans offered at setup

The setup wizard proposes four plans, with these monthly figures. The **Plan entitlements** card
above is always the authoritative version for your boutique, and quoted prices are LKR list
prices.

| Plan | Price | Blossoms per month | Staff seats | Customers |
|---|---|---|---|---|
| **Seed** | Free | 150 | 1 | 50 |
| **Bloom** | LKR 3,500/mo | 750 | 3 | 250 |
| **Orchid** | LKR 9,000/mo | 2,000 | 10 | 1,000 |
| **Rose** | LKR 20,000/mo | 5,000 | 25 | 5,000 |

## Who can see this section

| What | Role needed |
|---|---|
| Open Billing | Manager, Owner |
| Top up Blossoms | Owner |

Blossoms themselves are visible to every role from the [Usage](/docs/usage) section and the
dashboard header.

## When something is not on screen

| What you see | What it means |
|---|---|
| **Could not load the billing data.** | The read failed. Press **Try again**. |
| **No entitlements are recorded for this plan, so no limits are shown rather than assumed.** | No limits came back for the plan. |
| **No billing periods are recorded yet.** | No period history exists for this boutique. |
| **No ledger entries in this window.** | No Blossom movement in the selected window. |
| **No top-up packs are configured in the price book, so nothing can be purchased.** | No pack is on file, so the dialog cannot offer one. |
| **The top-up could not be recorded. Nothing was granted.** | The grant did not write. Nothing changed. |

## What billing does not do

- **It does not take payment.** No payment provider is connected, no checkout exists, and no card
  is collected.
- **It does not produce an invoice document**, because there is no payment to bill.
- **It does not show who spent the Blossoms.** Aveline does not record which person or client an
  AI action belongs to, so the statement has no per-user column.
- **It does not change your plan by itself.** Plan changes are arranged with the Aveline team.
