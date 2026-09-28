import { Card, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { grafanaLink } from "@/lib/admin/grafana"
import { ExternalLink } from "lucide-react"

/**
 * V11 — the Grafana entry point, and the point of Q11.
 *
 * The console's role for time series is to **route**, not to redraw. When Grafana is not
 * configured every link renders **disabled with a stated reason**, because the published port is
 * dev-only and no production route exists in the repository — a wall of 404s is worse than an
 * honest "not configured".
 */
export function GrafanaCard() {
  const link = grafanaLink('overview')

  return (
    <Card className="border-border shadow-xs">
      <CardContent className="p-4 flex items-center justify-between gap-3 flex-wrap">
        <div>
          <div className="text-sm font-medium text-foreground">Platform metrics — Grafana</div>
          <div className="text-[11px] text-muted-foreground">
            {link.enabled
              ? 'Time series, saturation and database panels over the Prometheus window.'
              : (link.reason ?? 'Grafana is not configured for this environment')}
          </div>
        </div>
        {link.enabled ? (
          <Button asChild variant="outline" size="sm" className="text-xs gap-1.5">
            <a href={link.href ?? '#'} target="_blank" rel="noreferrer noopener">
              Open Grafana
              <ExternalLink className="size-3.5" />
            </a>
          </Button>
        ) : (
          <Button variant="outline" size="sm" className="text-xs" disabled>
            Grafana unavailable
          </Button>
        )}
      </CardContent>
    </Card>
  )
}
