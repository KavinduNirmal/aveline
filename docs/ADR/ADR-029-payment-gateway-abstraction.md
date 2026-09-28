# ADR-029: Payment gateway abstraction — a provider-neutral intent, one SPI, and verified settlement

## Status

Accepted (Phase 6 of `.agents/plans/payment-gateway-abstraction-implementation.ignore.md`).
Amended at Phase 10 (2026-09): a provider dispute is no longer an `Unknown` event — it has an explicit
type and an append-only Revenue reversal — and the agent-service payment tools no longer fabricate a
settlement or a checkout link.

## Context

There was no payment gateway in Aveline. Instead there were three unrelated simulations of payment:
a Commerce order-payment path whose "gateway" was a hardcoded URL literal and whose confirmation
trusted any non-empty `GatewayTransactionId`; a Python agent tool that returned `is_settled: True`
unconditionally; and an operator top-up path that recorded revenue as `Derived` because "a reference
is evidence of a session, not proof of settlement". No `IPaymentProvider` existed, no provider charge
was stored, and `OrganizationSubscription.ExternalProvider` / `.ExternalSubscriptionId` were declared
and never written.

The repository had already committed to the shape of the change: `Rules.md` §5 fixes the dependency
direction ("Infrastructure must implement abstractions defined by the appropriate inner layer"),
`ADR-010` §Decision 6 defers payment processing and is a registered project risk, and
`docs/api/README.md` promised that "Phase 3 attaches a payment provider; until then a top-up is a
recorded grant, not a charge". The revenue ledger deduplicates income entries on a filtered unique
`(SourceKind, SourceRef)` index, so a second write of the same reference raises `409
duplicate-revenue-entry` — which makes the `Derived -> Verified` supersede path load-bearing, and
that path lived inside an endpoint rather than a service.

## Options Considered

1. **Do nothing beyond documenting the simulations.** Zero cost, but the deliverable — a
   demonstrable payment flow behind a swappable provider — is unmet.
2. **One `IPaymentGateway` implemented by a mock and an HTTP adapter, called from the existing
   endpoints in place.** Small, but it cannot express atomic settlement, an idempotent replay, or a
   refund that finds its receipt. Every money rule becomes endpoint-local ad hoc code, which is the
   current problem restated.
3. **A `Modules/Payments/` module with a provider SPI, a persisted provider-neutral intent, a webhook
   inbox, and an orchestration service; adapters in `Providers/`, every flow migrated phase by
   phase.** The abstraction is the *intent*; the provider is a dependency of the intent, not of the
   feature. Chosen.
4. **Implement Stripe first and derive the interface from it.** Inverts the requirement (mock first,
   external provider swappable later), adds an external dependency and PCI-adjacent review before any
   demonstration, and derives the interface from one vendor's model — the failure mode that makes a
   later swap expensive.

## Decision

**D1 — The abstraction is a persisted, provider-neutral intent, not a method call.** `PaymentIntents`
models what Aveline needs to know; the provider is a dependency of the row. Consequence: every future
flow must create an intent before it charges.

**D2 — One provider interface with a `Capabilities` record, not two interfaces.** A provider that
cannot honour a call throws `PaymentProviderNotSupportedException` (501) and callers must consult
`Capabilities` first. This keeps "swap a provider by implementing one interface" true while making a
missing capability loud rather than silent.

**D3 — Money crosses the provider boundary as integer minor units; the domain keeps `decimal`.** One
conversion site (`Money.Lkr`); the intent stores `AmountMinor` and the list `PriceLkr` it was struck
from.

**D4 — Settlement writes `Verified`, not `Derived`, when a provider confirms.** A webhook-verified
settlement is a receipt; the pre-existing operator route keeps writing `Derived` and keeps working.
The two classes of entry are genuinely distinguishable in the ledger.

**D5 — Provider selection uses keyed DI, resolved by the key stored on the intent.** `Resolve(key)`
means flipping the configured provider does not orphan open intents or make in-flight webhooks
unprocessable.

