# ADR-013: Notification Service Architecture

## Status
Accepted

## Context

Aveline needs to notify users across several event types (new WhatsApp message,
approval needed, payment confirmed, VIP at-risk, event reminder, new visual match).
Recipients vary per type (all staff in an org, manager/owner, a specific associate, the
owner) and channels vary per recipient (Flutter push via FCM, React/Flutter realtime via
SignalR, email). The catalog is expected to grow, so the delivery infrastructure must be
type-agnostic and extensible without rewriting dispatch code.

## Options Considered

### 1. Notification Gateway + Channel Adapters (chosen)
A single typed `Notification` (type, title, body, data payload, target intent, requested
channels) is fanned out by an `INotificationDispatcher` through pluggable channel
adapters (`IPushChannel`, `IRealtimeChannel`, `IEmailChannel`). A recipient resolver
turns a target intent into concrete users; a channel router narrows requested channels to
those each user is eligible for.

- Pros: New notification types = new enum value + resolver rule. New channels = new
  adapter. Delivery code is stable. Fully unit-testable with logging demo adapters before
  real providers land.
- Cons: Slightly more indirection than direct per-type send calls.

### 2. Per-type notification services (e.g. `IPaymentNotificationService`)
One interface per event type, each calling channels directly.

- Pros: Very explicit call sites.
- Cons: Duplicated routing/persistence logic; every new type adds a new service; channel
  changes ripple across many services.

### 3. Direct SignalR/FCM calls at trigger sites
Trigger code (webhooks, agents) calls SignalR/FCM directly.

- Pros: Minimal abstraction.
- Cons: Tight coupling of business triggers to delivery transports; no audit trail; hard
  to test; no per-user preference/eligibility handling.

## Decision

1. **Notification Gateway + Channel Adapters.** A `Modules/Notifications` module exposes
   `INotificationDispatcher.DispatchAsync(Notification)`. The dispatcher resolves
   recipients, routes channels, sends best-effort, and persists the notification plus one
   delivery row per (recipient, channel) attempt.
2. **Recipient resolution is intent-based.** `NotificationTarget` carries an org id, an
   optional role filter, and an optional specific user id. `OrganizationRecipientResolver`
   resolves it against active `OrganizationMembership` rows (reusing the canonical
   `org:boutique_*` roles).
3. **Channel routing is per-user.** `ChannelRouter` narrows requested channels: realtime
   is always eligible; push requires opt-in plus a registered device token; email requires
   an email contact preference.
4. **Delivery is best-effort.** A failure in one channel is logged and recorded as a
   failed delivery but never aborts other channels or the caller (mirrors the usage-tracker
   non-fatal pattern, ADR-010).
5. **Persistence.** `NotificationRecord` (audit/inbox) + `NotificationDelivery` (per
   recipient/channel outcome) are written by the dispatcher.
6. **Channels are swappable.** Initial registrations are logging demo adapters. SignalR
   (realtime) and FCM (push) adapters replace them in later slices without touching the
   dispatcher. Email reuses the existing `IEmailService` abstraction.
7. **Triggers are out of scope for the gateway.** WhatsApp webhooks, agent pause events,
   payment webhooks, and scheduled checks call the dispatcher later; the gateway is
   trigger-agnostic.

## Consequences

- A `Notifications` vertical-slice module is introduced following the module pattern
  (ADR-001).
- New notification types require only an enum value + a resolver rule + a factory helper.
- New channels require only a new adapter behind a channel interface.
- The dispatcher is fully testable without external providers via logging demo adapters.
- A Redis SignalR backplane for horizontal scale-out is deferred (single instance now).
- Device-token storage and FCM/SignalR adapters land in follow-up slices (#90, #91).
