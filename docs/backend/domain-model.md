# Aveline Backend — Domain and Data Model

**Status:** Proposed
**Companion:** [Requirements](backend-requirements.md) · [Statistics Catalog](statistics-catalog.md) · [Implementation Plan](implementation-plan.md)

This document defines every entity, column, type, constraint, index, and
relationship required by the backend plan. Column types are PostgreSQL types as
produced by the Npgsql provider; EF Core mapping notes are given where the
existing codebase has an established convention.

---

## 1. Conventions inherited from the existing codebase

These were read out of the current code, not invented. New entities must match.

| Convention | Established pattern | Evidence |
| --- | --- | --- |
| Table naming | `builder.ToTable("PascalCase")` for modern entities. Legacy entities use data annotations and are **inconsistent** (`[Table("Orders")]`, `[Table("Approval_Queue")]`). | `Infrastructure/Data/Configurations/BillingConfigurations.cs:12,55`; `Modules/Commerce/Models/ApprovalQueueEntry.cs:6` |
| Primary key | `Guid` with `Guid.CreateVersion7()` default for new entities; legacy entities use `Guid.NewGuid()` | `Modules/Billing/Models/UsageAccount.cs:36`; `Modules/Commerce/Models/Order.cs:17` |
| String limits | Always explicit via `.HasMaxLength(n)` or `[MaxLength(n)]` | `BillingConfigurations.cs:16-30` |
| Enums | Persisted as strings: `.HasConversion<string>().HasMaxLength(32)` | `BillingConfigurations.cs:68-70` |
| Decimals | Always explicit precision. `(18,8)` for money/USD, `(18,4)` for Blossom units, `(18,2)` for LKR | `BillingConfigurations.cs:32-36`; `Modules/Commerce/Models/Order.cs:34-40` |
| Foreign keys | `HasOne(...).WithMany().HasForeignKey(...).OnDelete(DeleteBehavior.Restrict)` | `BillingConfigurations.cs:41-44` |
| Indexes | `HasIndex(...)`, `IsUnique()` for uniqueness. No index naming convention is set, so PostgreSQL defaults (`IX_<Table>_<Columns>`) apply. | `BillingConfigurations.cs:47,87-88` |
| Soft delete | `HasQueryFilter(x => x.DeletedAt == null)` — applied only to `User`, `Customer`, `CustomerMemory` | `Infrastructure/Data/Configurations/UserConfiguration.cs:82` |
| Tenant scoping | **No EF global tenant filter exists.** Every repository must filter `OrganizationId` explicitly. | `Common/MultiTenancy/ITenantEntity.cs:6-11`; `Modules/Billing/Repositories/UsageRepository.cs:116` |
| Concurrency | **No concurrency token exists anywhere in the repository.** New mutable-balance tables must introduce one. | grep for `IsConcurrencyToken`/`RowVersion`/`xmin` returns zero matches |
| Config registration | `IEntityTypeConfiguration<T>` classes discovered by `ApplyConfigurationsFromAssembly`; `DbSet<T>` declared explicitly on `AppDbContext` | `Infrastructure/Data/AppDbContext.cs:88-92` |
| Migrations | `dotnet-ef` 10.0.11 via `dotnet-tools.json`; file naming `yyyyMMddHHmmss_DescriptivePascalCase.cs` | `dotnet-tools.json`; `Aveline.Api/Migrations/` |

**Note on the existing period model.** `UsageAccount` already carries
`PeriodStart`, `PeriodEnd`, and a unique index on `(OrganizationId, PeriodStart)`
(`BillingConfigurations.cs:86-88`). This plan keeps `UsageAccount` as the billing
period ledger rather than introducing a separate `BillingPeriod` entity, because
`ADR-010` explicitly defers billing-cycle management and a second period entity
would create two sources of truth for the same boundary.

---

## 2. Entity relationship overview

```
                          ┌──────────────────────┐
                          │    Organization      │
                          └──────────┬───────────┘
        ┌────────────────┬───────────┼───────────┬────────────────┬───────────────┐
        ▼                ▼           ▼           ▼                ▼               ▼
 Organization      Organization  Organization  UsageAccount   ApiKey      Organization-
 Membership        Invitation    Subscription     │                        Subscription
        │                                        │
        │                       ┌────────────────┼────────────────┐
        │                       ▼                ▼                ▼
        │              BlossomLedgerEntry   AiUsageRecord   BillingPeriodInfo
        │                                        │          (on UsageAccount)
        │                                        │
        │                                        ▼
        │                                AgentWorkflowRun
        │                                        │
        │                                        ▼
        │                                  AgentStepRun
        │
        ▼
       User ──── AuditLogEntry
        │
        └──── ApiRequestMetric / ApiRequestLog (organisation + user + api key)

 PlanTier ──► PlanEntitlement ◄── PlanEntitlementOverride ──► Organization
     │
     └──► BlossomPriceEntry

 BlossomConversionRule   (independent, resolved by provider + model + time)

 SystemMetricSample   SystemAlertRule ──► SystemAlert
```

---

## 3. Billing: Blossom pricing (Feature area 1)

### 3.1 `BlossomConversionRule` → `BlossomConversionRules`

The effective-dated normalisation rate. Resolved per `(provider, model,
timestamp)`.

| Column | Type | Null | Default | Notes |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | no | UUIDv7 | PK |
| `ScopeKind` | `varchar(16)` | no | — | `Global`, `Provider`, `ProviderModel` |
| `Provider` | `varchar(64)` | yes | `NULL` | Required for `Provider` and `ProviderModel` |
| `Model` | `varchar(128)` | yes | `NULL` | Required for `ProviderModel` |
| `UnitsPerBlossom` | `integer` | no | `1000` | Normalised units that buy 1 Blossom |
| `MinimumChargeBlossoms` | `numeric(18,4)` | no | `0.1` | Floor per workflow |
| `RoundingMode` | `varchar(16)` | no | `Ceiling` | `Ceiling`, `HalfUp`, `Down`, `Up` |
| `RoundingDecimals` | `smallint` | no | `1` | 0–6 |
| `EffectiveFrom` | `timestamptz` | no | — | Inclusive |
| `EffectiveTo` | `timestamptz` | yes | `NULL` | Exclusive; `NULL` = open-ended |
| `Status` | `varchar(16)` | no | `Draft` | `Draft`, `Active`, `Superseded`, `Cancelled` |
| `Version` | `integer` | no | `1` | Monotonic per `(ScopeKind, Provider, Model)` |
| `ChangeReason` | `varchar(500)` | no | — | Min 10 chars |
| `CreatedByUserId` | `uuid` | no | — | FK → `Users.Id` |
| `ApprovedByUserId` | `uuid` | yes | `NULL` | FK → `Users.Id`; required for backdated rules |
| `CreatedAt` | `timestamptz` | no | `now()` | |
| `UpdatedAt` | `timestamptz` | no | `now()` | |
| `ConcurrencyToken` | `xid` | no | system | Npgsql `xmin` system column |

**Constraints**

