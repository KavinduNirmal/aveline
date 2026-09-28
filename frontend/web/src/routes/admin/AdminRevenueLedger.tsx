import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query"
import { useCallback, useMemo, useState } from "react"
import { useSearchParams } from "react-router-dom"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
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
import { IdempotentActionButton } from "@/components/admin/blossoms/IdempotentActionButton"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { fetchIncomeLedger, refundIncome, verifyIncome } from "@/lib/admin/api"
import type { OperationSnapshot } from "@/lib/admin/idempotency"
import { readListParams, writeListParams } from "@/lib/admin/query-params"
import { describeRevenueQuality } from "@/lib/admin/revenue-quality"
import { REVENUE_WINDOW_PRESETS } from "@/lib/admin/revenue-series"
import { useRevenueWindow } from "@/lib/admin/use-revenue-window"
import type { IncomeLedgerEntry, IncomeLedgerPage } from "@/types/admin"

const PAGE_SIZES = [25, 50, 100, 200] as const
const LEDGER_FILTERS = ["chargeBasis", "kind", "window"] as const
type LedgerFilter = (typeof LEDGER_FILTERS)[number]

/**
 * The income ledger register.
 *
 * The rule this page exists to keep: **derived and verified are never collapsed into one total.**
 * What a list price says should be billed and what was actually collected are two different
 * numbers, and their distance is the most important figure on the surface. A single unlabelled
 * "revenue" total would destroy the ledger's only reason to exist.
 *
 * The register is deliberately uncached server-side, so a mutation invalidates the query and the
 * balance is re-read rather than trusted.
 */
