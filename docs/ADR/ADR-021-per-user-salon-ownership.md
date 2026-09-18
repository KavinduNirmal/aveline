# ADR-021: Per-User Ownership of the General Salon

## Status
Accepted — supersedes decision 1 of [ADR-016](ADR-016-conversation-inbox.md) in part.

## Context

ADR-016 decided that the Salon is **one unified conversation per organization**: a single
`Conversation` (kind `Salon`, `CustomerId == null`) shared by every member of the boutique.

That model leaks conversations between staff. The general Salon is a personal concierge thread:
a staff member asks Aveline "what should I follow up on?", and the answers, drafts, and customer
lookups raised in that thread are personal to them. Because the row was keyed only on
`OrganizationId + CustomerId + Kind`, every member of the organization resolved to the **same**
row:

- `ConversationRepository.GetOrCreateSalonAsync` matched on `OrganizationId`, `CustomerId` and
  `Kind` only.
- `ConversationService.GetOrCreateSalonAsync` accepted a `userId` and never used it.
- `Conversation` had no owner column at all.

Observed in the Flutter app: signed in as a staff member, the Salon displayed the owner's
thread, including the owner's messages and agent context.

Three further exposures made a get-or-create fix insufficient on its own:
`ConversationRepository.ListAsync` returns every conversation in the organization, `GetAsync`
returns any conversation by id, and `ConversationHub.JoinSalon` admits any active organization
member to `salon:{conversationId}`.

ADR-016's non-fragmentation rationale still holds for **customer** threads: a customer's
history, memory, and open drafts are organization property, and splitting them per staff member
would give the same customer a different agent context for each colleague.

## Options Considered

1. **Per-user general Salon; customer Salons stay organization-shared (chosen).**
   The general Salon is owned by one user (`OrganizationId + OwnerUserId`). Customer-bound
   Salons remain organization-wide, preserving Ava's shared customer context.
2. **All conversations per-user.** Strongest isolation, but the same customer gets a separate
   agent thread and memory context per staff member, contradicting ADR-016's rationale.
3. **Keep one organization-wide Salon and document it.** No schema change, but staff continue
   to read each other's concierge threads; the privacy defect remains.

## Decision

1. `Conversation` gains a nullable `OwnerUserId`.
2. The **general** Salon (`Kind == Salon`, `CustomerId == null`) is keyed on
   `OrganizationId + OwnerUserId`. Its `OwnerUserId` is always set.
3. **Customer-bound** and **channel** Salons (`CustomerId != null`, or created from an inbound
   `ExternalRef`) keep `OwnerUserId == null` and remain organization-shared.
4. `ListAsync` returns shared Salons plus the caller's own general Salon.
5. `GetAsync` refuses to return another user's general Salon.
6. `ConversationHub.JoinSalon` admits the caller only for a shared Salon or their own.
7. Existing general Salons are backfilled to `Organization.OwnerUserId`, preserving the owner's
   history.

## Consequences

- An EF migration adds `Conversations.OwnerUserId`, an FK to `Users`, and the
  `(OrganizationId, OwnerUserId, CustomerId, Kind)` index.
- ADR-016 decision 1 is superseded in part; its other decisions (sender set, rich messages,
  `threadId` as checkpoint anchor, API as system of record, SignOff cards, transient
  notifications) are unchanged.
- Each user's general Salon carries its own `ThreadId`, so each user gets an independent
  LangGraph checkpoint thread. This is the intended outcome: concierge context becomes
  per-user.
- The Flutter app needs no change: it already posts only `organizationId` with
  `customerId: null`, so each authenticated user now resolves to their own Salon.
- A customer-bound Salon keeps its single `ThreadId` and shared agent context.
- `ListConversations` is a breaking response change for clients that assumed every listed
  Salon belonged to the whole organization: a caller now sees fewer rows.
