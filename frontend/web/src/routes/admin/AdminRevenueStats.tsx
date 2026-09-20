import { useQuery } from "@tanstack/react-query"
import { useState } from "react"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ChartFrame } from "@/components/admin/charts/ChartFrame"
import { HorizontalBarChart } from "@/components/admin/charts/HorizontalBarChart"
import { KpiTile } from "@/components/admin/charts/KpiTile"
import { TimeSeriesChart } from "@/components/admin/charts/TimeSeriesChart"
import { RangePresets } from "@/components/admin/kpi/RangePresets"
import type { RangePreset } from "@/components/admin/kpi/RangePresets"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import {
  fetchIncomeOverview,
  fetchRevenueBlossomSales,
  fetchRevenueCollections,
  fetchRevenueTimeseries,
} from "@/lib/admin/api"
import { describeRevenueQuality } from "@/lib/admin/revenue-quality"
import { useRevenueWindow } from "@/lib/admin/use-revenue-window"
import type { ChartConfig } from "@/components/ui/chart"

const REVENUE_CONFIG = {
  derived: { label: "Derived", color: "var(--chart-1)" },
  verified: { label: "Verified", color: "var(--chart-2)" },
} satisfies ChartConfig

const COLLECTION_CONFIG = {
  collectionRate: { label: "Collection rate", color: "var(--chart-1)" },
} satisfies ChartConfig

const SALES_CONFIG = {
  packsSold: { label: "Packs sold", color: "var(--chart-1)" },
} satisfies ChartConfig

/**
 * The payments statistics surface (S-52…S-55).
 *
 * **Four independent reads**, deliberately not one aggregated call. The reason
 * `AdminBusinessGrowth` uses the same shape: a page that blanks because one of four aggregates
 * failed hides three working answers, and the operator cannot tell which one is broken.
 *
 * Every failure path goes through `ChartFrame` with its `state` prop, never through a message
 * rendered inside `ChartContainer` — the recorded trap where a non-chart child lands in a `0x0` box
 * and wraps one character per line.
 */
