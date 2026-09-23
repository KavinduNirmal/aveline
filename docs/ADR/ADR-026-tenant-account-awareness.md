# ADR-026: Tenant Account Awareness

## Status
Accepted

## Context

Aveline could answer questions about clients, the catalogue and orders, and — since
[ADR-025](ADR-025-handbook-knowledge-base.md) — questions about Aveline itself. She could not answer
a question about the boutique's **own account**: "How many Blossoms do I have left?", "How many
seats do I have left?", "How many customers do I have left?".

The figures existed and were already trustworthy. `IBlossomService.GetBalanceAsync` is described in
its own contract as "the authoritative balance projection for one period", and
`ISubscriptionService.GetEntitlementUsageAsync` already reports consumption against the plan's
`staff.max` and `customers.active.max` entitlements for the tenant dashboard's usage panel. Neither
reached any prompt.

Two of those questions already landed somewhere, and neither landing was right:

- **"How many Blossoms do I have left?" was a `aveline_help` question**, because
  `_AVELINE_HELP_PATTERNS` claims any mention of "blossom". It was answered from the handbook — the
  page that explains what Blossoms *are* — under a rule that forbids quoting Blossom amounts.
- **"How many customers do I have left?" matched nothing at all** and fell through to
  `general_inquiry`, which routes the customer-memory agent. Ava would have briefed on a customer,
  when the question is about a count of them.

Three constraints shaped the answer:

1. **These are the boutique's figures, and some of the askers are not the boutique.** The concierge
   answers inbound WhatsApp messages from customers and staff messages from the Salon through the
   same workflow. A customer asking "how many Blossoms do I have left?" must not be shown the
   tenant's balance. This is the first feature in the codebase where a wrong audience decision leaks
   money-adjacent business data rather than producing a bad answer.
2. **A number that disagrees with the dashboard is a bug, not a rounding difference.** This codebase
   already treats a second copy of a formula as a defect: `BlossomService.LedgerDerivedBalance` and
   `ReconciliationDrift` are called by both the system-metric collector and the admin console
   precisely "because a second formula here would let the console and the alarm disagree". The
   tenant's balance is rendered in three places already (web, mobile, admin); a fourth derivation
   would be a fourth chance to differ.
3. **Offline determinism is a hard guarantee.** With `agent_llm_enabled=false` or no key the workflow
   is fully rule-based, and CI depends on it.

One documented rule appears to conflict, and the resolution belongs on the record. ADR-025 and
`SYSTEM_PROMPT.md` state that the concierge "must not quote Blossom amounts or per-action costs",
and the handbook's `what-the-handbook-does-not-cover` page documents that unused Blossoms expire
while revealing no numbers. That rule is about **published** amounts — what a Blossom costs, what an
action is charged — which is marketing and pricing policy. A boutique's own balance is a different
thing: it is already shown to every boutique role on its own dashboard, gated by
`billing:view:self`. The owner asked for the balance to be answerable; the prompt rule is therefore
narrowed to published pricing rather than repealed, and the handbook's silence is untouched.

## Options Considered

### 1. Push the figures on every request, inside `org_context`
The API already pushes identity into `org_context` (`organization_id`, `customer_id`,
`direction`). Adding a `tenant` block would be the smallest transport change.
- **Pros:** the audience decision lands where authentication already lives; the agent never asks for
  what it should not have.
- **Cons:** every staff message carries a billing payload whether or not it is a billing question;
  `Modules/Conversations` would take a dependency on `Modules/Billing` to build it; and the figures
  would be as stale as the caller's snapshot. Rejected, but its best property — a fail-safe audience
  default — is kept (see Decision 1).

### 2. Pull the figures, gating on the existing staff heuristic
The agent service already pulls (`load_context`, `load_handbook`), and the codebase already has a
notion of "is this staff?": the commerce lane derives `staff_query` from `org_context`.
- **Pros:** no new wire field; consistent with the existing retrieval lane.
- **Cons:** that heuristic is **open by default** — an absent `direction` reads as staff, which is
  correct for the staff paths that send no direction, and correct-by-accident for anything that
  forgets to. Reusing an open-by-default heuristic for financial data means a future channel that
  omits a field leaks a balance. Rejected as the sole gate.

