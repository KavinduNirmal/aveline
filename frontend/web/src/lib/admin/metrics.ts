import type { GrafanaDashboard } from "@/lib/admin/grafana"

/**
 * The dashboard's widget declarations — **V1–V11** of the plan's §4, which supersede the audit's
 * W1–W11 catalogue.
 *
 * The rule this encodes (Q11): **Grafana owns the platform's time series; the console owns
 * decisions and actions.** Where the two would show the same number over time, the console shows
 * the number once, points at Grafana for the series, and never builds a second copy of the chart.
 *
 * No widget here reads Prometheus. Every one of them reads a shipped admin endpoint, so the
 * console is independent of the sibling workstream's deferred PromQL proxy.
 */

export type WidgetKind = 'banner' | 'tile' | 'table' | 'notice' | 'link'

export interface DashboardWidget {
  /** The plan's catalogue id, V1–V11. */
  id: string
  title: string
  kind: WidgetKind
  /** The endpoint (and field) the widget reads. Never a Prometheus series name. */
  source: string
  /** The Grafana dashboard this widget's number links out to, when one exists. */
  grafana: GrafanaDashboard | null
  /** Why this is not simply a Grafana link, for the widgets with no Grafana equivalent. */
  rationale: string
}

export const DASHBOARD_WIDGETS: readonly DashboardWidget[] = [
  {
    id: 'V1',
    title: 'Readiness banner',
    kind: 'banner',
    source: 'GET /admin/statistics/system/overview → readiness.status, checks[]',
    grafana: null,
    rationale: 'Product readiness; no Prometheus job answers it. A failed call renders an error.',
  },
  {
    id: 'V2',
    title: 'Firing alerts',
    kind: 'table',
    source: 'GET /admin/statistics/system/alerts?status=Firing',
    grafana: null,
    rationale: 'The product alert family, deliberately separate from the operator alerts.',
  },
  {
    id: 'V3',
    title: 'Pending access requests',
    kind: 'table',
    source: 'GET /admin/requests',
    grafana: null,
    rationale: 'No Grafana equivalent exists.',
  },
  {
    id: 'V4',
    title: 'Recent business actions',
    kind: 'table',
    source: 'GET /admin/audit?pageSize=8',
    grafana: null,
    rationale: 'Audit is Postgres-only.',
  },
  {
    id: 'V5',
    title: 'Blossom reconciliation',
    kind: 'banner',
    source: 'GET /admin/orgs/{orgId}/blossoms/statement → reconciliation.isConsistent',
    grafana: 'business',
    rationale: 'The console shows the boolean; the drift series is Grafana\u2019s.',
  },
  {
    id: 'V6',
    title: 'Platform health KPIs',
    kind: 'tile',
    source: 'GET /admin/statistics/system/overview + /throughput',
    grafana: 'overview',
    rationale: 'Replaces W2/W3/W5/W6 charts: the number is the ten-second answer.',
  },
  {
    id: 'V7',
    title: 'Throughput omissions',
    kind: 'notice',
    source: 'GET /admin/statistics/system/throughput → omitted[]',
    grafana: null,
    rationale: 'This is the honesty contract, not a chart.',
  },
  {
    id: 'V8',
    title: 'Queue depths',
    kind: 'tile',
    source: 'GET /admin/statistics/system/queues',
    grafana: 'database',
    rationale: 'Tiles plus a link to the depth\u2019s cause.',
  },
  {
    id: 'V9',
    title: 'Log error hotspot',
    kind: 'tile',
    source: 'GET /admin/audit filtered to error-derived actions',
    grafana: 'overview',
    rationale: 'Derived and labelled as derived; it is the console\u2019s own action (Q4).',
  },
  {
    id: 'V10',
    title: 'Agent activity',
    kind: 'tile',
    source: 'GET /admin/statistics/agents/overview',
    grafana: 'business',
    rationale: 'The copy is the point: "no runs recorded yet" is not "not instrumented".',
  },
  {
    id: 'V11',
    title: 'Grafana entry point',
    kind: 'link',
    source: '—',
    grafana: 'overview',
    rationale: 'The point of Q11: the console routes to the real chart.',
  },
]

/** Widget ids the plan removed as duplicates of Grafana panels. */
export const REMOVED_WIDGETS: readonly string[] = [
  'W5 5xx volume chart',
  'W6 latency percentile chart',
  'W2 error-rate series',
  'W3 throughput series',
  'W9 blossom movement by kind',
]

export function findWidget(id: string): DashboardWidget | undefined {
  return DASHBOARD_WIDGETS.find((widget) => widget.id === id)
}