export function AdminRevenueLedgerView() {
  const { can } = useAdminSession()
  const [searchParams, setSearchParams] = useSearchParams()
  const [acting, setActing] = useState<IncomeLedgerEntry | null>(null)

  const { page, pageSize, filters } = readListParams<LedgerFilter>(searchParams, {
    pageSizes: PAGE_SIZES,
    defaultPageSize: 25,
    filters: LEDGER_FILTERS,
  })

  const window = useRevenueWindow(filters.window ?? "30d")

  const ledgerQuery = useQuery({
    queryKey: ["admin", "revenue", "ledger", { page, pageSize, ...filters, ...window }],
    queryFn: () =>
      fetchIncomeLedger({
        ...window,
        page,
        pageSize,
      }),
    // The register is the surface an operator refreshes right after acting on it.
    staleTime: 0,
    placeholderData: keepPreviousData,
  })

  const setFilter = useCallback(
    (key: LedgerFilter, value: string) => {
      const next = new URLSearchParams(searchParams)
      if (value.length === 0) next.delete(key)
      else next.set(key, value)
      // A narrowed set starts at page 1: staying on page 3 of a shrunken result shows an empty page.
      next.set("page", "1")
      setSearchParams(next, { replace: true })
    },
    [searchParams, setSearchParams],
  )

  const goToPage = useCallback(
    (nextPage: number, nextSize: number) => {
      setSearchParams(
        writeListParams<LedgerFilter>(
          { page: nextPage, pageSize: nextSize, filters },
          { pageSizes: PAGE_SIZES, defaultPageSize: 25, filters: LEDGER_FILTERS },
        ),
        { replace: true },
      )
    },
    [filters, setSearchParams],
  )

  const data = ledgerQuery.data
  const filtered = useMemo(() => {
    if (!data) return []
    // The charge-basis and kind filters are applied here rather than server-side because the ledger
    // endpoint does not accept them yet; the server-side filters arrive with the read API's next
    // revision. Until then this is a page-local narrowing, and the totals below are the server's.
    return data.items.filter((entry) => {
      if (filters.chargeBasis && entry.chargeBasis !== filters.chargeBasis) return false
      if (filters.kind && entry.kind !== filters.kind) return false
      return true
    })
  }, [data, filters.chargeBasis, filters.kind])

  const columns = useMemo<Column<IncomeLedgerEntry>[]>(
    () => [
      {
        key: "occurredAt",
        header: "When",
        className: "text-xs text-muted-foreground whitespace-nowrap",
        render: (row) => new Date(row.occurredAt).toLocaleDateString(),
      },
      {
        key: "kind",
        header: "Kind",
        render: (row) => <span className="text-[10px] font-mono">{row.kind}</span>,
      },
      {
        key: "basis",
        header: "Basis",
        // Visually and textually distinct, so the two classes cannot be read as interchangeable.
        render: (row) => (
          <Badge
            variant={row.chargeBasis === "Verified" ? "default" : "outline"}
            className="text-[10px]"
          >
            {row.chargeBasis}
          </Badge>
        ),
      },
      {
        key: "status",
        header: "Status",
        render: (row) => (
          <span
            className={
              row.status === "Voided" ? "text-[10px] text-muted-foreground" : "text-[10px]"
            }
          >
            {row.status}
          </span>
        ),
      },
      {
        key: "amount",
        header: "Amount",
        className: "text-right font-mono text-xs",
        render: (row) => row.amount,
      },
      {
        key: "reason",
        header: "Reason",
        className: "text-xs text-muted-foreground",
        render: (row) => row.reason,
      },
      {
        key: "actions",
        header: "Actions",
        className: "text-right",
        render: (row) => {
          // Only a live derived expectation can be settled, and only a verified receipt can be
          // refunded. A control that cannot succeed is worse than no control.
          if (row.status !== "Recorded") return null
          if (can("revenue:manage") && row.chargeBasis === "Derived") {
            return (
              <Button variant="outline" size="sm" className="text-xs" onClick={() => setActing(row)}>
                Verify
              </Button>
            )
          }
          if (can("revenue:refund") && row.chargeBasis === "Verified" && row.kind !== "Refund") {
            return (
              <Button variant="outline" size="sm" className="text-xs" onClick={() => setActing(row)}>
                Refund
              </Button>
            )
          }
          return null
        },
      },
    ],
    [can],
  )

  return (
    <div className="admin-container flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Income Ledger
          </h2>
          <p className="text-sm text-muted-foreground">
            Every revenue entry, and the distance between what was billed and what was collected.
          </p>
        </div>
        <span className="text-[11px] text-muted-foreground">Currency LKR</span>
      </div>

      {data && <ReconciliationTotals data={data} />}
      {data && <QualityNotes data={data} />}

      <Card className="border-border shadow-xs">
        <CardHeader className="flex flex-col gap-3">
          <div>
            <CardTitle className="font-serif text-base">Entries</CardTitle>
            <CardDescription className="text-xs">
              A derived row is an expectation; a verified row is money an operator confirmed.
            </CardDescription>
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <label className="flex flex-col gap-1 text-xs font-medium">
              Basis
              <Select
                value={filters.chargeBasis ?? "all"}
                onValueChange={(value) => setFilter("chargeBasis", value === "all" ? "" : value)}
              >
                <SelectTrigger className="h-9 w-full text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Both</SelectItem>
                  <SelectItem value="Derived">Derived only</SelectItem>
                  <SelectItem value="Verified">Verified only</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label className="flex flex-col gap-1 text-xs font-medium">
              Window
              <Select
                value={filters.window ?? "30d"}
                onValueChange={(value) => setFilter("window", value)}
              >
                <SelectTrigger className="h-9 w-full text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {REVENUE_WINDOW_PRESETS.map((preset) => (
                    <SelectItem key={preset} value={preset}>
                      Last {preset}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </label>

            <label className="flex flex-col gap-1 text-xs font-medium">
              Kind
              <Input
                value={filters.kind ?? ""}
                placeholder="SubscriptionCharge"
                className="h-9 text-xs"
                onChange={(event) => setFilter("kind", event.target.value)}
              />
            </label>
          </div>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {ledgerQuery.isError ? (
            <ErrorState
              error={ledgerQuery.error}
              title="The income ledger could not be loaded"
              onRetry={() => void ledgerQuery.refetch()}
            />
          ) : (
            <>
              <DataTable
                columns={columns}
                rows={filtered}
                getRowKey={(row) => row.id}
                state={ledgerQuery.isLoading ? "loading" : "ready"}
                emptyMessage="No revenue entries in this window."
                caption="Income ledger"
              />
              {(data?.total ?? 0) > 0 && (
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={data?.total ?? 0}
                  pageSizes={PAGE_SIZES}
                  onPageChange={(next) => goToPage(next, pageSize)}
                  onPageSizeChange={(size) => goToPage(1, size)}
                />
              )}
            </>
          )}
        </CardContent>
      </Card>

      {acting !== null && (
        <RevenueActionDialog
          entry={acting}
          canRefund={can("revenue:refund")}
          onClose={() => setActing(null)}
        />
      )}
    </div>
  )
}

/**
 * The window's totals, as **three labelled figures**.
 *
 * `unverifiedGap` is a magnitude, matching the server: a receipt with no matching charge is as much
 * a finding as a charge with no receipt, and a signed subtraction would report the first as a
 * negative number that reads like a typo.
 */
