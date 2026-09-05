# Module: Organizations

Boutique ownership, staff membership, and invitation persistence (Issue #50).

## Models (`Models/`)

- **`Organization`** — a boutique. Has a unique human-readable `Slug`, a unique
  **`ClerkOrgId`** (the Clerk `org_...` id carried by the JWT `org_id` claim and
  the legacy `User.OrganizationId` string), the owning user, and lifecycle fields.
- **`OrganizationMembership`** — a user's membership in an organization with the
  canonical **boutique role** (`org:boutique_*` from the authorization catalog) and
  a `MembershipStatus`. Uniqueness: one row per `(OrganizationId, UserId)`.
- **`OrganizationInvitation`** — a code/link invitation. Only the **SHA-256
  `TokenHash`** of the secret code is stored (never plaintext, never logged);
  tracks expiry, revocation, inviter, intended recipient (user id and/or email),
  and one-time acceptance.

Domain exceptions in `OrganizationDomainExceptions.cs`; secret-code generation and
hashing in `Services/InvitationTokens.cs`.

## Persistence

- EF configs + indexes/FKs: `Infrastructure/Data/Configurations/Organization*.cs`.
- First EF migration **`AddOrganizationDomain`** captures the whole current model
  (including the pre-existing `Users` table) — this is the repository's migration
  baseline. It must be applied in coordination with the user-propagation schema
  work (Issue #47) and existing ad-hoc dev databases.

## Services (`Services/`)

`IOrganizationService` / `OrganizationService`:

- `CreateOrganizationAsync` — creates the org + the **owner's** membership (`org:boutique_owner`).
- `InviteMemberAsync` — issues an invite for a boutique role; returns the one-time plaintext code.
- `AcceptInvitationAsync` — validates (not accepted/revoked/expired + recipient match),
  then **atomically** activates a membership and marks the invitation accepted in a
  single `SaveChanges`, so acceptance is one-time.
- `GetUserMembershipsAsync` — active memberships for a user.

## Backfill behavior for legacy `User.OrganizationId` / `User.OrganizationRole`

Memberships are the canonical record. The denormalized strings on `User` are legacy
(issue #50 / #47). Backfill is a **one-time reconcile**, not a copy: an
`OrganizationId` string alone cannot reconstruct an organization row (no name/slug),
so backfill should create `Organization`/`OrganizationMembership` rows from Clerk
org-membership data (webhook or a Clerk sync), matching on `ClerkOrgId`, then stop
writing the legacy fields. Until then, a registered user with an empty
`OrganizationId` is fully supported — they can accept an invite and gain a
membership without any `org_id` set.

## Tests

`Aveline.Api.Tests/OrganizationRepositoryTests.cs` + `OrganizationServiceTests.cs`
cover create/query, owner membership, invite hashing, one-time acceptance, expiry,
revocation, recipient scoping, and the new-staff-with-no-membership path.