### 3. Declared audience plus pulled figures (chosen)
The API states the audience explicitly on both agent paths; the agent pulls the figures only when
that statement is present and affirmative.

### 4. The agent service reads the database directly
- **Pros:** no endpoint.
- **Cons:** the agent service has no domain ownership, no migrations and no tenancy model; ADR-009
  puts the API behind an internal token for exactly this reason. Rejected.

## Decision

### 1. The audience is declared by the API, never inferred by the agent
`ConversationService` sends `staff_query: true` from the staff paths (`TriggerAgentAsync`, reached by
`SendStaffNoteAsync`, `GetOrCreateSalonAsync` and `RegenerateAsync`) and `staff_query: false` from the
customer path (`TriggerInboundDraftAsync`).

The gate is `may_read_tenant_account`, which requires **explicit affirmation** — `staff_query is
True`. It deliberately does not reuse the `direction`-based derivations in the workflow's memory and
visual nodes, which read an absent direction as staff. Those heuristics are right for identity — they
decide whether a customer brief is wanted — and wrong for money, because they would open this lane
for any future caller that simply forgot to declare itself. The failure mode of a missing declaration
here is a missing answer, never a leaked balance.

(The two `direction` derivations already disagree with each other on `direction="outbound"`: the
memory node treats it as a customer message and the visual node as staff. That predates this decision,
is not touched by it, and is recorded here because it is exactly why a third derivation was not
introduced — see Consequences.)

### 2. The figures are pulled, not pushed
A new internal endpoint, `GET /internal/usage/tenant/{organizationId}`, behind
`InternalServicePolicy` (`X-Internal-Token`, ADR-009), joins the existing `/internal/usage` group.
The agent reads it from a `load_tenant_usage` node between `load_handbook` and `supervisor`, gated on
(a) the feature flag, (b) the audience, (c) the intent, and (d) the presence of an organisation.

This keeps `Modules/Conversations` free of a `Modules/Billing` dependency, keeps the payload free of
billing data on the overwhelming majority of turns that do not need it, and makes the figures as
fresh as the question rather than as fresh as the caller.

### 3. The snapshot reuses the authoritative projections; no figure is recomputed
`TenantUsageSnapshotDto` is composed from the services that already own the numbers: the Blossom half
is literally `BlossomBalanceDto.From(balance, threshold)`, the same projection the org balance route
returns, and the seat/customer half is `ISubscriptionService.GetEntitlementUsageAsync`.
`TenantUsageEndpointTests.TenantUsage_AgreesWithTheBillingSummaryTheMeterReads` pins the agreement,
and `..._ReportsTheStoredBalance_NotOneDerivedFromTheEntitlementLimit` uses an account whose stored
remainder differs from every derivable one (the entitlement limit, and the bare limit) so that a
future "simplification" to arithmetic fails loudly.

Entitlement usage is read **before** the balance, because the balance read creates the period account
on demand and asking it about an unknown organisation would leave a stray account row behind.

### 4. An account question is its own intent, and it outranks the handbook lane
`tenant_account` joins `IntentType`. It is checked before `is_aveline_help`, whose `\bblossoms?\b`
pattern would otherwise claim every balance question, and it is distinct from `aveline_help` rather
than folded into it because the two have genuinely different sources: documentation is *quoted* from
a corpus, an account is *read* from live data.

The discriminator is the shape of the ask, not the noun. "What is a Blossom?" and "how many seats
does the Orchid plan include?" are documentation; "how many Blossoms do I have left?" is an account.
A locating question — "where can I see my Blossom balance?" — stays in the handbook lane, because it
asks where a figure lives rather than what it is, and `_is_interface_location_question` encodes that.
`tests/test_handbook_retrieval.py` already asserted the locating case, and caught the first version of
this pattern claiming it.