function ReconciliationTotals({ data }: { data: IncomeLedgerPage }) {
  const totals = data.reconciliation
  const figures = [
    { label: "Derived (billed)", value: totals.derivedTotal },
    { label: "Verified (collected)", value: totals.verifiedTotal },
    { label: "Unverified gap", value: totals.unverifiedGap },
    { label: "Refunded", value: totals.refundTotal },
    { label: "Net verified", value: totals.netVerified },
  ]

  return (
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-5">
      {figures.map((figure) => (
        <div key={figure.label} className="rounded-md border border-border p-3">
          <dt className="text-[11px] text-muted-foreground">{figure.label}</dt>
          <dd className="font-mono text-sm text-foreground">{figure.value}</dd>
        </div>
      ))}
    </dl>
  )
}

/** The honesty block. A flag that is false names itself rather than being left to inference. */
function QualityNotes({ data }: { data: IncomeLedgerPage }) {
  const lines = describeRevenueQuality(data.dataQuality)

  return (
    <Card className="border-border shadow-xs">
      <CardContent className="flex flex-col gap-1 p-3 text-[11px] text-muted-foreground">
        {lines.map((line) => (
          <span key={line}>{line}</span>
        ))}
      </CardContent>
    </Card>
  )
}

/** Verify, refund or adjust one entry, from the row it applies to. */
function RevenueActionDialog({
  entry,
  canRefund,
  onClose,
}: {
  entry: IncomeLedgerEntry
  canRefund: boolean
  onClose: () => void
}) {
  const queryClient = useQueryClient()
  const isRefund = entry.chargeBasis === "Verified" && canRefund
  const [amount, setAmount] = useState(entry.amount)
  const [reason, setReason] = useState("")

  const snapshot: OperationSnapshot = isRefund
    ? {
        verb: "refund",
        organizationId: entry.organizationId,
        sourceRef: entry.sourceRef ?? "",
        amount,
        reason: reason.trim(),
      }
    : {
        verb: "credit",
        organizationId: entry.organizationId,
        sourceRef: entry.sourceRef ?? "",
        amount,
        reason: reason.trim(),
      }

  const execute = useCallback(
    async ({ key, snapshot: current }: { key: string; snapshot: OperationSnapshot }) => {
      if (isRefund) {
        const result = await refundIncome(
          {
            organizationId: current.organizationId,
            sourceKind: entry.sourceKind,
            sourceRef: current.sourceRef ?? "",
            amount: current.amount ?? 0,
            reason: current.reason,
          },
          key,
        )
        return { replayed: result.replayed }
      }

      const result = await verifyIncome(
        {
          organizationId: current.organizationId,
          sourceKind: entry.sourceKind,
          sourceRef: current.sourceRef ?? "",
          amount: current.amount ?? 0,
          reason: current.reason,
        },
        key,
      )
      return { replayed: result.replayed }
    },
    [entry.sourceKind, isRefund],
  )

  return (
    <div
      role="dialog"
      aria-label={isRefund ? "Refund entry" : "Verify entry"}
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4"
    >
      <Card className="w-full max-w-md border-border shadow-lg">
        <CardHeader>
          <CardTitle className="font-serif text-base">
            {isRefund ? "Refund this receipt" : "Confirm this receipt"}
          </CardTitle>
          <CardDescription className="text-xs">{entry.reason}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4 text-xs">
          <label className="flex flex-col gap-1 font-medium">
            Amount to settle
            <Input
              type="number"
              min="1"
              value={amount}
              className="h-9 text-xs font-mono"
              onChange={(event) => setAmount(Number(event.target.value))}
            />
          </label>

          <label className="flex flex-col gap-1 font-medium">
            Reason <span className="text-destructive">*</span>
            <Input
              value={reason}
              className="h-9 text-xs"
              onChange={(event) => setReason(event.target.value)}
            />
          </label>

          <IdempotentActionButton
            snapshot={snapshot}
            execute={execute}
            label={isRefund ? "Confirm refund" : "Confirm receipt"}
            disabled={reason.trim().length === 0}
            onSettled={(outcome) => {
              if (outcome === "success" || outcome === "replayed") {
                void queryClient.invalidateQueries({ queryKey: ["admin", "revenue"] })
                onClose()
              }
            }}
          />

          <Button variant="ghost" size="sm" className="text-xs" onClick={onClose}>
            Cancel
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