```sql
ALTER TABLE "BlossomConversionRules"
  ADD CONSTRAINT "CK_BlossomConversionRules_Scope" CHECK (
    ("ScopeKind" = 'Global'        AND "Provider" IS NULL     AND "Model" IS NULL) OR
    ("ScopeKind" = 'Provider'      AND "Provider" IS NOT NULL AND "Model" IS NULL) OR
    ("ScopeKind" = 'ProviderModel' AND "Provider" IS NOT NULL AND "Model" IS NOT NULL)
  ),
  ADD CONSTRAINT "CK_BlossomConversionRules_Range" CHECK (
    "EffectiveTo" IS NULL OR "EffectiveTo" > "EffectiveFrom"
  ),
  ADD CONSTRAINT "CK_BlossomConversionRules_Units" CHECK ("UnitsPerBlossom" > 0),
  ADD CONSTRAINT "CK_BlossomConversionRules_Minimum" CHECK ("MinimumChargeBlossoms" >= 0),
  ADD CONSTRAINT "CK_BlossomConversionRules_Decimals" CHECK ("RoundingDecimals" BETWEEN 0 AND 6);
```

**Indexes**

```sql
-- Lookup path: resolve the active rule for (provider, model, t)
CREATE UNIQUE INDEX "IX_BlossomConversionRules_Scope_From"
  ON "BlossomConversionRules" ("ScopeKind", "Provider", "Model", "EffectiveFrom")
  NULLS NOT DISTINCT;

CREATE INDEX "IX_BlossomConversionRules_Active"
  ON "BlossomConversionRules" ("Status", "EffectiveFrom", "EffectiveTo")
  WHERE "Status" = 'Active';

-- Overlap prevention. Requires CREATE EXTENSION btree_gist;
ALTER TABLE "BlossomConversionRules"
  ADD CONSTRAINT "EX_BlossomConversionRules_NoOverlap" EXCLUDE USING gist (
    "ScopeKind" WITH =,
    coalesce("Provider", '') WITH =,
    coalesce("Model", '')    WITH =,
    tstzrange("EffectiveFrom", "EffectiveTo", '[)') WITH &&
  ) WHERE ("Status" IN ('Draft', 'Active'));
```

`NULLS NOT DISTINCT` requires PostgreSQL 15+; the stack is PostgreSQL 16
(`docs/ADR/ADR-003`). The `coalesce(...)` expressions in the exclusion constraint
exist because GiST equality on `NULL` is not usable; they must be backed by an
expression index for the constraint to be maintained efficiently:

```sql
CREATE INDEX "IX_BlossomConversionRules_ExScope" ON "BlossomConversionRules"
  ("ScopeKind", coalesce("Provider", ''), coalesce("Model", ''));
```

### 3.2 `BlossomPriceEntry` → `BlossomPriceEntries`

The effective-dated commercial price of a Blossom. Used for revenue reporting and
Blossom pack pricing. **Never** used to compute consumption.

| Column | Type | Null | Default | Notes |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | no | UUIDv7 | PK |
| `PlanTier` | `varchar(32)` | yes | `NULL` | `NULL` = applies to all tiers |
| `OrganizationId` | `uuid` | yes | `NULL` | `NULL` = tier price; set = contract override |
| `SkuKind` | `varchar(24)` | no | — | `PlanAllowance`, `TopUpPack`, `OverageUsage` |
| `SkuCode` | `varchar(64)` | yes | `NULL` | e.g. `blossom_pack_500` |
| `BlossomQuantity` | `numeric(18,4)` | no | — | Pack size; `1` for a unit price |
| `PriceLkr` | `numeric(18,2)` | no | — | Total price for `BlossomQuantity` |
| `EffectiveFrom` | `timestamptz` | no | — | Inclusive |
| `EffectiveTo` | `timestamptz` | yes | `NULL` | Exclusive |
| `Status` | `varchar(16)` | no | `Draft` | `Draft`, `Active`, `Superseded`, `Cancelled` |
| `ChangeReason` | `varchar(500)` | no | — | |
| `CreatedByUserId` | `uuid` | no | — | FK → `Users.Id` |
| `CreatedAt` | `timestamptz` | no | `now()` | |
| `UpdatedAt` | `timestamptz` | no | `now()` | |

**Constraints:** `BlossomQuantity > 0`, `PriceLkr >= 0`,
`EffectiveTo IS NULL OR EffectiveTo > EffectiveFrom`.
**Index:** unique on
`(coalesce(PlanTier,''), coalesce(OrganizationId,'00000000-0000-0000-0000-000000000000'::uuid), SkuKind, coalesce(SkuCode,''), EffectiveFrom)`.

### 3.3 Changes to `AiUsageRecord`

Six added columns make historical pricing reproducible. All are nullable, so the
migration is backwards compatible and the existing endpoint contract is
unchanged.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `AgentWorkflowRunId` | `uuid` | yes | FK → `AgentWorkflowRuns.Id`; 1:1 link |
| `PricingRuleId` | `uuid` | yes | FK → `BlossomConversionRules.Id`; `NULL` for pre-migration rows |
| `PricingRuleVersion` | `integer` | yes | Snapshot of the rule version used |
| `UnitsPerBlossom` | `integer` | yes | Snapshot; `NULL` ⇒ assume 1000 |
| `RoundingMode` | `varchar(16)` | yes | Snapshot |
| `RoundingDecimals` | `smallint` | yes | Snapshot |
| `NormalizedUnits` | `bigint` | yes | `InputTokens + OutputTokens + CachedTokens` as applied |

Existing index `(OrganizationId, CreatedAt)` is preserved
(`BillingConfigurations.cs:47`). One index is added:

```sql
CREATE INDEX "IX_AiUsageRecords_Workflow"
  ON "AiUsageRecords" ("OrganizationId", "WorkflowId");
```

---

## 4. Billing: entitlement ledger and operations (Feature area 2)

### 4.1 `UsageAccount` (extended) → `UsageAccounts`

The O(1) balance projection. `ADR-010` chose this two-table design deliberately
and it is preserved.

| Column | Type | Null | Default | Change |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | no | UUIDv7 | unchanged |
| `OrganizationId` | `uuid` | no | — | unchanged |
| `PeriodStart` | `timestamptz` | no | — | unchanged |
| `PeriodEnd` | `timestamptz` | no | — | unchanged |
| `MonthlyBlossomLimit` | `numeric(18,4)` | no | — | unchanged |
| `BlossomUsed` | `numeric(18,4)` | no | `0` | unchanged |
| `BlossomGranted` | `numeric(18,4)` | no | `0` | **new** — sum of positive non-consumption deltas |
| `BlossomAdjusted` | `numeric(18,4)` | no | `0` | **new** — magnitude of negative non-consumption deltas |
| `BlossomRemaining` | `numeric(18,4)` | no | — | unchanged, now derived from all four |
| `ActiveCustomerCount` | `integer` | no | `0` | unchanged |
| `StaffCount` | `integer` | no | `0` | unchanged |
| `PlanTierSnapshot` | `varchar(32)` | yes | `NULL` | **new** — plan at period open |
| `Status` | `varchar(32)` | no | `Active` | unchanged |
| `IsClosed` | `boolean` | no | `false` | **new** |
| `ClosedAt` | `timestamptz` | yes | `NULL` | **new** |
| `ConcurrencyToken` | `xid` | no | system | **new** — Npgsql `xmin` |
| `UpdatedAt` | `timestamptz` | no | `now()` | unchanged |
| `Organization` | nav | — | — | unchanged |

