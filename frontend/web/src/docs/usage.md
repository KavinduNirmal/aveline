# Blossoms and Usage

A **Blossom** is Aveline's unit of AI work. Instead of asking you to read tokens, model calls or
provider costs, Aveline converts everything its agents do for your boutique into Blossoms. Your
plan grants a monthly allowance, and the **Usage** section shows what has been spent from it.

Open it from the sidebar at `/app/b/<your-boutique>/usage`. Every role can open this section; what
you see inside depends on your role.

---

## The balance

The **Blossom balance** card is the first thing on the page. It shows:

- **Blossom balance** — how many Blossoms remain, with the allowance underneath.
- **Used** — how many have been consumed this period.
- **Allowance**, **Granted**, **Adjusted** and **Used** — the four parts of the current period's
  position, so a grant is never folded invisibly into an allowance.
- A progress bar, and the word **closed** when the period has ended.

If the server did not report a balance, the card says so in words — *no percentage is shown rather
than a fabricated one* — and the header chip reads **Balance unavailable**. A missing figure is
never drawn as `0`.

## What else is on the page

| Panel | Shows |
|---|---|
| **Usage against plan limits** | Every limit your plan grants and how much of it this period has used. Non-hard limits are marked **soft**. |
| **Burn rate** | Blossoms consumed per day, the average across the window, and the projected exhaustion date when the server can project one. |
| **Blossom consumption** | Daily consumption across the window, drawn against your allowance line. A gap in the data stays a gap. |
| **API consumption** | Requests, errors, throttled calls, p95 latency, and the quota for the period. |

One reporting window in the header drives the whole dashboard. Choose **Last 7 days**, **Last 30
days**, **Last 90 days**, **This month** or **This year**, and every panel moves together.

### Who sees which panel

| Panel | Roles that see it |
|---|---|
| Blossom balance | All roles |
| Usage against plan limits, Burn rate, Blossom consumption | Manager, Owner |
| API consumption | Supervisor, Manager, Owner |

A Staff account sees the balance and a line saying that the detailed panels are available to a
manager or owner. A Supervisor sees the balance and the API panels. Nothing is hidden by accident:
a panel your role cannot read is simply not fetched, and the page says so rather than showing you
an empty box.

## Figures that were not measured

Every figure in this section follows one rule: **`0` is a measurement, and a blank is not.**

- A limit the server did not report reads **not measured**.
- **WhatsApp messages per month** is the one limit with no meter behind it. Its observed column
  reads **not measured** and its usage column reads **no outbound send log exists**. Aveline
  records inbound WhatsApp messages, not outbound sends, so any figure there would be invented.
- A consumption chart with no points at all reads *not measured — no consumption points were
  recorded in this window* instead of drawing an empty axis that looks like a quiet month.
- A projection the server did not make reads **not projected**.

## What is not here

- **No agent internals.** Runs, tokens and provider cost are how Aveline works, not what your
  boutique buys. There is no agentic panel, and no route behind one.
- **No per-person Blossom split.** Aveline does not record which staff member or client an AI
  action belongs to, so there is no "who spent the Blossoms" chart to show.
- **No self-serve plan change** — see [Plan and Billing](/docs/billing).

## When something is not on screen

| What you see | What it means |
|---|---|
| **Could not load the usage data.** | The read failed. Press **Try again**. |
| **Balance unavailable** | The server did not return a balance for this period. The figure is unknown, not zero. |
| **Detailed billing panels are available to a manager or owner.** | Your role sees the balance only. |
| **No entitlement limits are recorded for this plan.** | No limits came back for the plan. |
| **No quota is recorded for this plan.** | No API quota came back for the period. |
