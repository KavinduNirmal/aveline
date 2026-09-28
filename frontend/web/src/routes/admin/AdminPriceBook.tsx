import { useQuery } from "@tanstack/react-query"

import { Badge } from "@/components/ui/badge"
import { Card, CardContent } from "@/components/ui/card"
import { DataTable, type Column } from "@/components/admin/data/DataTable"
import { ErrorState } from "@/components/admin/data/states/ErrorState"
import { fetchPriceBook } from "@/lib/admin/api"
import { LEGACY_FORMULA_BANNER } from "@/lib/admin/pricing"
import type { PricingPriceEntry } from "@/types/admin"
import { Info } from "lucide-react"

const columns: Column<PricingPriceEntry>[] = [
  {
    key: "sku",
    header: "SKU",
    render: (entry) => (
      <span className="font-mono text-xs text-foreground">{entry.skuCode ?? "—"}</span>
    ),
  },
  {
    key: "kind",
    header: "Kind",
    render: (entry) => (
      <Badge variant="outline" className="text-[10px] font-mono">
        {entry.skuKind}
      </Badge>
    ),
  },
  {
    key: "tier",
    header: "Plan tier",
    render: (entry) => (
      <span className="text-xs text-muted-foreground">{entry.planTier ?? "any"}</span>
    ),
  },
  {
    key: "blossoms",
    header: "Blossoms",
    render: (entry) => (
      <span className="text-xs font-mono text-muted-foreground">{entry.blossomQuantity}</span>
    ),
  },
  {
    key: "price",
    header: "Price (LKR)",
    render: (entry) => (
      <span className="text-xs font-mono text-foreground">
        {entry.priceLkr.toLocaleString()}
      </span>
    ),
  },
  {
    key: "window",
    header: "Effective window",
    render: (entry) => (
      <span className="text-xs text-muted-foreground">
        {new Date(entry.effectiveFrom).toLocaleDateString()}
        {" → "}
        {entry.effectiveTo === null
          ? "in force"
          : new Date(entry.effectiveTo).toLocaleDateString()}
      </span>
    ),
  },
  {
    key: "status",
    header: "Status",
    render: (entry) => (
      <Badge
        variant={entry.status === "Active" ? "default" : "secondary"}
        className="text-[10px]"
      >
        {entry.status}
      </Badge>
    ),
  },
]

/**
 * The price book.
 *
 * Two contract details drive this page. `GET /admin/pricing/price-book` is a **bare, unpaginated
 * array** (rule 9), so there is no pager and no `page`/`pageSize` to send. And the legacy-formula
 * banner is stated here as it is on every pricing view: a price write can succeed and change
 * nothing.
 */
export function AdminPriceBookView() {
  const priceBookQuery = useQuery({
    queryKey: ["admin", "price-book"],
    queryFn: () => fetchPriceBook({}),
    staleTime: 15_000,
  })

  const entries = priceBookQuery.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Price Book
        </h2>
        <p className="text-sm text-muted-foreground">
          The resolved price entries with their effective windows. Unpaginated by contract.
        </p>
      </div>

      <Card className="border-warning/30 bg-warning/5 shadow-xs">
        <CardContent className="p-4 flex items-start gap-3 text-xs text-foreground">
          <Info className="size-5 text-warning shrink-0 mt-0.5" />
          <div className="flex flex-col gap-1">
            <div className="font-semibold text-foreground">
              System policy note: legacy formula active
            </div>
            <p className="text-muted-foreground leading-relaxed">{LEGACY_FORMULA_BANNER}</p>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        {priceBookQuery.isError ? (
          <ErrorState
            error={priceBookQuery.error}
            title="Price book could not be loaded"
            onRetry={() => void priceBookQuery.refetch()}
          />
        ) : (
          <DataTable<PricingPriceEntry>
            columns={columns}
            rows={entries}
            getRowKey={(entry) => entry.id}
            state={priceBookQuery.isPending ? "loading" : "ready"}
            emptyMessage="No price book entries."
            caption="Price book entries"
          />
        )}
      </Card>
    </div>
  )
}