**Normative balance definition**

```
BlossomRemaining = MonthlyBlossomLimit + BlossomGranted - BlossomAdjusted - BlossomUsed
```

A database `CHECK` enforces the identity so a bug cannot silently desynchronise
the projection:

```sql
ALTER TABLE "UsageAccounts" ADD CONSTRAINT "CK_UsageAccounts_Balance" CHECK (
  "BlossomRemaining" = "MonthlyBlossomLimit" + "BlossomGranted" - "BlossomAdjusted" - "BlossomUsed"
);
```

Existing unique index on `(OrganizationId, PeriodStart)` is preserved. One index
is added:

```sql
CREATE INDEX "IX_UsageAccounts_Open"
  ON "UsageAccounts" ("OrganizationId", "IsClosed", "PeriodStart" DESC);
```

### 4.2 `BlossomLedgerEntry` → `BlossomLedgerEntries` (new, append-only)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | no | FK → `Organizations.Id`, `Restrict` |
| `UsageAccountId` | `uuid` | no | FK → `UsageAccounts.Id`, `Restrict` |
| `EntryType` | `varchar(32)` | no | See enum below |
| `BlossomDelta` | `numeric(18,4)` | no | Signed; `> 0` credit, `< 0` debit; never `0` |
| `BlossomBalanceAfter` | `numeric(18,4)` | no | `BlossomRemaining` after this entry |
| `Reason` | `varchar(500)` | no | Min 10 chars |
| `SourceKind` | `varchar(32)` | yes | `Admin`, `PaymentProvider`, `PlanChange`, `Expiry`, `System` |
| `SourceRef` | `varchar(128)` | yes | Provider payment id, ticket id, etc. |
| `SupersedesEntryId` | `uuid` | yes | FK → self; set on `TopUpRevocation` |
| `ExpiresAt` | `timestamptz` | yes | `NULL` = never expires |
| `IdempotencyKey` | `varchar(128)` | yes | |
| `IdempotencyScope` | `varchar(64)` | yes | Route template that consumed the key |
| `CreatedByUserId` | `uuid` | yes | FK → `Users.Id`; `NULL` for system entries |
| `CreatedAt` | `timestamptz` | no | |

**`EntryType` values**

| Value | Sign | Meaning |
| --- | --- | --- |
| `PeriodAllocation` | + | The plan allowance for the period (written at period open) |
| `TopUpGrant` | + | A purchased or gifted Blossom pack |
| `AdminCredit` | + | Manual credit by an Aveline administrator |
| `AdminDebit` | − | Manual debit by an Aveline administrator |
| `TopUpRevocation` | − | Reversal of a `TopUpGrant` |
| `Expiry` | − | Expiry of an un-consumed, expiring grant |
| `PlanUpgradeProration` | + | Allowance delta on mid-period upgrade |
| `PlanDowngradeAdjustment` | − | Allowance delta on immediate downgrade |
| `CorrectionRecompute` | ± | Result of a pricing recompute |

**Constraints**

```sql
ALTER TABLE "BlossomLedgerEntries"
  ADD CONSTRAINT "CK_BlossomLedgerEntries_Delta" CHECK ("BlossomDelta" <> 0),
  ADD CONSTRAINT "CK_BlossomLedgerEntries_Reason" CHECK (length("Reason") >= 10),
  ADD CONSTRAINT "CK_BlossomLedgerEntries_Expiry" CHECK ("ExpiresAt" IS NULL OR "ExpiresAt" > "CreatedAt");
```

**Indexes**

```sql
CREATE INDEX "IX_BlossomLedgerEntries_Org_Created"
  ON "BlossomLedgerEntries" ("OrganizationId", "CreatedAt" DESC);

CREATE INDEX "IX_BlossomLedgerEntries_Org_Type_Created"
  ON "BlossomLedgerEntries" ("OrganizationId", "EntryType", "CreatedAt" DESC);

CREATE INDEX "IX_BlossomLedgerEntries_Expiring"
  ON "BlossomLedgerEntries" ("ExpiresAt") WHERE "ExpiresAt" IS NOT NULL;

CREATE UNIQUE INDEX "IX_BlossomLedgerEntries_Idempotency"
  ON "BlossomLedgerEntries" ("OrganizationId", "IdempotencyScope", "IdempotencyKey")
  WHERE "IdempotencyKey" IS NOT NULL;
```

**Immutability.** Like `AiUsageRecord`, rows are never updated or deleted.
Corrections are new entries with `SupersedesEntryId` set. This is enforced by
convention plus a repository with no update method, matching the existing
treatment of `AiUsageRecord` (`AiUsageRecord.cs:8-15`).

**Consumption is deliberately not mirrored here.** `AiUsageRecord` is already the
append-only consumption log and `UsageAccount.BlossomUsed` is its aggregate.
Duplicating every consumption event into this table would double the hot-path
write volume with no query benefit. The invariant
`SUM(BlossomLedgerEntries.BlossomDelta) + (-1 * BlossomUsed) == BlossomRemaining - MonthlyBlossomLimit`
is verified by a reconciliation query and asserted by tests. See
[OQ-3](assumptions-and-open-questions.md) for the unified-ledger alternative.

### 4.3 `IdempotencyRecord` → `IdempotencyRecords` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | yes | `NULL` for non-tenant endpoints |
| `ActorUserId` | `uuid` | yes | |
| `ApiKeyId` | `uuid` | yes | |
| `IdempotencyKey` | `varchar(128)` | no | Client-supplied |
| `Endpoint` | `varchar(200)` | no | Route template |
| `HttpMethod` | `varchar(10)` | no | |
| `RequestHash` | `char(64)` | no | SHA-256 hex of the canonical JSON body |
| `ResponseStatus` | `smallint` | no | |
| `ResponseBodyJson` | `jsonb` | no | Replayed verbatim on retry |
| `CreatedAt` | `timestamptz` | no | |
| `ExpiresAt` | `timestamptz` | no | `CreatedAt + Billing:IdempotencyRetentionHours` |

```sql
CREATE UNIQUE INDEX "IX_IdempotencyRecords_Replay"
  ON "IdempotencyRecords" ("OrganizationId", "Endpoint", "IdempotencyKey")
  NULLS NOT DISTINCT;
CREATE INDEX "IX_IdempotencyRecords_Expiry" ON "IdempotencyRecords" ("ExpiresAt");
```

### 4.4 `OrganizationSubscription` → `OrganizationSubscriptions` (new)