**D6 — The mock is a first-class adapter, not a test double.** A real `IPaymentProvider`, registered
only when `Payments:Mock:Enabled` is true, gated to Development by startup validation, with a gauge a
Grafana alert can fire on. Six independent anti-production guards, because one is not enough.

**D7 — The existing operator top-up route keeps working unchanged and stays the `manual` path.** The
new checkout route is additive; the shipped `POST /blossoms/top-ups` contract, its `Derived` income
row, and the admin console that calls it are untouched.

**D8 — Refunds are two-step: the provider call precedes the ledger write.** The ledger never records a
refund a provider refused. Phase 6 adds three refinements in the same spirit:

- **Cancellation is provider-first.** `CancelAsync` calls
  `IPaymentProvider.CancelSubscriptionAsync(id, atPeriodEnd: true)` before persisting anything; a
  refusal is a hard `502 payment-provider-error` and **no** cancellation is written, because a
  cancellation that never reached the provider is the worst outcome — the tenant keeps being charged
  while Aveline believes the subscription is ending. The rollover then honours `CancelAtPeriodEnd`:
  the subscription moves to `Cancelled`, the closed period's `Derived` charge is not written, and
  nothing is renewed. The un-cancel route (`POST /subscription/resume`) is gated on
  `Capabilities.SupportsCancelAtPeriodEnd` and answers `501 payment-provider-capability-missing`
  where the provider cannot be asked.
- **The `Derived -> Verified` supersede path is a service, not an endpoint.** It is extracted into
  `IIncomeLedgerService.VerifyAsync(VerifyIncomeCommand)`, so the admin verify route, the top-up
  webhook settlement and the proration settlement share one implementation. The refund route keeps
  the shipped rule — a refund requires a live `Verified` receipt for the same
  `(SourceKind, SourceRef)` — and the refund window is configuration
  (`Payments:RefundWindowDays`, absent by default) because the policy is unresolved.
- **An unknown provider event is a fail-safe, not a no-op.** A type with no handler maps to
  `PaymentWebhookEventType.Unknown` and is stored **unprocessed** with a `ProcessingError` and
  logged, so reconciliation surfaces it rather than forgetting it. Phase 10 narrowed this: a provider
  **dispute/chargeback** now has an explicit `DisputeOpened` type and a known effect — an append-only
  `Verified` `Refund` reversal recorded through `IIncomeLedgerService`, keyed
  `payment-dispute:{providerIntentId}` so a second reversal is refused rather than double-counted.
  The settled charge, its Blossom grant and its receipt are never mutated, and the intent stays
  `Succeeded` because the charge did succeed. Anything still without a handler keeps the fail-safe.
  Phase 10 also removed the agent-service fabrications: `validate_payment` returns the server's
  status or an explicit `unknown`, and `generate_payment_request` raises rather than minting a link
  that leads nowhere.

## Consequences

- A new external provider is one class in `Providers/`, one registration line, one configuration
  block: no change to `SubscriptionService`, `BlossomService`, `IncomeLedgerService`, any endpoint,
  any DTO or the schema.
- The ledger's `(SourceKind, SourceRef)` identity is the settlement dedup identity; the intent's
  `(Provider, ProviderIntentId)` and the inbox's `(Provider, ProviderEventId)` are two more layers,
  so a replayed webhook cannot double-grant.
- `revenueProviderSettlementAvailable` reports whether the configured provider can itself settle
  money, and the pre-existing operator top-up route remains the honest `manual` path.
- Money policy that is not settled (the refund window) is deliberately left unresolved and surfaced
  as configuration, rather than decided by a hardcoded constant. The dispute effect **is** decided
  (an append-only reversal, Phase 10); the refund window is not, because the consumer-protection
  review it needs has not happened.
- The mock remains the offline demonstration and test double in every environment except Production.
