# Tenant Account Awareness

> **Status:** Accepted (see [ADR-026](../ADR/ADR-026-tenant-account-awareness.md)).

This document describes how Aveline answers a question about the boutique's **own account** — "How
many Blossoms do I have left?", "How many seats do I have left?" — where those figures come from, who
is allowed to see them, and what stops a number from being invented.

---

## 1. The path

```
load_context                 (fetches + compacts the transcript)
      │
      ▼
load_handbook                (documentation, for a platform question)
      │
      ▼
load_tenant_usage            (live figures, for an account question)
      │
      ▼
supervisor                   (composes Aveline's reply from whichever it was given)
```

`load_tenant_usage` (`app/workflows/concierge_workflow.py`) runs between `load_handbook` and
`supervisor` and returns one state field, `tenant_usage`, holding the backend's snapshot or `None`.
Four gates must all pass before a single byte is fetched:

| Gate | Rule |
|---|---|
| Feature | `tenant_awareness_enabled` (`app/core/config.py`) |
| Audience | `may_read_tenant_account(org_context)` — `staff_query is True`, explicitly |
| Relevance | `classify_by_rules(message).intent_type == "tenant_account"` |
| Tenant | `organization_id` (or `org_id`) is present |

The node is best-effort like its neighbours: a fetch failure leaves `tenant_usage` as `None` and the
run proceeds, because losing the figures degrades an answer but must never prevent one.

---

## 2. The audience is declared, not inferred

The concierge answers two kinds of request through one workflow: inbound WhatsApp messages from
**customers**, and Salon messages from **staff**. Only the second may see the boutique's balance.

The `.NET` API states which it is, on both paths
(`Aveline.Api/Modules/Conversations/Services/ConversationService.cs`):

| Path | Method | `staff_query` |
|---|---|---|
| Salon note, regeneration, agent brief | `TriggerAgentAsync` | `true` |
| Inbound WhatsApp | `TriggerInboundDraftAsync` | `false` |

The agent's gate requires **explicit affirmation** — `org_context.get("staff_query") is True` — and
deliberately does *not* reuse the `direction`-based derivations in the workflow's memory and visual
nodes. Those read an absent direction as staff, which is right for identity (deciding whether a
customer brief is wanted) and wrong for money: a channel that forgets to declare itself would open
the account lane. With the strict gate, the failure mode of a missing declaration is a missing
answer.

The two existing derivations already disagree with each other on `direction="outbound"` — the memory
node treats it as a customer message, the visual node as staff. That predates this work and is
untouched; it is recorded here because it is the reason a *third* derivation was not introduced.
Consolidating them is a follow-up.

---

## 3. Where the figures come from

`GET /internal/usage/tenant/{organizationId}`, behind `InternalServicePolicy` (ADR-009), joining the
existing `/internal/usage` group. Nothing is recalculated:

- **Blossoms** — `IBlossomService.GetBalanceAsync`, whose own contract calls it "the authoritative
  balance projection for one period", serialised through the same `BlossomBalanceDto` the boutique's
  Blossom meter renders. `blossomRemaining` is the reconciled remainder, which is **not**
  `limit - used` and **not** the entitlement limit minus usage: grants and adjustments move it.
- **Staff seats and customers** — `ISubscriptionService.GetEntitlementUsageAsync`, the same
  observed-versus-allowed read the dashboard's usage panel uses. The endpoint assembles `remaining`
  from it (`limit - used`, floored at zero), because the entitlement layer reports a percentage
  rather than a remainder.
- **`blossomsAreLow`** — computed server-side by the rule the clients already apply
  (`remaining <= allowance * threshold / 100`, where the allowance is the monthly limit plus grants
  less adjustments), so the agent carries no billing arithmetic.

Entitlement usage is read *before* the balance on purpose: the balance read creates the period
account on demand, so asking it about an unknown organisation would leave a stray account row.

`blossoms.planTier` is intentionally not rendered into any prompt. It is the billing period's
snapshot, written by the rollover job and not by a mid-period plan change, so quoting it could name a
plan the boutique has already left.

---

## 4. What the model is given, and what it may say

`render_tenant_block` (`app/context/__init__.py`) renders a `TENANT ACCOUNT` prompt section holding
the figures as data, marked as authoritative and as *data rather than instruction* — the same rule
the conversation window and the handbook carry. The block is unrendered (empty string) when there are
no figures, which leaves the supervisor's prompt byte-identical to one assembled with no account data
at all.