Phase 3 per `ADR-010`, but required by the plan-change requirements. One current
row per organisation.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | no | Unique |
| `PlanTier` | `varchar(32)` | no | Mirrors `Organization.PlanTier` while active |
| `BillingCycle` | `varchar(16)` | no | `Monthly`, `Annual` |
| `Status` | `varchar(24)` | no | `Trialing`, `Active`, `PastDue`, `Cancelled`, `Expired` |
| `CurrentPeriodStart` | `timestamptz` | no | |
| `CurrentPeriodEnd` | `timestamptz` | no | |
| `SeatsIncluded` | `integer` | no | |
| `PriceLkr` | `numeric(18,2)` | no | |
| `CancelAtPeriodEnd` | `boolean` | no | |
| `ExternalProvider` | `varchar(32)` | yes | `stripe`, `payhere`, … |
| `ExternalSubscriptionId` | `varchar(128)` | yes | |
| `CreatedAt` | `timestamptz` | no | |
| `UpdatedAt` | `timestamptz` | no | |
| `CancelledAt` | `timestamptz` | yes | |
| `ConcurrencyToken` | `xid` | no | system |

```sql
CREATE UNIQUE INDEX "IX_OrganizationSubscriptions_Org" ON "OrganizationSubscriptions" ("OrganizationId");
CREATE UNIQUE INDEX "IX_OrganizationSubscriptions_External"
  ON "OrganizationSubscriptions" ("ExternalProvider", "ExternalSubscriptionId")
  WHERE "ExternalSubscriptionId" IS NOT NULL;
```

### 4.5 `PlanEntitlement` → `PlanEntitlements` (new)

Replaces the three hardcoded plan-limit tables (defect D-12).

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `PlanTier` | `varchar(32)` | no | `Seed`, `Bloom`, `Orchid`, `Rose`, `Enterprise` |
| `Key` | `varchar(64)` | no | See key list below |
| `ValueType` | `varchar(16)` | no | `Integer`, `Decimal`, `Boolean`, `String` |
| `ValueDecimal` | `numeric(18,4)` | yes | Used for `Integer` and `Decimal` |
| `ValueBool` | `boolean` | yes | |
| `ValueText` | `varchar(200)` | yes | |
| `IsEnabled` | `boolean` | no | Allows disabling a capability without deleting the row |
| `EffectiveFrom` | `timestamptz` | no | |
| `EffectiveTo` | `timestamptz` | yes | |
| `CreatedAt` | `timestamptz` | no | |

**Entitlement keys** (seeded from `docs/architecture/pricing_plan.md`):

| Key | ValueType | Seed | Bloom | Orchid | Rose | Enterprise |
| --- | --- | --- | --- | --- | --- | --- |
| `blossoms.monthly` | Decimal | 150 | 750 | 2000 | 5000 | 9999 |
| `staff.max` | Integer | 1 | 3 | 10 | 25 | 9999 |
| `customers.active.max` | Integer | 50 | 250 | 1000 | 5000 | 999999 |
| `api.access` | Boolean | false | false | false | true | true |
| `api.requests.monthly` | Integer | 0 | 0 | 0 | 1 000 000 | 10 000 000 |
| `api.requests.perMinute` | Integer | 30 | 60 | 300 | 600 | 6000 |
| `whatsapp.monthly` | Integer | 25 | 500 | 2000 | 5000 | 99999 |
| `agents.visual` | String | `limited` | `full` | `full` | `full` | `full` |
| `agents.commerce` | String | `limited` | `limited` | `full` | `full` | `full` |
| `ai.customContext` | String | `none` | `basic` | `full` | `full` | `full` |
| `ai.customAgents` | Boolean | false | false | false | true | true |
| `analytics.level` | String | `basic` | `basic` | `advanced` | `advanced` | `advanced` |
| `automation.level` | String | `none` | `basic` | `advanced` | `advanced` | `advanced` |
| `stats.retentionDays` | Integer | 90 | 180 | 400 | 400 | 400 |

The `blossoms.monthly` and `staff.max` values reproduce the existing
`PlanBlossomLimits` maps exactly (`UsageTrackerService.cs:24-31`,
`OnboardingService.cs:19-25`), so migration is behaviour-preserving.

```sql
CREATE UNIQUE INDEX "IX_PlanEntitlements_Tier_Key_From"
  ON "PlanEntitlements" ("PlanTier", "Key", "EffectiveFrom");
```

### 4.6 `PlanEntitlementOverride` → `PlanEntitlementOverrides` (new)

Per-organisation exception, used for Enterprise contracts.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | no | FK → `Organizations.Id` |
| `Key` | `varchar(64)` | no | Same key space as `PlanEntitlements` |
| `ValueType` | `varchar(16)` | no | |
| `ValueDecimal` | `numeric(18,4)` | yes | |
| `ValueBool` | `boolean` | yes | |
| `ValueText` | `varchar(200)` | yes | |
| `EffectiveFrom` | `timestamptz` | no | |
| `EffectiveTo` | `timestamptz` | yes | |
| `Reason` | `varchar(500)` | no | |
| `CreatedByUserId` | `uuid` | no | FK → `Users.Id` |
| `CreatedAt` | `timestamptz` | no | |

```sql
CREATE UNIQUE INDEX "IX_PlanEntitlementOverrides_Org_Key_From"
  ON "PlanEntitlementOverrides" ("OrganizationId", "Key", "EffectiveFrom");
```

---

## 5. Users and API keys (Feature areas 3 and 4)

### 5.1 `ApiKey` → `ApiKeys` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | no | FK → `Organizations.Id`, `Restrict` |
| `Name` | `varchar(100)` | no | Human label |
| `Prefix` | `varchar(16)` | no | Lookup key; unique |
| `KeyHash` | `varchar(128)` | no | SHA-256 hex of the full secret. Never returned. |
| `HashAlgorithm` | `varchar(32)` | no | `sha256` |
| `Scopes` | `text[]` | no | Subset of `Permissions.All` |
| `Environment` | `varchar(16)` | no | `live`, `test` |
| `Status` | `varchar(16)` | no | `Active`, `Revoked`, `Expired` |
| `CreatedByUserId` | `uuid` | no | FK → `Users.Id` |
| `CreatedAt` | `timestamptz` | no | |
| `ExpiresAt` | `timestamptz` | yes | |
| `LastUsedAt` | `timestamptz` | yes | Updated at most once per minute |
| `RevokedAt` | `timestamptz` | yes | |
| `RevokedByUserId` | `uuid` | yes | FK → `Users.Id` |
| `RevokedReason` | `varchar(300)` | yes | |
| `RequestCount` | `bigint` | no | `0` — lifetime counter, flushed from telemetry |
| `LastUsedIpHash` | `char(64)` | yes | SHA-256 of the last client IP |

```sql
CREATE UNIQUE INDEX "IX_ApiKeys_Prefix" ON "ApiKeys" ("Prefix");
CREATE INDEX "IX_ApiKeys_Org_Status" ON "ApiKeys" ("OrganizationId", "Status");
CREATE INDEX "IX_ApiKeys_Expiring" ON "ApiKeys" ("ExpiresAt")
  WHERE "Status" = 'Active' AND "ExpiresAt" IS NOT NULL;
```

**Secret format.** `avl_` + `live|test` + `_` + 32 base62 characters, e.g.
`avl_live_7Qk2mZpX9rT4vBnL6sW1cYhJ3dF8gAeU`. `Prefix` stores the first 16
characters (`avl_live_7Qk2mZ`); the remaining 24 characters are the secret and
are verified by constant-time comparison of the SHA-256 hash of the *full*
string. The hash is never exposed by any endpoint, log, or audit record.