export function AdminRevenueStatsView() {
  const { can } = useAdminSession()
  const allowed = can("revenue:read")
  const [preset, setPreset] = useState<RangePreset>("30d")
  const window = useRevenueWindow(preset)

  const overview = useQuery({
    queryKey: ["admin", "revenue", "overview", window.from, window.to],
    queryFn: () => fetchIncomeOverview({ ...window, granularity: "day" }),
    enabled: allowed,
    staleTime: 60_000,
  })

  const timeseries = useQuery({
    queryKey: ["admin", "revenue", "timeseries", window.from, window.to],
    queryFn: () => fetchRevenueTimeseries({ ...window, granularity: "day" }),
    enabled: allowed,
    staleTime: 60_000,
  })

  const collections = useQuery({
    queryKey: ["admin", "revenue", "collections", window.from, window.to],
    queryFn: () => fetchRevenueCollections({ ...window, granularity: "day" }),
    enabled: allowed,
    staleTime: 60_000,
  })

  const blossoms = useQuery({
    queryKey: ["admin", "revenue", "blossoms", window.from, window.to],
    queryFn: () => fetchRevenueBlossomSales({ ...window, granularity: "day" }),
    enabled: allowed,
    staleTime: 60_000,
  })

  if (!allowed) {
    return (
      <div className="admin-container flex flex-col gap-6">
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Payments Statistics
        </h2>
        <Card className="border-border shadow-xs">
          <CardContent className="p-4 text-xs text-muted-foreground">
            Payments statistics are not available to your role.
          </CardContent>
        </Card>
      </div>
    )
  }

  const overviewData = overview.data
  const sales = blossoms.data

  return (
    <div className="admin-container flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Payments Statistics
          </h2>
          <p className="text-sm text-muted-foreground">
            List-price scheduled revenue, collection and Blossom pack sales.
          </p>
        </div>
        <RangePresets value={preset} onChange={setPreset} />
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-4">
        {/* `null` renders as "not measured", never as a formatted zero. */}
        <KpiTile
          label="MRR"
          value={overviewData?.mrr ?? null}
          unit="raw"
          source="Subscriptions.PriceLkr, monthly-normalised"
        />
        <KpiTile label="ARR" value={overviewData?.arr ?? null} unit="raw" source="MRR × 12" />
        <KpiTile
          label="ARPU"
          value={overviewData?.arpu ?? null}
          unit="raw"
          source="MRR ÷ paying organizations"
        />
        <KpiTile
          label="Paying organizations"
          value={overviewData ? overviewData.payingOrganizations : null}
          unit="count"
          source="Subscriptions with a configured price"
        />
      </div>

      {overview.isError && (
        // The overview is the one section not wrapped in a `ChartFrame`, so it needs its error
        // state stated here rather than silently rendering four "not measured" tiles — which would
        // look like four unmeasurable figures instead of one failed read.
        <Card className="border-destructive/40 bg-destructive/5 shadow-xs">
          <CardContent className="flex flex-col gap-1 p-3 text-[11px] text-foreground">
            <span className="font-medium">The revenue overview could not be loaded</span>
            <span className="text-muted-foreground">
              {String((overview.error as Error | undefined)?.message ?? "unavailable")}
            </span>
          </CardContent>
        </Card>
      )}

      {overviewData && (
        <Card className="border-border shadow-xs">
          <CardContent className="flex flex-col gap-1 p-3 text-[11px] text-muted-foreground">
            {describeRevenueQuality(overviewData.dataQuality).map((line) => (
              <span key={line}>{line}</span>
            ))}
          </CardContent>
        </Card>
      )}

      <ChartFrame
        title="Revenue over time"
        description="Derived is what the list price says should be billed; verified is what was collected."
        config={REVENUE_CONFIG}
        state={
          timeseries.isError ? "error" : (timeseries.data?.series.length ?? 0) === 0 ? "empty" : "ready"
        }
        stateMessage={
          timeseries.isError
            ? String((timeseries.error as Error | undefined)?.message ?? "unavailable")
            : "No revenue was recorded in this window."
        }
      >
        <TimeSeriesChart
          data={(timeseries.data?.series ?? []).map((point) => ({
            bucket: point.bucketStart,
            derived: point.derived,
            verified: point.verified,
          }))}
          series={[
            { dataKey: "derived", label: "Derived" },
            { dataKey: "verified", label: "Verified" },
          ]}
        />
      </ChartFrame>

      <ChartFrame
        title="Collection rate"
        description="Verified receipts against derived charges. A period with nothing billed has no rate."
        config={COLLECTION_CONFIG}
        state={
          collections.isError
            ? "error"
            : (collections.data?.series.length ?? 0) === 0
              ? "empty"
              : "ready"
        }
        stateMessage={
          collections.isError
            ? String((collections.error as Error | undefined)?.message ?? "unavailable")
            : "No billing periods in this window."
        }
      >
        <TimeSeriesChart
          data={(collections.data?.series ?? []).map((point) => ({
            bucket: point.bucketStart,
            // A `null` rate is a gap, not a zero: the chart must not draw a line through it.
            collectionRate: point.collectionRate,
          }))}
          series={[{ dataKey: "collectionRate", label: "Collection rate" }]}
        />
      </ChartFrame>

      <ChartFrame
        title="Blossom pack sales"
        description="Referenced purchases only. A top-up with no payment reference writes no income row."
        config={SALES_CONFIG}
        state={blossoms.isError ? "error" : sales ? "ready" : "empty"}
        stateMessage={
          blossoms.isError
            ? String((blossoms.error as Error | undefined)?.message ?? "unavailable")
            : "No Blossom packs were sold in this window."
        }
      >
        <HorizontalBarChart
          config={SALES_CONFIG}
          data={[
            {
              category: "Packs sold",
              count: sales?.packsSold ?? null,
              fill: "var(--chart-1)",
            },
            {
              category: "Granted without a reference",
              count: sales?.grantedWithoutReference ?? null,
              fill: "var(--chart-2)",
            },
          ]}
        />
      </ChartFrame>

      {sales && (
        <Card className="border-border shadow-xs">
          <CardHeader>
            <CardTitle className="font-serif text-base">Blossom sales detail</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-2 gap-3 text-xs lg:grid-cols-4">
            <Figure label="Packs sold" value={sales.packsSold} />
            <Figure label="List price" value={sales.listPriceLkr} />
            <Figure label="Verified" value={sales.verifiedLkr} />
            <Figure
              label="Conversion"
              value={sales.conversion === null ? "not measured" : `${sales.conversion}%`}
            />
          </CardContent>
        </Card>
      )}
    </div>
  )
}

function Figure({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[11px] text-muted-foreground">{label}</span>
      <span className="font-mono text-sm text-foreground">{value}</span>
    </div>
  )
}