Aveline writes the wording; she does not produce the number. `_with_a_bounded_reply` (`app/gate.py`)
keeps her reply only while **every numeral in it appears in the rendered block**. The comparison
normalises thousands separators and trailing zeros, so `1,234.50` and `1234.5` are the same figure.
A reply quoting anything else is discarded in favour of the deterministic reply built from the
snapshot, and a warning is logged. `_pin_authoritative_lane` completes the guarantee: a model that
re-routes an account question out of the lane is pinned back, because leaving the lane would leave the
guard.

The guard is keyed on the rendered block rather than on the snapshot existing, which is stronger than
a numeral check: with no figures to show, the model may not word an account answer at all — not even
one carrying no numerals, because "you have none left" is a claim about the account whether or not it
contains a digit.

Three ways a reply can be produced, and none of them can invent a figure:

| Situation | Reply |
|---|---|
| Model available, figures shown | The model's wording, if every numeral it used is in the block |
| No model, or the model's wording was rejected | `_tenant_reply(snapshot)` — the figures in Aveline's voice |
| Figures could not be fetched | `_TENANT_UNAVAILABLE_REPLY` — an admission, not a guess |
| The asker is not staff | `_TENANT_NOT_STAFF_REPLY` — says nothing about the account |

The offline path is *not* skipped here, unlike `load_handbook`: the answer is data, so
`_or_general_inquiry` deliberately does not degrade `tenant_account` the way it degrades
`aveline_help`.

---

## 5. Which questions belong here

`tenant_account` is checked **before** `is_aveline_help`, whose `\bblossoms?\b` pattern claims any
mention of the word. The discriminator is the shape of the ask, not the noun:

| Message | Intent | Why |
|---|---|---|
| "How many Blossoms do I have left?" | `tenant_account` | Asks for the asker's own position |
| "How many seats have I got?" | `tenant_account` | Same |
| "How many customers can I add?" | `tenant_account` | Same |
| "What is a Blossom?" | `aveline_help` | Documentation; the handbook answers it |
| "How much does a Blossom cost?" | `aveline_help` | Published pricing is the handbook's, and it does not publish it |
| "How many seats does the Orchid plan include?" | `aveline_help` | A plan fact, not this account |
| "Where can I see my Blossom balance?" | `aveline_help` | *Locating* the figure, not asking for it |

`_is_interface_location_question` encodes that last row. Without it the possessive patterns claim
both readings, and `tests/test_handbook_retrieval.py` — which already asserted the locating case —
caught the first version of the pattern doing exactly that.

---

## 6. What does not change

- **No client change.** Both frontends already send staff messages through the staff path, and the
  reply is an ordinary `text` block. No wire format, DTO or OpenAPI contract on the user-facing API
  moved.
- **No new dependency in `Modules/Conversations`.** The audience flag is a boolean on an existing
  payload; the figures are pulled by the agent service.
- **The handbook's silence is untouched.** "No published amounts" still holds; what changed is that
  the boutique's *own* balance is now answerable, which its dashboard already shows every role.

---

## 7. Limits and follow-ups

- **Total customers versus active customers.** `customers.active.max` counts customers active in the
  last 90 days (an interaction, an order, or a profile update), not the customer book. The snapshot
  carries `customerCountBasis` and the block repeats it, so an answer cannot report the count as
  "your customers". A boutique wanting its total is asking a different question this lane does not
  answer.
- **A model-first classification fails safe.** If the model is the first to read a novel phrasing as
  an account question, no fetch happened, so the reply is the "couldn't reach your figures"
  admission. The alternative — fetching on every staff turn to cover the case — pays for a query the
  model usually will not use.
- **The low-balance rule now exists in three places** (mobile, this DTO, the alerting metrics).
  Consolidating it is a follow-up.
- **Extension is a field, not a lane.** Plan, renewal date, top-up packs and the statement each add a
  field to the snapshot, a line to the renderer, and a pattern. The audience decision and the numeral
  guard cover them unchanged.

---

## 8. Related

- [ADR-026](../ADR/ADR-026-tenant-account-awareness.md) — the decision and the alternatives
- [ADR-009](../ADR/ADR-009-internal-service-authentication.md) — the internal token
- [ADR-010](../ADR/ADR-010-usage-tracking-architecture.md) — Blossom accounting and the balance
- [ADR-025](../ADR/ADR-025-handbook-knowledge-base.md) — the documentation lane this sits beside
- [`handbook.md`](./handbook.md) — retrieval, for the questions this lane deliberately does not answer
- [`agent-context.md`](./agent-context.md) — the conversation window, the third context layer