### 5.2 `Organization` (extended) → `Organizations`

| Column | Type | Null | Default | Change |
| --- | --- | --- | --- | --- |
| `BillingEmail` | `varchar(254)` | yes | `NULL` | **new** |
| `ContactEmail` | `varchar(254)` | yes | `NULL` | **new** |
| `Currency` | `char(3)` | no | `'LKR'` | **new** |
| `TimeZone` | `varchar(64)` | no | `'UTC'` | **new** — IANA id; see [OQ-4](assumptions-and-open-questions.md) |
| `SuspendedAt` | `timestamptz` | yes | `NULL` | **new** |

All other columns are unchanged (`Modules/Organizations/Models/Organization.cs`).
Note the existing `PlanTier` enum is already persisted as a string
(`Organization.cs:46`), which the entitlement resolver reads.

### 5.3 `OrganizationMembership` — verify, do not change shape

The model is `(Id, UserId, OrganizationId, BoutiqueRole, Status, CreatedAt,
UpdatedAt)` (`Modules/Organizations/Models/OrganizationMembership.cs`). A unique
index on `(UserId, OrganizationId)` is required for correctness and its presence
was **not confirmed** during this review. The implementation plan must verify it
and add it if absent — this is a prerequisite for
`OrganizationScopeAuthorizationHandler` returning a single membership
deterministically.

---

## 6. Agentic statistics (Feature area 5)

### 6.1 `AgentWorkflowRun` → `AgentWorkflowRuns` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | yes | `NULL` when unresolvable (fixes D-7) |
| `WorkflowId` | `varchar(128)` | no | LangGraph thread/run id |
| `ParentWorkflowRunId` | `uuid` | yes | FK → self, for sub-graph runs |
| `RequestId` | `varchar(128)` | yes | Correlation id from the HTTP request |
| `TraceId` | `uuid` | yes | OTel trace id when available |
| `TriggerKind` | `varchar(32)` | no | `WhatsAppInbound`, `WhatsAppOutbound`, `ApiRequest`, `Scheduled`, `Manual`, `Webhook` |
| `TriggerRef` | `varchar(128)` | yes | Message id, request id, etc. |
| `ConversationId` | `uuid` | yes | FK → `Conversations.Id` |
| `CustomerId` | `uuid` | yes | FK → `Customers.Id` |
| `InitiatedByUserId` | `uuid` | yes | FK → `Users.Id` |
| `Status` | `varchar(24)` | no | `Running`, `Succeeded`, `Failed`, `Cancelled`, `TimedOut`, `PausedForApproval` |
| `AgentsInvolved` | `text[]` | no | `[]`; distinct `AgentKey`s observed |
| `StartedAt` | `timestamptz` | no | |
| `CompletedAt` | `timestamptz` | yes | |
| `DurationMs` | `integer` | yes | |
| `PausedAt` | `timestamptz` | yes | |
| `ResumedAt` | `timestamptz` | yes | |
| `ApprovalWaitMs` | `integer` | yes | `ResumedAt - PausedAt` |
| `StepCount` | `integer` | no | `0` |
| `ToolCallCount` | `integer` | no | `0` |
| `RetryCount` | `integer` | no | `0` |
| `InputTokens` | `integer` | no | `0` |
| `OutputTokens` | `integer` | no | `0` |
| `CachedTokens` | `integer` | no | `0` |
| `NormalizedUnits` | `bigint` | yes | |
| `ActualCostUsd` | `numeric(18,8)` | no | `0` |
| `BlossomUnits` | `numeric(18,4)` | no | `0` |
| `PricingRuleId` | `uuid` | yes | FK → `BlossomConversionRules.Id` |
| `PlanTierAtRun` | `varchar(32)` | yes | |
| `IsUnattributed` | `boolean` | no | `true` when `OrganizationId IS NULL` |
| `ErrorCode` | `varchar(64)` | yes | |
| `ErrorMessage` | `varchar(1000)` | yes | Truncated; never contains prompt content |
| `CreatedAt` | `timestamptz` | no | Ingest time |
| `UpdatedAt` | `timestamptz` | no | |

```sql
CREATE UNIQUE INDEX "IX_AgentWorkflowRuns_Org_Workflow"
  ON "AgentWorkflowRuns" ("OrganizationId", "WorkflowId") NULLS NOT DISTINCT;
CREATE INDEX "IX_AgentWorkflowRuns_Org_Started"
  ON "AgentWorkflowRuns" ("OrganizationId", "StartedAt" DESC);
CREATE INDEX "IX_AgentWorkflowRuns_Status_Started"
  ON "AgentWorkflowRuns" ("Status", "StartedAt");
CREATE INDEX "IX_AgentWorkflowRuns_Org_Trigger_Started"
  ON "AgentWorkflowRuns" ("OrganizationId", "TriggerKind", "StartedAt" DESC);
CREATE INDEX "IX_AgentWorkflowRuns_Unattributed"
  ON "AgentWorkflowRuns" ("StartedAt" DESC) WHERE "IsUnattributed";
CREATE INDEX "IX_AgentWorkflowRuns_Agents"
  ON "AgentWorkflowRuns" USING gin ("AgentsInvolved");
```

### 6.2 `AgentStepRun` → `AgentStepRuns` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `WorkflowRunId` | `uuid` | no | FK → `AgentWorkflowRuns.Id`, `Cascade` |
| `OrganizationId` | `uuid` | yes | Denormalised; avoids a join on every statistic |
| `StepIndex` | `smallint` | no | 0-based order within the run |
| `AgentKey` | `varchar(64)` | no | `customer_memory`, `visual_insight`, `commerce`, `orchestrator` |
| `NodeName` | `varchar(128)` | no | LangGraph node name |
| `StepKind` | `varchar(24)` | no | `LlmCall`, `ToolCall`, `Decision`, `HumanInterrupt`, `Retrieval`, `Validation` |
| `ToolName` | `varchar(128)` | yes | Set when `StepKind = ToolCall` |
| `Status` | `varchar(16)` | no | `Succeeded`, `Failed`, `Skipped`, `TimedOut` |
| `AttemptNumber` | `smallint` | no | `1` |
| `StartedAt` | `timestamptz` | no | |
| `CompletedAt` | `timestamptz` | yes | |
| `DurationMs` | `integer` | yes | |
| `Provider` | `varchar(64)` | yes | |
| `Model` | `varchar(128)` | yes | |
| `InputTokens` | `integer` | no | `0` |
| `OutputTokens` | `integer` | no | `0` |
| `CachedTokens` | `integer` | no | `0` |
| `ActualCostUsd` | `numeric(18,8)` | no | `0` |
| `ArgsHash` | `char(64)` | yes | SHA-256 of canonicalised args. **Args are never stored.** |
| `ResultBytes` | `integer` | yes | Size only. **Result content is never stored.** |
| `ErrorCode` | `varchar(64)` | yes | |
| `ErrorMessage` | `varchar(1000)` | yes | |
| `CreatedAt` | `timestamptz` | no | |

