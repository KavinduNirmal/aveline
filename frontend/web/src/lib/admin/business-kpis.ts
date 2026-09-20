/**
 * The business-KPI widget catalogue — **B1…B12**, deliberately a **separate file** from
 * `lib/admin/metrics.ts`.
 *
 * Two reasons, both load-bearing:
 *
 * 1. `metrics.test.ts` pins `DASHBOARD_WIDGETS` to exactly `V1…V11`. Adding a twelfth entry there
 *    is a test failure, not a feature — that catalogue is a frozen contract.
 * 2. The two surfaces answer different questions. Q11 governs the triage catalogue (*"is anything
 *    wrong, and what do I do about it?"*) and should keep governing it. This catalogue is about
 *    **business state** (users, organizations, subscriptions, usage), whose authoritative store is
 *    Postgres, not Prometheus.
 *
 * No widget here names a Prometheus series: the console's boundary test forbids it, and these
 * numbers do not come from Prometheus.
 */

export type BusinessWidgetKind = 'tile' | 'trend' | 'table' | 'distribution'

export interface BusinessWidget {
  /** The catalogue id, B1…B12. */
  id: string
  title: string
  kind: BusinessWidgetKind
  /** The endpoint and field the widget reads. Never a Prometheus series name. */
  source: string
  /** The KPI definition, verbatim from `docs/backend/statistics-catalog.md`. */
  definition: string
  /** The data-quality flag this widget's honesty depends on, when it depends on one. */
  dataQualityDependency?: BusinessDataQualityKey
}

/** The keys of the server's `dataQuality` block, mirrored so the catalogue can name one. */
export type BusinessDataQualityKey =
  | 'userAttributionAvailable'
  | 'unresolvedAttributionCount'
  | 'subscriptionHistoryBackfilled'
  | 'lastActivityIsReconstructed'
  | 'agentMetricsUninstrumented'
  | 'notes'

export const BUSINESS_WIDGETS: readonly BusinessWidget[] = [
  {
    id: 'B1',
    title: 'New users',
    kind: 'tile',
    source: '/api/v1/admin/statistics/business/growth → totals.newUsers',
    definition:
      'COUNT(*) FROM Users WHERE CreatedAt ∈ bucket AND DeletedAt IS NULL. Local first-seen time, not Clerk created_at.',
  },
  {
    id: 'B2',
    title: 'New boutiques',
    kind: 'tile',
    source: '/api/v1/admin/statistics/business/growth → totals.newOrganizations',
    definition: 'COUNT(*) FROM Organizations WHERE CreatedAt ∈ bucket.',
  },
  {
    id: 'B3',
    title: 'Signups',
    kind: 'trend',
    source: '/api/v1/admin/statistics/business/growth → series[].newUsers, series[].newOrganizations',
    definition:
      'New users and new organizations per bucket. Buckets before observedFrom are null, never 0; the leading and trailing buckets are isPartial.',
  },
  {
    id: 'B4',
    title: 'Access requests',
    kind: 'tile',
    source: '/api/v1/admin/statistics/business/growth → totals.newAdminRequests, totals.approvedAdminRequests',
    definition:
      'COUNT(*) FROM AdminApprovalRequests WHERE RequestedAt ∈ bucket, and the same restricted to Status = Approved.',
  },
  {
    id: 'B5',
    title: 'Daily active users',
    kind: 'tile',
    source: '/api/v1/admin/statistics/business/active-users → rolling.dau',
    definition:
      'COUNT(DISTINCT UserId) over the trailing 1 day of ApiRequestMetrics day rows. An authenticated user who sent at least one request in the window.',
    dataQualityDependency: 'userAttributionAvailable',
  },
  {
    id: 'B6',
    title: 'Active users',
    kind: 'trend',
    source: '/api/v1/admin/statistics/business/active-users → series[].activeUsers, series[].activeOrganizations',
    definition:
      'Distinct active users and organizations per bucket. When no row is attributed the series is null with userAttributionAvailable = false, never 0.',
    dataQualityDependency: 'userAttributionAvailable',
  },
  {
    id: 'B7',
    title: 'Stickiness',
    kind: 'tile',
    source: '/api/v1/admin/statistics/business/active-users → rolling.stickiness',
    definition: 'Dau / Mau over the trailing 1 and 30 days. Null when Mau is unavailable or zero.',
    dataQualityDependency: 'userAttributionAvailable',
  },
  {
    id: 'B8',
    title: 'Plan mix',
    kind: 'distribution',
    source: '/api/v1/admin/statistics/business/plan-mix → tiers[]',
    definition:
      'OrganizationCount per tier from Organizations.PlanTier, which is authoritative for every organization. Free = Seed; premium = Bloom, Orchid, Rose, Enterprise.',
  },
  {
    id: 'B9',
    title: 'Premium share',
    kind: 'tile',
    source: '/api/v1/admin/statistics/business/plan-mix → premium.shareOfOrganizations',
    definition:
      'Premium organizations as a share of all organizations. BilledSubscriptionCount is deliberately a separate field: an organization that never changed plan has no billing row.',
  },
  {
    id: 'B10',
    title: 'Subscriptions',
    kind: 'trend',
    source: '/api/v1/admin/statistics/business/subscriptions → series[].activeTotal, series[].activeByTier',
    definition:
      'Active organizations by tier over time, counted from the daily snapshot. Buckets reconstructed from the audit ledger are marked approximate.',
    dataQualityDependency: 'subscriptionHistoryBackfilled',
  },
  {
    id: 'B11',
    title: 'Usage',
    kind: 'trend',
    source: '/api/v1/admin/statistics/business/usage → series[].messagesSent, agentRuns, apiRequests, blossomUnits',
    definition:
      'Messages, agent runs, API calls, Blossom units and actual AI cost per bucket. A platform total includes unattributed API requests (BR-6.1); a scoped read excludes them.',
    dataQualityDependency: 'agentMetricsUninstrumented',
  },
  {
    id: 'B12',
    title: 'Organizations by usage',
    kind: 'table',
    source: '/api/v1/admin/statistics/business/organizations → items[]',
    definition:
      'Organizations ranked by messages, agent runs, API calls or Blossom units over a window, with a greatest-of last-activity reconstruction.',
    dataQualityDependency: 'lastActivityIsReconstructed',
  },
]

export function findBusinessWidget(id: string): BusinessWidget | undefined {
  return BUSINESS_WIDGETS.find((widget) => widget.id === id)
}
