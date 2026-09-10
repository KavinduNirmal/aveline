import { Blossom } from '@/components/auth/Blossom'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import type {
  OrganizationProfileDto,
  OrganizationUsageSummary,
} from '@/types/organization'

interface UsagePanelProps {
  organization: OrganizationProfileDto
  usage: OrganizationUsageSummary | null
}

/** Formats an ISO timestamp as a short local date, e.g. "9 Sep 2026". */
function formatDate(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' })
}

/**
 * The Usage section of the tenant dashboard: a detailed view of the current billing
 * period's Blossom consumption against the plan allowance.
 */
export function UsagePanel({ organization, usage }: UsagePanelProps) {
  const limit = usage?.monthlyBlossomLimit ?? 0
  const used = usage?.blossomUsed ?? 0
  const remaining = usage?.blossomRemaining ?? 0
  const pct = limit > 0 ? Math.min(100, Math.round((used / limit) * 100)) : 0

  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          {organization.name}
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Usage</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
          How your boutique is spending Blossoms this billing period.
        </p>
      </div>

      {usage ? (
        <>
          {/* Headline balance */}
          <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
            <CardHeader>
              <div className="flex items-center justify-between gap-4">
                <CardTitle className="font-serif text-lg font-medium">Blossom balance</CardTitle>
                <Badge variant="outline" className="gap-1">
                  <Blossom className="size-3 text-primary" /> {organization.planTier}
                </Badge>
              </div>
              <CardDescription>
                {formatDate(usage.periodStart)} – {formatDate(usage.periodEnd)}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="flex items-baseline gap-2">
                <span className="font-serif text-5xl font-medium">
                  {remaining.toLocaleString()}
                </span>
                <span className="text-sm text-muted-foreground">
                  of {limit.toLocaleString()} Blossoms remaining
                </span>
              </div>

              {/* Progress bar */}
              <div className="mt-6">
                <div className="flex items-center justify-between text-xs text-muted-foreground">
                  <span>{used.toLocaleString()} used</span>
                  <span>{pct}%</span>
                </div>
                <div className="mt-1.5 h-2.5 w-full overflow-hidden rounded-full bg-muted">
                  <div
                    className={cn(
                      'h-full rounded-full transition-all',
                      pct >= 90
                        ? 'bg-destructive'
                        : pct >= 70
                          ? 'bg-amber-500'
                          : 'bg-gradient-to-r from-[#8b2e42] to-[#c05267]',
                    )}
                    style={{ width: `${pct}%` }}
                  />
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Stat tiles */}
          <div className="grid gap-4 sm:grid-cols-3">
            <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
              <CardHeader>
                <CardDescription>Monthly allowance</CardDescription>
                <CardTitle className="font-serif text-3xl font-medium">
                  {limit.toLocaleString()}
                </CardTitle>
              </CardHeader>
            </Card>
            <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
              <CardHeader>
                <CardDescription>Used this period</CardDescription>
                <CardTitle className="font-serif text-3xl font-medium">
                  {used.toLocaleString()}
                </CardTitle>
              </CardHeader>
            </Card>
            <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
              <CardHeader>
                <CardDescription>Remaining</CardDescription>
                <CardTitle className="font-serif text-3xl font-medium">
                  {remaining.toLocaleString()}
                </CardTitle>
              </CardHeader>
            </Card>
          </div>
        </>
      ) : (
        <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
          <CardContent>
            <p className="text-sm text-muted-foreground">Usage data unavailable.</p>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
