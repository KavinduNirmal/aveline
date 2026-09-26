import { describe, expect, expectTypeOf, it } from 'vitest'

import type {
  AdminOrganizationDto,
  BusinessActiveUsers,
  BusinessOrganizationUsage,
  BusinessPlanMix,
  BusinessSubscriptionTrend,
  CreditBlossomsRequest,
  DebitBlossomsRequest,
  EntitlementOverrideInput,
  RevokeBlossomsRequest,
  SystemAlertAckResponse,
  SystemAlertDto,
} from './index'

/**
 * These assertions pin the four type defects the delivered client shipped. Each one is a
 * disagreement with the C# contract, so the authority named beside it is the record.
 */
describe('AdminOrganizationDto matches AdminOrganizationDtos.cs:6-15', () => {
  it('carries the wire fields the server actually sends', () => {
    const organization = {
      id: 'org-1',
      name: 'Aveline Boutique',
      slug: 'aveline-boutique',
      clerkOrgId: 'org_clerk_1',
      ownerUserId: 'user-1',
      planTier: 'Bloom',
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-02T00:00:00Z',
    } satisfies AdminOrganizationDto

    expect(organization.clerkOrgId).toBe('org_clerk_1')
    expect(organization.ownerUserId).toBe('user-1')
    expectTypeOf<AdminOrganizationDto>().toHaveProperty('clerkOrgId')
    expectTypeOf<AdminOrganizationDto>().toHaveProperty('ownerUserId')
  })

  it('does not declare the invented ownerEmail / memberCount fields', () => {
    // Neither field is on the wire; the delivered type declared them optional and callers
    // rendered them as blank columns.
    expectTypeOf<AdminOrganizationDto>().not.toHaveProperty('ownerEmail')
    expectTypeOf<AdminOrganizationDto>().not.toHaveProperty('memberCount')
  })
})

describe('EntitlementOverrideInput matches EntitlementOverrideDtos.cs:12-18', () => {
  it('accepts the three JSON value shapes behind valueType', () => {
    const numeric = {
      key: 'max_products',
      valueType: 'Integer',
      value: 25,
      reason: 'contract term',
    } satisfies EntitlementOverrideInput

    const booleanValue = {
      key: 'custom_domain',
      valueType: 'Boolean',
      value: true,
      reason: 'pilot',
    } satisfies EntitlementOverrideInput

    const stringValue = {
      key: 'support_tier',
      valueType: 'String',
      value: 'priority',
      reason: 'enterprise',
    } satisfies EntitlementOverrideInput

    expect(numeric.value).toBe(25)
    expect(booleanValue.value).toBe(true)
    expect(stringValue.value).toBe('priority')
    expectTypeOf<EntitlementOverrideInput['value']>().toEqualTypeOf<
      number | boolean | string
    >()
  })

  it('leaves the effective window optional because the server defaults it', () => {
    expectTypeOf<EntitlementOverrideInput>().toHaveProperty('effectiveFrom')
    expectTypeOf<EntitlementOverrideInput>().toHaveProperty('effectiveTo')

    const withoutWindow = {
      key: 'max_products',
      valueType: 'Integer',
      value: 25,
      reason: 'contract term',
    } satisfies EntitlementOverrideInput
    expect(withoutWindow).not.toHaveProperty('effectiveFrom')
  })
})