```sql
CREATE UNIQUE INDEX "IX_AgentStepRuns_Run_Index_Attempt"
  ON "AgentStepRuns" ("WorkflowRunId", "StepIndex", "AttemptNumber");
CREATE INDEX "IX_AgentStepRuns_Org_Agent_Started"
  ON "AgentStepRuns" ("OrganizationId", "AgentKey", "StartedAt" DESC);
CREATE INDEX "IX_AgentStepRuns_Tool_Started"
  ON "AgentStepRuns" ("ToolName", "StartedAt" DESC) WHERE "ToolName" IS NOT NULL;
CREATE INDEX "IX_AgentStepRuns_Failed"
  ON "AgentStepRuns" ("OrganizationId", "StartedAt" DESC) WHERE "Status" = 'Failed';
```

**Privacy rule (FR-5.9).** No table in this module stores prompt text, tool
arguments, tool results, or customer content. `ArgsHash` and `ResultBytes` exist
specifically so that a tool call can be counted, grouped, and compared without
retaining its content. This must be asserted by a test that scans the ingest DTO
for forbidden field names.

---

## 7. API consumption statistics (Feature area 6)

### 7.1 `ApiRequestMetric` → `ApiRequestMetrics` (new, hourly rollup)

The primary read path. One row per unique dimension tuple per hour.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` | no | `GENERATED ALWAYS AS IDENTITY` PK. Chosen over `uuid` because this is the highest-volume table in the system and the key is never exposed to clients. |
| `OrganizationId` | `uuid` | yes | `NULL` = unattributed |
| `ApiKeyId` | `uuid` | yes | |
| `UserId` | `uuid` | yes | |
| `RouteTemplate` | `varchar(200)` | no | e.g. `/api/v1/orgs/{organizationId}/usage` |
| `HttpMethod` | `varchar(10)` | no | |
| `StatusCode` | `smallint` | no | Exact status, so 429 is distinguishable (FR-6.8) |
| `StatusClass` | `varchar(8)` | no | `1xx`–`5xx` |
| `IsThrottled` | `boolean` | no | `true` iff `StatusCode = 429` |
| `WindowStart` | `timestamptz` | no | Truncated to the hour, UTC |
| `WindowSize` | `varchar(8)` | no | `hour` |
| `RequestCount` | `bigint` | no | |
| `ErrorCount` | `bigint` | no | `StatusCode >= 400` |
| `TotalDurationMs` | `bigint` | no | Sum, for the mean |
| `MaxDurationMs` | `integer` | no | |
| `BucketCounts` | `integer[]` | no | 12 cumulative counts; see the bucket set below |
| `RequestBytes` | `bigint` | no | `0` |
| `ResponseBytes` | `bigint` | no | `0` |

**Latency buckets (milliseconds, cumulative `le` semantics):**
`[5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000]` plus an overflow
bucket, so `BucketCounts` has 12 elements and `BucketCounts[11] == RequestCount`.

```sql
CREATE UNIQUE INDEX "IX_ApiRequestMetrics_Dimensions"
  ON "ApiRequestMetrics" ("OrganizationId", "ApiKeyId", "UserId",
                          "RouteTemplate", "HttpMethod", "StatusCode", "WindowStart")
  NULLS NOT DISTINCT;
CREATE INDEX "IX_ApiRequestMetrics_Org_Window"
  ON "ApiRequestMetrics" ("OrganizationId", "WindowStart" DESC);
CREATE INDEX "IX_ApiRequestMetrics_Route_Window"
  ON "ApiRequestMetrics" ("RouteTemplate", "WindowStart" DESC);
CREATE INDEX "IX_ApiRequestMetrics_Key_Window"
  ON "ApiRequestMetrics" ("ApiKeyId", "WindowStart" DESC) WHERE "ApiKeyId" IS NOT NULL;
CREATE INDEX "IX_ApiRequestMetrics_Window"
  ON "ApiRequestMetrics" ("WindowStart");
```

The unique index makes the incremental upsert (`ON CONFLICT ... DO UPDATE SET
RequestCount = t.RequestCount + EXCLUDED.RequestCount`) correct without a
surrogate business key.

### 7.2 `ApiRequestLog` → `ApiRequestLogs` (new, raw, partitioned, short retention)

Declarative range partitioning by day on `OccurredAt`.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` | no | Identity, part of the partitioned PK |
| `OccurredAt` | `timestamptz` | no | Partition key |
| `OrganizationId` | `uuid` | yes | |
| `ApiKeyId` | `uuid` | yes | |
| `UserId` | `uuid` | yes | |
| `RouteTemplate` | `varchar(200)` | no | |
| `HttpMethod` | `varchar(10)` | no | |
| `StatusCode` | `smallint` | no | |
| `DurationMs` | `integer` | no | |
| `RequestBytes` | `integer` | no | |
| `ResponseBytes` | `integer` | no | |
| `RequestId` | `varchar(128)` | yes | The `X-Request-Id` value |
| `TraceId` | `uuid` | yes | |
| `ClientIpHash` | `char(64)` | yes | SHA-256; **raw IP is never stored** |
| `UserAgentHash` | `char(64)` | yes | SHA-256 |
| `ErrorCode` | `varchar(64)` | yes | |
| `ResourceType` | `varchar(64)` | yes | e.g. `Customer`, `Order` |
| `ResourceId` | `varchar(128)` | yes | So a slow request links to the entity it touched |

```sql
CREATE TABLE "ApiRequestLogs" (
  ...
  PRIMARY KEY ("Id", "OccurredAt")
) PARTITION BY RANGE ("OccurredAt");

CREATE INDEX "IX_ApiRequestLogs_Org_Occurred"
  ON "ApiRequestLogs" ("OrganizationId", "OccurredAt" DESC);
CREATE INDEX "IX_ApiRequestLogs_Key_Occurred"
  ON "ApiRequestLogs" ("ApiKeyId", "OccurredAt" DESC) WHERE "ApiKeyId" IS NOT NULL;
CREATE INDEX "IX_ApiRequestLogs_Slow"
  ON "ApiRequestLogs" ("OccurredAt" DESC) WHERE "DurationMs" > 1000;
CREATE INDEX "IX_ApiRequestLogs_Errors"
  ON "ApiRequestLogs" ("OccurredAt" DESC) WHERE "StatusCode" >= 500;
CREATE INDEX "IX_ApiRequestLogs_RequestId"
  ON "ApiRequestLogs" ("RequestId") WHERE "RequestId" IS NOT NULL;
```

Because EF Core cannot express partition DDL, the partitioning and the partition
management functions are applied in a **raw SQL migration step** alongside the
generated migration. The plan specifies a `CREATE TABLE ... PARTITION BY RANGE`
that supersedes EF's generated `CREATE TABLE`, plus two SQL functions:
`aveline_ensure_api_request_log_partition(date)` and
`aveline_drop_old_api_request_log_partitions(int retention_days)`.

### 7.3 `ApiQuotaUsage` → `ApiQuotaUsage` (new)

