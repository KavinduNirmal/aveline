import { useQuery } from "@tanstack/react-query"
import { Link } from "react-router-dom"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { KpiTile } from "@/components/admin/charts/KpiTile"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { fetchIncomeOverview } from "@/lib/admin/api"
import { describeRevenueQuality } from "@/lib/admin/revenue-quality"
import { useRevenueWindow } from "@/lib/admin/use-revenue-window"

/**
 * The revenue domain's landing page.
 *
 * Three headline figures and the way in to the two children. The rule it keeps is the family's
 * null-versus-zero contract: `MRR` is `null` while no subscription has a list price, and it renders
 * as *"not measured"* rather than `0` — a `0` reads as "we earn nothing" when the truth is "no price
 * is configured".
 *
 * The gate is asserted from both halves: a caller without `revenue:read` sees an explanation and
 * issues **no** request, because a `403` in the network log is a worse experience than an absent
 * page.
 */
export function AdminRevenueView() {
  const { can, userId } = useAdminSession() as { can: (p: string) => boolean; userId?: string }
  const allowed = can("revenue:read")
  const window = useRevenueWindow("30d")

  // The window is spread into the key as **primitives**. Embedding the object itself would give the
  // key a new identity on every render, which React Query reads as a different query — and that is
  // not a hypothetical: the first version of this page issued 88 requests in 200 ms. A query key
  // must be structural, never referential.
  const overviewQuery = useQuery({
    queryKey: ["admin", "revenue", "overview", window.from, window.to],
    queryFn: () => fetchIncomeOverview(window),
    enabled: allowed,
    staleTime: 60_000,
  })

  if (!allowed) {
    return (
      <div className="admin-container flex flex-col gap-6">
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">Revenue</h2>
        <Card className="border-border shadow-xs">
          <CardContent className="p-4 text-xs text-muted-foreground">
            Revenue is not available to your role.
          </CardContent>
        </Card>
      </div>
    )
  }

  const overview = overviewQuery.data
  const quality = overview ? describeRevenueQuality(overview.dataQuality) : []
  const base = `/admin/${userId ?? ""}/revenue`

  return (
    <div className="admin-container flex flex-col gap-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">Revenue</h2>
        <p className="text-sm text-muted-foreground">
          Aveline&rsquo;s own income: what the list price says should be billed, and what was
          actually collected.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        {/* `null` renders as "not measured", never as `0`. */}
        <KpiTile
          label="MRR"
          value={overview?.mrr ?? null}
          unit="raw"
          source="Organizations.Subscriptions.PriceLkr, monthly-normalised"
        />
        <KpiTile
          label="ARR"
          value={overview?.arr ?? null}
          unit="raw"
          source="MRR × 12"
        />
        <KpiTile
          label="Paying organizations"
          value={overview ? overview.payingOrganizations : null}
          unit="count"
          source="Subscriptions with a configured price"
        />
      </div>

      {overview && (
        <Card className="border-border shadow-xs">
          <CardContent className="flex flex-col gap-1 p-3 text-[11px] text-muted-foreground">
            {quality.map((line) => (
              <span key={line}>{line}</span>
            ))}
          </CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Card className="border-border shadow-xs">
          <CardHeader>
            <CardTitle className="font-serif text-base">Income Ledger</CardTitle>
            <CardDescription className="text-xs">
              Every entry, with the gap between billed and collected.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild variant="outline" size="sm" className="text-xs">
              <Link to={`${base}/ledger`}>Income Ledger</Link>
            </Button>
          </CardContent>
        </Card>

        <Card className="border-border shadow-xs">
          <CardHeader>
            <CardTitle className="font-serif text-base">Payments Statistics</CardTitle>
            <CardDescription className="text-xs">
              MRR, collection rate and Blossom pack sales over time.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild variant="outline" size="sm" className="text-xs">
              <Link to={`${base}/statistics`}>Payments Statistics</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