describe('SystemAlertDto and SystemAlertAckResponse are two shapes, not one', () => {
  it('types the alerts-list row with ruleName', () => {
    const row = {
      id: 'alert-1',
      ruleId: 'rule-1',
      ruleName: 'api.error_rate',
      organizationId: null,
      metricName: 'api.error_rate',
      severity: 'Warning',
      status: 'Firing',
      title: 'Error rate high',
      detail: null,
      observedValue: 0.12,
      threshold: 0.05,
      occurrenceCount: 3,
      firedAt: '2026-01-01T00:00:00Z',
      lastObservedAt: '2026-01-01T00:05:00Z',
      acknowledgedAt: null,
      resolvedAt: null,
    } satisfies SystemAlertDto

    expect(row.ruleName).toBe('api.error_rate')
    expectTypeOf<SystemAlertDto>().toHaveProperty('ruleName')
  })

  it('types the acknowledge response as the entity: no ruleName, six extra mutable fields', () => {
    // The delivered type reused the list row for the acknowledge response, so a caller that
    // read `ruleName` off it got `undefined` and silently lost the label.
    expectTypeOf<SystemAlertAckResponse>().not.toHaveProperty('ruleName')
    expectTypeOf<SystemAlertAckResponse>().toHaveProperty('consecutiveOkCount')
    expectTypeOf<SystemAlertAckResponse>().toHaveProperty('acknowledgedByUserId')
    expectTypeOf<SystemAlertAckResponse>().toHaveProperty('resolvedByUserId')
    expectTypeOf<SystemAlertAckResponse>().toHaveProperty('resolutionNote')
    expectTypeOf<SystemAlertAckResponse>().toHaveProperty('notificationRecordId')

    const acknowledged = {
      id: 'alert-1',
      ruleId: 'rule-1',
      organizationId: null,
      metricName: 'api.error_rate',
      severity: 'Warning',
      status: 'Acknowledged',
      title: 'Error rate high',
      detail: null,
      observedValue: 0.12,
      threshold: 0.05,
      occurrenceCount: 3,
      consecutiveOkCount: 0,
      firedAt: '2026-01-01T00:00:00Z',
      lastObservedAt: '2026-01-01T00:05:00Z',
      acknowledgedAt: '2026-01-01T00:06:00Z',
      acknowledgedByUserId: 'user-1',
      resolvedAt: null,
      resolvedByUserId: null,
      resolutionNote: null,
      notificationRecordId: null,
    } satisfies SystemAlertAckResponse

    expect(acknowledged.status).toBe('Acknowledged')
  })
})

describe('the three Blossom request records', () => {
  it('types each verb with the record the backend binds', () => {
    expectTypeOf<CreditBlossomsRequest>().toHaveProperty('amount')
    expectTypeOf<CreditBlossomsRequest>().toHaveProperty('reason')
    expectTypeOf<DebitBlossomsRequest>().toHaveProperty('allowNegative')
    expectTypeOf<RevokeBlossomsRequest>().toEqualTypeOf<{
      ledgerEntryId: string
      reason: string
    }>()
  })
})

/**
 * Business KPIs (S-44…S-49). Each shape is pinned against the C# record it mirrors, and the two
 * fields whose *absence* would be a lie are asserted explicitly: `activeUsers` is nullable (a
 * `null` is "not measured"), and `BilledSubscriptionCount` is a separate field from
 * `OrganizationCount` rather than the same number under two names.
 */
describe('BusinessActiveUsers mirrors BusinessActiveUsersDto (BusinessKpiDtos.cs)', () => {
  it('types a measure as nullable, because the server sends null when it is unavailable', () => {
    const payload = {
      window: {
        from: '2026-09-01T00:00:00Z',
        to: '2026-09-20T00:00:00Z',
        granularity: 'day',
        timeZone: 'UTC',
        bucketCount: 20,
      },
      series: [
        {
          bucketStart: '2026-09-19T00:00:00Z',
          isPartial: false,
          activeUsers: null,
          activeOrganizations: null,
        },
      ],
      rolling: { dau: null, wau: null, mau: null, stickiness: null },
      dataQuality: {
        userAttributionAvailable: false,
        unresolvedAttributionCount: 3,
        subscriptionHistoryBackfilled: false,
        lastActivityIsReconstructed: false,
        agentMetricsUninstrumented: false,
        notes: ['attribution unavailable'],
      },
    } satisfies BusinessActiveUsers

    expect(payload.series[0].activeUsers).toBeNull()
    expect(payload.dataQuality.userAttributionAvailable).toBe(false)
    expectTypeOf<BusinessActiveUsers['series'][number]['activeUsers']>().toEqualTypeOf<
      number | null
    >()
  })
})

