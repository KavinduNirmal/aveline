import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { useState } from "react"

import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { DataTable, type Column } from "@/components/admin/data/DataTable"
import { Pagination } from "@/components/admin/data/Pagination"
import { ErrorState } from "@/components/admin/data/states/ErrorState"
import { RecomputeButton } from "@/components/admin/pricing/RecomputeButton"
import { RuleTimeline } from "@/components/admin/pricing/RuleTimeline"
import { fetchPricingRules, recomputePricingRule } from "@/lib/admin/api"
import { LEGACY_FORMULA_BANNER } from "@/lib/admin/pricing"
import { AUDIT_PAGE_SIZES } from "@/lib/admin/query-params"
import type { PricingRule } from "@/types/admin"
import { Info } from "lucide-react"

const STATUSES = ["all", "Draft", "Scheduled", "Active", "Cancelled"] as const

const columns: Column<PricingRule>[] = [
  {
    key: "scope",
    header: "Scope",
    render: (rule) => (
      <span className="text-xs font-medium text-foreground">{rule.scopeKind}</span>
    ),
  },
  {
    key: "applies",
    header: "Applies to",
    render: (rule) => (
      <span className="text-xs text-muted-foreground">
        {rule.provider ?? "any provider"} · {rule.model ?? "any model"}
      </span>
    ),
  },
  {
    key: "rate",
    header: "Units / Blossom",
    render: (rule) => (
      <span className="text-xs font-mono text-muted-foreground">{rule.unitsPerBlossom}</span>
    ),
  },
  {
    key: "rounding",
    header: "Rounding",
    render: (rule) => (
      <span className="text-xs text-muted-foreground">
        {rule.roundingMode} · {rule.roundingDecimals}dp
      </span>
    ),
  },
  {
    key: "window",
    header: "Effective window",
    render: (rule) => (
      <span className="text-xs text-muted-foreground">
        {new Date(rule.effectiveFrom).toLocaleDateString()}
        {" → "}
        {rule.effectiveTo === null
          ? "in force"
          : new Date(rule.effectiveTo).toLocaleDateString()}
      </span>
    ),
  },
  {
    key: "status",
    header: "Status",
    render: (rule) => (
      <Badge variant={rule.status === "Active" ? "default" : "secondary"} className="text-[10px]">
        {rule.status}
      </Badge>
    ),
  },
  {
    key: "version",
    header: "Version",
    render: (rule) => (
      <span className="text-xs font-mono text-muted-foreground">v{rule.version}</span>
    ),
  },
  {
    key: "action",
    header: "Recompute",
    className: "text-right",
    render: (rule) => <RecomputeButton ruleId={rule.id} recompute={recomputePricingRule} />,
  },
]

/**
 * The pricing rules surface.
 *
 * `GET /admin/pricing/rules` returns a **paged** envelope (`PricingRulePageDto`) — unlike the bare
 * price-book array — so this page has a real pager. Recompute sits on each rule and is gated on the
 * `pricing:backdate` capability, and the legacy-formula banner is stated because a pricing write can
 * succeed and change nothing.
 */
export function AdminPricingRulesView() {
  const [status, setStatus] = useState<string>("all")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)

  const rulesQuery = useQuery({
    queryKey: ["admin", "pricing-rules", { status, page, pageSize }],
    queryFn: () =>
      fetchPricingRules({
        status: status === "all" ? undefined : status,
        page,
        pageSize,
      }),
    placeholderData: keepPreviousData,
    staleTime: 15_000,
  })

  const rules = rulesQuery.data?.items ?? []
  const total = rulesQuery.data?.total ?? 0

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Pricing &amp; Valuation Rules
        </h2>
        <p className="text-sm text-muted-foreground">
          Temporal, non-overlapping conversion rules and their effective windows.
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

      <Card className="border-border shadow-xs">
        <CardHeader className="flex flex-row items-center justify-between gap-2">
          <div>
            <CardTitle className="font-serif text-base">Effective windows</CardTitle>
            <CardDescription className="text-xs">
              One bar per rule. A bar without an end is a rule still in force.
            </CardDescription>
          </div>
          <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1) }}>
            <SelectTrigger className="h-9 w-[160px] text-xs">
              <SelectValue placeholder="All statuses" />
            </SelectTrigger>
            <SelectContent>
              {STATUSES.map((value) => (
                <SelectItem key={value} value={value}>
                  {value === "all" ? "All statuses" : value}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CardHeader>
        <CardContent>
          <RuleTimeline rules={rules} />
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        {rulesQuery.isError ? (
          <ErrorState
            error={rulesQuery.error}
            title="Pricing rules could not be loaded"
            onRetry={() => void rulesQuery.refetch()}
          />
        ) : (
          <>
            <DataTable<PricingRule>
              columns={columns}
              rows={rules}
              getRowKey={(rule) => rule.id}
              state={rulesQuery.isPending ? "loading" : "ready"}
              emptyMessage="No pricing rules match this filter."
              caption="Pricing rules"
            />
            <Pagination
              page={page}
              pageSize={pageSize}
              total={total}
              pageSizes={AUDIT_PAGE_SIZES}
              onPageChange={setPage}
              onPageSizeChange={(next) => {
                setPageSize(next)
                setPage(1)
              }}
            />
          </>
        )}
      </Card>
    </div>
  )
}
