# Income

**Income** is what your boutique took from its clients. It is your own takings register — separate
from what Aveline bills you, which lives under [Plan & Billing](/docs/billing).

Open it from the sidebar at `/app/b/<your-boutique>/income`. The section is available to a
**Manager**, **Supervisor** or **Owner**; a Staff account sees the reduced **Takings** card on the
dashboard instead.

---

## Two figures, kept apart

The section never shows a single "income" number, and that is deliberate. Money the shop has
evidence of collecting and value the orders say was sold are different facts, and adding them
together would hide which one you are looking at.

The **Reconciliation** banner always shows three figures side by side:

| Figure | What it means |
|---|---|
| **Collected** | Money a person or the payment flow confirmed was taken. |
| **Billed, unconfirmed** | What the orders say was sold. Not evidence that money moved. |
| **Refunded** | Already subtracted from Collected, and stated here so it is never invisible. |

Below them, when a gap exists, the banner says how much billed value has no confirmation yet and
states plainly that this is *a number to act on, not an error*. Any notes the server sent about
the window are listed underneath — for example that no payment rows exist yet, so the cash figures
come from counter sales only, or that the register contains entries repaired by the reconciliation
job and therefore begins at a date rather than covering your full history.

The badge at the top of the banner has three possible states:

| Badge | Meaning |
|---|---|
| **Every sale is confirmed** | The server checked and found nothing outstanding. |
| **Billed value awaiting confirmation** | Orders exist whose money has not been confirmed. |
| **Reconciliation status unknown** | No figures were returned for this window, so nothing is claimed about the reconciliation. |

**Open the register** scrolls down to the ledger.

## The register

Column by column:

| Column | What it shows |
|---|---|
| **When** | The date and time of the entry. |
| **Kind** | Sale, PaymentReceived, Refund or Adjustment. |
| **Basis** | **Money taken** for a confirmed entry, or **Billed, unconfirmed** for one the orders imply. |
| **Reason** | Why the entry exists. |
| **Amount** | The money, with `+` or `−` in front of it. |

The basis is always written as words, not signalled by colour alone, so a printout or a
colour-blind reader cannot mistake billed value for money taken. An entry that has been superseded
by a confirmed one is marked **(superseded)** and greyed out; it is voided rather than deleted, so
the money is never counted twice.

### Narrow the register

- **Search** matches the reason or the reference behind the entry.
- **Filter by kind** — All kinds, Sale, PaymentReceived, Refund, Adjustment.
- **Filter by basis** — All bases, Money taken, Billed, unconfirmed.

The totals and the reconciliation cover everything the filters select, not just the page you are
looking at, so a busy week is not understated by the page on screen. When the register holds more
than 50 entries, the footer shows **Page 1 of N · N entries · <amount> in sales this window**.

### What the window is

There is no date-range control on this page. The server chooses the window it measures, the
register covers that window, and the banner prints the dates it actually used — with
**(capped to the server limit)** when the period asked for was longer than the server allows.

## What else is on the page

The **By kind** card totals each kind separately, so a refund is never netted into a sale without
a label. It also lists the payment methods behind the money. When the window holds nothing, it
reads **No entries in this window yet.**

---

## Staff see a reduced view

A Staff account has no access to the register. Instead, the dashboard home carries a **Takings**
card showing the same two headline figures — **Collected** and **Billed, unconfirmed** — always
both, always labelled, with the note that they are never added together. If no takings were
measured for the window, the card says so rather than showing a zero.

## When something is not on screen

| What you see | What it means |
|---|---|
| **Could not load the income register.** | The read failed. Press **Try again**. |
| **The register is empty for this view.** | Nothing matches the current filters. A counter sale, a confirmed payment or a completed order appears here as soon as it happens. |
| **No entries in this window yet.** | The by-kind breakdown has nothing to total. |
| **not measured** | The server did not return that figure. It is not a zero. |
| **Reconciliation status unknown** | No figures came back, so no reconciliation is claimed. |

## Limits

- **No entry is edited or added from this page.** Boutique roles hold no permission to move money
  by hand; a mis-keyed counter sale is corrected by the reconciliation job and, if needed, by an
  Aveline-team adjustment.
- **No single total.** The register reports per basis and per kind, and there is no combined figure
  to misread.
- **No per-client or per-staff breakdown** beyond the payment-method summary.
- Takings entries come from the work itself: a counter sale recorded against a piece, a confirmed
  payment, or an order that reaches completed or delivered.
