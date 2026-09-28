import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { KpiCard } from '@/components/dashboard/kpi/KpiCard'
import { formatCount, formatPercent } from '@/lib/format-money'
import type { ApiLatency, ApiQuotaStatus, ApiRequestCount } from '@/lib/statistics-api'

interface ApiConsumptionPanelProps {
  requests: ApiRequestCount
  latency: ApiLatency
  quota: ApiQuotaStatus
}

/**
 * API consumption, for a caller holding `stats:view`.
 *
 * The panel is only mounted when the permission is present (the parent decides), so it never
 * renders as a 403. A percentile the server withheld below its sample floor is already `null` and
 * renders as "not measured" through `KpiCard`; the panel does not substitute a zero.
 */
export function ApiConsumptionPanel({ requests, latency, quota }: ApiConsumptionPanelProps) {
  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">API consumption</CardTitle>
        <CardDescription>
          Requests this boutique made, and how close they are to the plan&apos;s quota. A percentile
          withheld for lack of samples reads &quot;not measured&quot;.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard label="Requests" value={requests.requestCount} format="count" />
          <KpiCard label="Errors" value={requests.errorCount} format="count" />
          <KpiCard label="Throttled" value={requests.throttledCount} format="count" />
          <KpiCard
            label="p95 latency"
            value={latency.p95Ms}
            format="count"
            note={latency.reason ?? '95th percentile over the window.'}
            caveat={latency.p95Ms === null ? 'below sample floor' : undefined}
          />
        </div>

        {quota.items.length > 0 ? (
          <div className="flex flex-col gap-2">
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              Quota this period
            </p>
            <ul className="flex flex-col gap-2">
              {quota.items.map((item) => (
                <li
                  key={`${item.metricKey}-${item.apiKeyId ?? 'org'}`}
                  className="flex items-center justify-between gap-4 text-sm"
                >
                  <span className="font-medium">{item.metricKey}</span>
                  <span className="text-muted-foreground">
                    {formatCount(item.used)} {item.limit === null ? 'used' : `of ${formatCount(item.limit)}`}{' '}
                    {item.percentUsed === null ? (
                      <span className="italic">not measured</span>
                    ) : (
                      <>· {formatPercent(item.percentUsed)}</>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">No quota is recorded for this plan.</p>
        )}
      </CardContent>
    </Card>
  )
}