Durable per-period quota counters. Redis holds the live counter; this table is
written at period boundaries and on shutdown.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | no | |
| `ApiKeyId` | `uuid` | yes | `NULL` = org-level quota |
| `MetricKey` | `varchar(64)` | no | `api.requests.monthly`, `api.requests.daily` |
| `PeriodStart` | `timestamptz` | no | |
| `PeriodEnd` | `timestamptz` | no | |
| `LimitValue` | `bigint` | yes | `NULL` = unlimited |
| `UsedValue` | `bigint` | no | |
| `WarnedAt` | `timestamptz` | yes | Set once when the warning threshold is crossed |
| `ExhaustedAt` | `timestamptz` | yes | Set once when the limit is reached |
| `UpdatedAt` | `timestamptz` | no | |

```sql
CREATE UNIQUE INDEX "IX_ApiQuotaUsage_Scope_Metric_Period"
  ON "ApiQuotaUsage" ("OrganizationId", "ApiKeyId", "MetricKey", "PeriodStart")
  NULLS NOT DISTINCT;
```

---

## 8. System statistics (Feature area 7)

### 8.1 `SystemMetricSample` → `SystemMetricSamples` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `bigint` | no | Identity PK |
| `MetricName` | `varchar(100)` | no | `aveline.api.requests_per_second`, `aveline.db.pool_in_use`, … |
| `DimensionsJson` | `jsonb` | no | `{}` |
| `DimensionHash` | `char(64)` | no | SHA-256 of the canonicalised dimensions, for uniqueness |
| `ValueDecimal` | `numeric(18,6)` | yes | Exactly one of the two value columns is set |
| `ValueBigint` | `bigint` | yes | |
| `Unit` | `varchar(24)` | no | `count`, `ms`, `bytes`, `ratio`, `percent` |
| `WindowStart` | `timestamptz` | no | |
| `WindowSize` | `varchar(8)` | no | `instant`, `minute`, `hour`, `day` |
| `SampledAt` | `timestamptz` | no | |

```sql
ALTER TABLE "SystemMetricSamples" ADD CONSTRAINT "CK_SystemMetricSamples_Value" CHECK (
  ("ValueDecimal" IS NOT NULL) <> ("ValueBigint" IS NOT NULL)
);
CREATE UNIQUE INDEX "IX_SystemMetricSamples_Metric_Dims_Window"
  ON "SystemMetricSamples" ("MetricName", "DimensionHash", "WindowStart", "WindowSize");
CREATE INDEX "IX_SystemMetricSamples_Metric_Window"
  ON "SystemMetricSamples" ("MetricName", "WindowStart" DESC);
```

### 8.2 `SystemAlertRule` → `SystemAlertRules` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `Name` | `varchar(100)` | no | Unique |
| `MetricName` | `varchar(100)` | no | |
| `Aggregation` | `varchar(16)` | no | `Avg`, `Max`, `Min`, `Sum`, `Rate`, `Count` |
| `ComparisonOperator` | `varchar(4)` | no | `Gt`, `Gte`, `Lt`, `Lte`, `Eq` |
| `Threshold` | `numeric(18,6)` | no | |
| `WindowSeconds` | `integer` | no | Evaluation window |
| `Severity` | `varchar(16)` | no | `Info`, `Warning`, `Critical` |
| `IsEnabled` | `boolean` | no | `true` |
| `CooldownSeconds` | `integer` | no | `300` |
| `MaxAlertsPerHour` | `integer` | no | `10` |
| `TargetRoles` | `jsonb` | no | `["owner"]` — resolved through `IRecipientResolver` |
| `DimensionFiltersJson` | `jsonb` | yes | Optional dimension narrowing |
| `CreatedByUserId` | `uuid` | no | |
| `CreatedAt` | `timestamptz` | no | |
| `UpdatedAt` | `timestamptz` | no | |
| `LastTriggeredAt` | `timestamptz` | yes | |

```sql
CREATE UNIQUE INDEX "IX_SystemAlertRules_Name" ON "SystemAlertRules" ("Name");
CREATE INDEX "IX_SystemAlertRules_Enabled" ON "SystemAlertRules" ("IsEnabled") WHERE "IsEnabled";
```

### 8.3 `SystemAlert` → `SystemAlerts` (new)

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `RuleId` | `uuid` | yes | FK → `SystemAlertRules.Id`; `NULL` = ad-hoc alert |
| `OrganizationId` | `uuid` | yes | `NULL` = system-wide |
| `MetricName` | `varchar(100)` | no | |
| `Severity` | `varchar(16)` | no | |
| `Status` | `varchar(16)` | no | `Firing`, `Acknowledged`, `Resolved` |
| `Title` | `varchar(200)` | no | |
| `Detail` | `varchar(2000)` | yes | |
| `ObservedValue` | `numeric(18,6)` | yes | |
| `Threshold` | `numeric(18,6)` | yes | |
| `OccurrenceCount` | `integer` | no | `1`; incremented while in cooldown |
| `FiredAt` | `timestamptz` | no | |
| `LastObservedAt` | `timestamptz` | no | |
| `AcknowledgedAt` | `timestamptz` | yes | |
| `AcknowledgedByUserId` | `uuid` | yes | |
| `ResolvedAt` | `timestamptz` | yes | |
| `ResolvedByUserId` | `uuid` | yes | |
| `ResolutionNote` | `varchar(500)` | yes | |
| `NotificationRecordId` | `uuid` | yes | FK → `NotificationRecords.Id` |

```sql
CREATE INDEX "IX_SystemAlerts_Status_Fired" ON "SystemAlerts" ("Status", "FiredAt" DESC);
CREATE INDEX "IX_SystemAlerts_Severity_Status" ON "SystemAlerts" ("Severity", "Status", "FiredAt" DESC);
CREATE INDEX "IX_SystemAlerts_Org_Fired"
  ON "SystemAlerts" ("OrganizationId", "FiredAt" DESC) WHERE "OrganizationId" IS NOT NULL;
```

---

## 9. Audit (shared, all feature areas)

### 9.1 `AuditLogEntry` → `AuditLogEntries` (new, append-only)

One generic table rather than one per module, because every feature area needs
the same shape and a generic table is queryable across all of them.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `Id` | `uuid` | no | PK, UUIDv7 |
| `OrganizationId` | `uuid` | yes | `NULL` for system-level actions |
| `ActorKind` | `varchar(24)` | no | `User`, `ApiKey`, `InternalService`, `System` |
| `ActorUserId` | `uuid` | yes | FK → `Users.Id` |
| `ActorApiKeyId` | `uuid` | yes | FK → `ApiKeys.Id` |
| `ActorRef` | `varchar(128)` | yes | Clerk id / `internal-service` |
| `Action` | `varchar(100)` | no | `blossom.ledger.adjust`, `org.settings.updated`, … |
| `EntityType` | `varchar(100)` | no | |
| `EntityId` | `varchar(128)` | no | |
| `BeforeJson` | `jsonb` | yes | Snapshot before the change; secrets redacted |
| `AfterJson` | `jsonb` | yes | |
| `Reason` | `varchar(500)` | yes | |
| `RequestId` | `varchar(128)` | yes | |
| `TraceId` | `uuid` | yes | |
| `IpHash` | `char(64)` | yes | SHA-256 |
| `UserAgent` | `varchar(300)` | yes | Truncated |
| `CreatedAt` | `timestamptz` | no | |