### 5. The figures are enforced in code, not requested of the model
The snapshot is rendered as a `TENANT ACCOUNT` prompt block, and the model writes the wording — but
`_with_a_bounded_reply` keeps the reply only while **every numeral in it appears in that block**. The
comparison normalises thousands separators and trailing zeros, so `1,234.50` and `1234.5` are the
same figure, and a reply quoting anything else is discarded in favour of the deterministic answer
built from the snapshot, with a warning logged.

The guard is keyed on the **rendered block** rather than on the snapshot existing, which makes it
stronger than a numeral check: when there are no figures to show, the model may not word an account
answer at all — not even one carrying no numerals, because "you have none left" is a claim about the
account whether or not it contains a digit.

`_pin_authoritative_lane` supports this: a model that re-routes an account question out of the lane
is pinned back, because leaving the lane would leave the guard. Aveline keeps the voice; she does not
get to produce the number.

### 6. The offline path still answers
Unlike `load_handbook`, `load_tenant_usage` does not skip when no model is configured. The answer is
data, so the deterministic reply in `_tenant_reply` answers the question with no LLM, and
`_or_general_inquiry` deliberately does not degrade `tenant_account` the way it degrades
`aveline_help`.

### 7. The customer count travels with its basis
`customers.active.max` is measured over customers active in the last 90 days (an interaction, an
order, or a profile update), not over all customers. `customerCountBasis` carries that wording into
the prompt so an answer cannot report the count as "your customers".

## Consequences

- **No client change.** Both frontends already send staff messages through the staff path, and the
  reply is an ordinary `text` block. The web and mobile apps needed no edit.
- **The plan tier is deliberately not rendered.** The balance projection carries `planTier`, but it
  is the *period's* snapshot: `PlanTierSnapshot` is written by the billing rollover job
  (`LedgerJobs.cs`) and not by a mid-period plan change, so quoting it could name a plan the boutique
  has already left. It stays on the DTO, unrendered. Reporting the live plan needs a live read and is
  not part of this decision.
- **A model-invented audience still fails safe.** If the model is the first to classify a novel
  phrasing as `tenant_account` when the rules did not, no fetch happened, so the reply is the
  "couldn't reach your figures" admission rather than a guess. That is the intended trade: an
  occasional unhelpful answer in exchange for no invented balance.
- **`direction` remains an identity signal, and its two readers still disagree.** The workflow's
  memory node reads `staff_query` as "no direction", while the visual node reads it as "no direction,
  or `outbound`/`internal`". They therefore differ for `direction="outbound"`. That predates this
  decision, the commerce and visual lanes behave exactly as before, and the account gate does not
  depend on either — which is why no third derivation was added. Consolidating them is a follow-up,
  not a prerequisite.
- **The figures are stated, not editorialised.** `BlossomsAreLow` is computed server-side by the rule
  the clients already apply (`remaining <= allowance * threshold / 100`, where the allowance includes
  grants), so the agent carries no billing arithmetic. That rule now exists in three implementations
  (mobile, this DTO, and the alerting metrics); consolidating it is a follow-up, not a prerequisite.
- **Extension is a field, not a lane.** Anything else the boutique may legitimately ask about its own
  account — plan, renewal date, top-up packs, statement — is a new field on the snapshot, a new line
  in the renderer, and a new pattern. The audience decision and the numeral guard cover it unchanged.

## Related

- [ADR-009](ADR-009-internal-service-authentication.md) — the internal token this endpoint sits behind
- [ADR-010](ADR-010-usage-tracking-architecture.md) — Blossom accounting and the balance projection
- [ADR-017](ADR-017-memory-pgvector-embeddings.md) — the precedent for reading data the EF model does not own
- [ADR-023](ADR-023-conversation-context-and-supervisor.md) — the supervisor whose reply this composes
- [ADR-025](ADR-025-handbook-knowledge-base.md) — the documentation lane this is deliberately separate from
- [`docs/architecture/tenant-awareness.md`](../architecture/tenant-awareness.md) — the operational description
