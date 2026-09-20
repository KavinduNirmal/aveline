/**
 * The fifth `dataQuality` vocabulary: money.
 *
 * The console carries four vocabularies — system (`lib/admin/data-quality.ts`), agent, api and
 * business (`businessDataQuality`) — and `admin-console.md` states the rule that keeps them
 * honest: they are never coerced into one another. Money needs its own because **none of the
 * four existing flags is true of it**: attribution and backfill say nothing about whether a
 * price was configured, and nothing about whether a receipt was ever verified.
 *
 * The distinguishing facts a reader would otherwise have to assume, and which this vocabulary
 * exists to state:
 *
 * - **No payment provider is wired.** `OrganizationSubscription` and `BlossomPriceEntry` carry
 *   provider columns that nothing writes, and `docs/api/README.md` records the position:
 *   *"Phase 3 attaches a payment provider; until then a top-up is a recorded grant, not a
 *   charge."* So no figure here is settled money.
 * - **Every subscription currently has `PriceLkr = 0`.** `SubscriptionService` never assigns
 *   the price, so a derived charge of `0` means *no list price is configured* — it does not
 *   mean free, and it must never enter MRR.
 * - **Derived and verified are different numbers.** The gap between what list price says should
 *   be billed and what an operator confirmed was collected is the most important figure on the
 *   surface, and it is not an error.
 *
 * The field set is pinned by `revenue-quality.test.ts` so this vocabulary cannot silently grow
 * or collapse back into `BusinessDataQuality`.
 */

/** The exact field names of the income data-quality contract, in C# declaration order. */
export const INCOME_QUALITY_FIELDS = [
  /** `false` until a provider client settles money; every figure is then an expectation. */
  "revenueProviderSettlementAvailable",
  /** `false` when every subscription's `PriceLkr` is `0`, so MRR is not measurable. */
  "subscriptionPricesConfigured",
  /** Count of `Derived` rows with no `Verified` counterpart — the uncollected gap. */
  "derivedEntriesUnverified",
  /** When these flags were evaluated, distinct from the window's `to`. */
  "checkedAt",
  /** Free-text notes, including the cache-degradation note. */
  "notes",
] as const