```sql
CREATE INDEX "IX_AuditLogEntries_Org_Created"
  ON "AuditLogEntries" ("OrganizationId", "CreatedAt" DESC);
CREATE INDEX "IX_AuditLogEntries_Entity"
  ON "AuditLogEntries" ("EntityType", "EntityId", "CreatedAt" DESC);
CREATE INDEX "IX_AuditLogEntries_Actor"
  ON "AuditLogEntries" ("ActorUserId", "CreatedAt" DESC);
CREATE INDEX "IX_AuditLogEntries_Action_Created"
  ON "AuditLogEntries" ("Action", "CreatedAt" DESC);
```

**Redaction rule.** `BeforeJson`/`AfterJson` must never contain a password,
secret, token, API key hash, integration credential ciphertext, or raw customer
message content. A redaction helper is applied at write time and is covered by a
test that attempts to log a secret and asserts it is absent.

---

## 10. Migration sequence

Eight migrations, ordered so each is independently deployable and reversible.
All are additive except M3, which backfills.

| # | Name | Contents | Breaking? |
| --- | --- | --- | --- |
| M1 | `AddAuditLogEntries` | `AuditLogEntries` + indexes | No |
| M2 | `AddBlossomPricingRules` | `BlossomConversionRules`, `BlossomPriceEntries`, `btree_gist` extension, exclusion constraint | No |
| M3 | `AddBlossomLedgerAndUsageAccountBalance` | `BlossomLedgerEntries`, `IdempotencyRecords`; add `BlossomGranted`, `BlossomAdjusted`, `PlanTierSnapshot`, `IsClosed`, `ClosedAt`, `ConcurrencyToken` to `UsageAccounts` + the balance `CHECK`; **backfill** (see below) | Data-mutating, additive schema |
| M4 | `AddPlanEntitlements` | `PlanEntitlements`, `PlanEntitlementOverrides`, `OrganizationSubscriptions`; seed all entitlement rows | No |
| M5 | `AddApiKeys` | `ApiKeys` + indexes | No |
| M6 | `AddAgenticStatistics` | `AgentWorkflowRuns`, `AgentStepRuns`; add the seven columns to `AiUsageRecords` | No |
| M7 | `AddApiConsumptionStatistics` | `ApiRequestMetrics`, `ApiQuotaUsage`; raw-SQL `ApiRequestLogs` with partitioning + partition functions | No |
| M8 | `AddSystemStatistics` | `SystemMetricSamples`, `SystemAlertRules`, `SystemAlerts`; add `BillingEmail`, `ContactEmail`, `Currency`, `TimeZone`, `SuspendedAt` to `Organizations` | No |

### 10.1 M3 backfill (the only data-mutating migration)

```sql
-- 1. New columns default to zero / null, so existing rows are already valid.
UPDATE "UsageAccounts"
   SET "BlossomGranted" = 0,
       "BlossomAdjusted" = 0,
       "IsClosed" = false,
       "PlanTierSnapshot" = 'Seed'   -- best available default; corrected below
 WHERE "BlossomGranted" IS NULL;

-- 2. Correct the snapshot from the owning organisation where possible.
UPDATE "UsageAccounts" a
   SET "PlanTierSnapshot" = o."PlanTier"
  FROM "Organizations" o
 WHERE a."OrganizationId" = o."Id";

-- 3. Synthesise one PeriodAllocation entry per existing period row so the
--    ledger and the projection agree from day one.
INSERT INTO "BlossomLedgerEntries"
  ("Id", "OrganizationId", "UsageAccountId", "EntryType", "BlossomDelta",
   "BlossomBalanceAfter", "Reason", "SourceKind", "CreatedAt")
SELECT gen_random_uuid(), a."OrganizationId", a."Id", 'PeriodAllocation',
       a."MonthlyBlossomLimit",
       a."MonthlyBlossomLimit" + a."BlossomGranted" - a."BlossomAdjusted" - a."BlossomUsed",
       'Backfilled from UsageAccounts during migration M3',
       'System', a."PeriodStart"
  FROM "UsageAccounts" a
 WHERE NOT EXISTS (
   SELECT 1 FROM "BlossomLedgerEntries" e
    WHERE e."UsageAccountId" = a."Id" AND e."EntryType" = 'PeriodAllocation');
```

This leaves `BlossomRemaining` numerically unchanged for every existing row,
which is what makes M3 safe to deploy without a maintenance window.

### 10.2 M7 raw-SQL step

EF Core generates the table; the migration then replaces the DDL with a
partitioned table. The generated migration's `Up` must be edited to:

1. `CREATE TABLE "ApiRequestLogs" (...) PARTITION BY RANGE ("OccurredAt");`
2. Create the child partition for the current and next day.
3. Create the SQL functions `aveline_ensure_api_request_log_partition(date)` and
   `aveline_drop_old_api_request_log_partitions(int)`.
4. Register a `pg_cron` job if the extension is available, otherwise rely on the
   `ApiRequestLogPartitionJob` hosted service.

`Down` drops the functions and the table. This is the **only** migration allowed
to hand-edit EF output, and the reason must be stated in the migration file.

### 10.3 Migration command

From the repository root, with `dotnet-tools.json` restored:

```bash
dotnet tool restore
dotnet ef migrations add AddBlossomPricingRules \
  --project Aveline.Api/Aveline.Api.csproj \
  --startup-project Aveline.Api/Aveline.Api.csproj
```

`dotnet-ef` is pinned to `10.0.11` in `dotnet-tools.json` and the file names use
the `yyyyMMddHHmmss_PascalCase` convention already present in
`Aveline.Api/Migrations/`.

---

## 11. Constraints summary (quick reference for implementation)

| # | Constraint | Why |
| --- | --- | --- |
| C-1 | `Append-only` tables (`AiUsageRecords`, `BlossomLedgerEntries`, `AuditLogEntries`) get repositories with no update or delete method | Preserves the audit guarantee that `AiUsageRecord.cs:8-15` already states |
| C-2 | Every new mutable table with a numeric balance gets an `xmin` concurrency token | Fixes D-3; there is currently no concurrency control anywhere |
| C-3 | Every new tenant table implements `ITenantEntity` and every query filters `OrganizationId` explicitly | No EF global tenant filter exists |
| C-4 | Every new monotonic key that is never exposed to clients is `bigint identity`, not `uuid` | Keeps the highest-volume tables small |
| C-5 | No table stores prompt text, tool arguments, message bodies, raw IPs, or secrets | FR-5.9, BR-6.x; a redaction test enforces it |
| C-6 | Every enum is persisted as a bounded `varchar`, never as an integer | Matches `BillingConfigurations.cs:68-70` and keeps the data readable in psql |
| C-7 | Every decimal column declares explicit precision | Matches every existing entity |
| C-8 | New migrations are additive and backfilled in a single statement per table | Deployability without a maintenance window |
