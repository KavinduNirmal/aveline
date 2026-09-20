import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'

import { grafanaLink, type GrafanaDashboard } from '@/lib/admin/grafana'
import { ExternalLink } from 'lucide-react'

/**
 * A Grafana deep link (Q11).
 *
 * The console **routes** to the platform's time series rather than redrawing them. When Grafana
 * is not configured the control is **disabled with a stated reason** rather than rendering a
 * link that 404s — the published port is dev-only and no production route exists in the
 * repository.
 */
export function GrafanaLink({
  dashboard = 'overview',
  label = 'Platform metrics — Grafana',
}: {
  dashboard?: GrafanaDashboard
  label?: string
}) {
  const link = grafanaLink(dashboard)

  return (
    <Card className="border-border shadow-xs" data-testid="grafana-link">
      <CardContent className="flex flex-wrap items-center justify-between gap-3 p-4">
        <div>
          <div className="text-sm font-medium text-foreground">{label}</div>
          <div className="text-[11px] text-muted-foreground">
            {link.enabled
              ? 'Time series, saturation and database panels over the provisioned dashboard.'
              : (link.reason ?? 'Grafana is not configured for this environment')}
          </div>
        </div>
        {link.enabled ? (
          <Button asChild variant="outline" size="sm" className="gap-1.5 text-xs">
            <a href={link.href ?? '#'} target="_blank" rel="noreferrer noopener">
              Open Grafana
              <ExternalLink className="size-3.5" />
            </a>
          </Button>
        ) : (
          <Button variant="outline" size="sm" className="text-xs" disabled>
            Open Grafana
          </Button>
        )}
      </CardContent>
    </Card>
  )
}