describe('BusinessPlanMix mirrors BusinessPlanMixDto (BusinessKpiDtos.cs)', () => {
  it('keeps the organization count and the billed count as two distinct fields', () => {
    const payload = {
      asOf: '2026-09-20T12:00:00Z',
      tiers: [
        {
          planTier: 'Seed',
          isFree: true,
          organizationCount: 8,
          activeOrganizationCount: 7,
          billedSubscriptionCount: 0,
          userCount: 8,
          monthlyPriceLkr: 0,
        },
      ],
      free: {
        organizationCount: 8,
        userCount: 8,
        monthlyPriceLkr: 0,
        shareOfOrganizations: 1,
      },
      premium: {
        organizationCount: 0,
        userCount: 0,
        monthlyPriceLkr: 0,
        shareOfOrganizations: 0,
      },
      organizationsTotal: 8,
      organizationsWithBillingRow: 0,
      totalMonthlyPriceLkr: 0,
      dataQuality: {
        userAttributionAvailable: true,
        unresolvedAttributionCount: 0,
        subscriptionHistoryBackfilled: false,
        lastActivityIsReconstructed: false,
        agentMetricsUninstrumented: false,
        notes: [],
      },
    } satisfies BusinessPlanMix

    expect(payload.tiers[0].organizationCount).not.toBe(payload.tiers[0].billedSubscriptionCount)
    expectTypeOf<BusinessPlanMix>().toHaveProperty('organizationsWithBillingRow')
  })
})

describe('BusinessOrganizationUsage mirrors BusinessOrganizationUsageDto', () => {
  it('types the recency figures as nullable and carries the reconstructed flag', () => {
    const payload = {
      metric: 'apiRequests',
      from: '2026-09-13T00:00:00Z',
      to: '2026-09-20T00:00:00Z',
      items: [
        {
          rank: 1,
          organizationId: '00000000-0000-0000-0000-000000000001',
          name: 'Atelier',
          planTier: 'Bloom',
          messagesSent: 10,
          agentRuns: 2,
          apiRequests: 100,
          blossomUnits: 5.5,
          lastActivityAt: null,
          daysSinceLastActivity: null,
        },
      ],
      totalCount: 1,
      dataQuality: {
        userAttributionAvailable: true,
        unresolvedAttributionCount: 0,
        subscriptionHistoryBackfilled: false,
        lastActivityIsReconstructed: true,
        agentMetricsUninstrumented: false,
        notes: [],
      },
    } satisfies BusinessOrganizationUsage

    expect(payload.dataQuality.lastActivityIsReconstructed).toBe(true)
    expectTypeOf<BusinessOrganizationUsage['items'][number]['lastActivityAt']>().toEqualTypeOf<
      string | null
    >()
  })
})

describe('BusinessSubscriptionTrend mirrors BusinessSubscriptionTrendDto', () => {
  it('carries the per-bucket backfill flag so the chart can mark a bucket approximate', () => {
    const payload = {
      window: {
        from: '2026-09-01T00:00:00Z',
        to: '2026-09-20T00:00:00Z',
        granularity: 'day',
        timeZone: 'UTC',
        bucketCount: 20,
      },
      series: [
        {
          bucketStart: '2026-09-19T00:00:00Z',
          isPartial: false,
          activeTotal: 4,
          activeByTier: { Seed: 2, Bloom: 2 },
          started: 1,
          cancelled: 0,
          isBackfilled: true,
        },
      ],
      openingActive: 3,
      closingActive: 4,
      churnRate: 0,
      dataQuality: {
        userAttributionAvailable: true,
        unresolvedAttributionCount: 0,
        subscriptionHistoryBackfilled: true,
        lastActivityIsReconstructed: false,
        agentMetricsUninstrumented: false,
        notes: [],
      },
    } satisfies BusinessSubscriptionTrend

    expect(payload.series[0].isBackfilled).toBe(true)
    expect(payload.series[0].activeByTier.Seed).toBe(2)
  })
})
