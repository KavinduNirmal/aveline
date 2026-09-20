import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query"
import { useCallback, useMemo, useState } from "react"
import { useSearchParams } from "react-router-dom"

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
import { Switch } from "@/components/ui/switch"
import { DataTable, type Column } from "@/components/admin/data/DataTable"
import { Pagination } from "@/components/admin/data/Pagination"
import { ErrorState } from "@/components/admin/data/states/ErrorState"
import { IdempotentActionButton } from "@/components/admin/blossoms/IdempotentActionButton"
import { OrgPicker } from "@/components/admin/orgs/OrgPicker"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import {
  creditBlossoms,
  debitBlossoms,
  fetchBlossomStatement,
  revokeBlossoms,
} from "@/lib/admin/api"
import type { OperationSnapshot } from "@/lib/admin/idempotency"
import { readListParams, writeListParams } from "@/lib/admin/query-params"
import type { AdminOrganizationDto, BlossomStatement, BlossomStatementItem } from "@/types/admin"

const PAGE_SIZES = [25, 50, 100, 200] as const
const STATEMENT_FILTERS = ["kind", "entryType", "sourceKind", "q", "window"] as const
type StatementFilter = (typeof STATEMENT_FILTERS)[number]

/** The window presets the surface offers, in days. `null` means "the server default". */
const WINDOW_PRESETS = [
  { value: "30", label: "Last 30 days", days: 30 },
  { value: "90", label: "Last 90 days", days: 90 },
  { value: "400", label: "Last 400 days", days: 400 },
] as const

/** Resolves a window preset into the `from`/`to` the statement accepts. */
function resolveWindow(preset: string | undefined): { from?: string; to?: string } {
  if (!preset) return {}
  const chosen = WINDOW_PRESETS.find((option) => option.value === preset)
  if (!chosen) return {}
  const to = new Date()
  const from = new Date(to.getTime() - chosen.days * 24 * 60 * 60 * 1000)
  return { from: from.toISOString(), to: to.toISOString() }
}

/**
 * The Blossom ledger, redesigned around the question an operator actually asks.
 *
 * Three things changed from the delivered page, and each was a defect rather than a preference:
 *
 * 1. **The statement is the spine.** It loads as soon as an organization is chosen and is read
 *    through TanStack Query, so changing a filter refetches. The delivered page held it in component
 *    state and loaded it only from a button, so nothing appeared until the operator pressed it.
 * 2. **Filters live in the URL.** A filtered view survives the back button and is shareable.
 * 3. **An operation starts from the row it applies to.** `revoke` used to require a hand-typed
 *    ledger-entry GUID, and a grant that was not revocable was only discovered by submitting and
 *    receiving a `409`. The statement reports what is revocable, so the action is on the row and the
 *    `409` stays the server's authoritative answer.
 *
 * The statement's `staleTime` is `0` because the balance must never be cached — the delivered page
 * already documented that, and it is the reason this surface does not use the console's 30-second
 * default.
 */
export function AdminBlossomsView() {
  const { can } = useAdminSession()
  const [searchParams, setSearchParams] = useSearchParams()

  const [organization, setOrganization] = useState<AdminOrganizationDto | null>(null)
  const orgId = organization?.id ?? ""
  // The grant whose revoke dialog is open. Held as the row itself, so the dialog never has to
  // re-parse an identifier the operator would otherwise have typed by hand.
  const [revoking, setRevoking] = useState<BlossomStatementItem | null>(null)

  const { page, pageSize, filters } = readListParams<StatementFilter>(searchParams, {
    pageSizes: PAGE_SIZES,
    defaultPageSize: 25,
    filters: STATEMENT_FILTERS,
  })

  const window = resolveWindow(filters.window)

  const statementQuery = useQuery({
    queryKey: [
      "admin",
      "blossoms",
      "statement",
      orgId,
      { page, pageSize, ...filters, ...window },
    ],
    queryFn: () =>
      fetchBlossomStatement(orgId, {
        ...window,
        kind: (filters.kind as "all" | "entitlement" | "consumption" | undefined) || undefined,
        entryType: filters.entryType || undefined,
        sourceKind: filters.sourceKind || undefined,
        query: filters.q || undefined,
        page,
        pageSize,
      }),
    enabled: orgId.length > 0,
    // The balance must never be cached: a stale balance is the one thing this page cannot show.
    staleTime: 0,
    // A page change keeps the previous rows on screen instead of blanking the table.
    placeholderData: keepPreviousData,
  })

  const setFilter = useCallback(
    (key: StatementFilter, value: string) => {
      const next = new URLSearchParams(searchParams)
      if (value.length === 0) next.delete(key)
      else next.set(key, value)
      // A narrowed view starts at page 1: staying on page 4 of a result set that just shrank shows
      // an empty page and looks like a failure.
      next.set("page", "1")
      setSearchParams(next, { replace: true })
    },
    [searchParams, setSearchParams],
  )

  const goToPage = useCallback(
    (nextPage: number, nextSize: number) => {
      setSearchParams(
        writeListParams<StatementFilter>(
          { page: nextPage, pageSize: nextSize, filters },
          { pageSizes: PAGE_SIZES, defaultPageSize: 25, filters: STATEMENT_FILTERS },
        ),
        { replace: true },
      )
    },
    [filters, setSearchParams],
  )

  const statement = statementQuery.data
  const items = statement?.items ?? []
  const total = statement?.total ?? 0

  const columns = useMemo<Column<BlossomStatementItem>[]>(
    () => [
      {
        key: "occurredAt",
        header: "When",
        className: "text-xs text-muted-foreground whitespace-nowrap",
        render: (row) => new Date(row.occurredAt).toLocaleString(),
      },
      {
        key: "kind",
        header: "Kind",
        render: (row) => (
          <span className="text-[10px] font-mono text-muted-foreground">{row.kind}</span>
        ),
      },
      {
        key: "reason",
        header: "Reason",
        className: "text-xs",
        render: (row) => (
          <span className="flex flex-col gap-0.5">
            <span>{row.reason}</span>
            {/* A consumption row can now be explained: which model, how much raw usage, what it
                cost. The delivered page said "Agent workflow" and nothing else. */}
            {row.kind === "Consumption" && (row.provider || row.model) && (
              <span className="text-[10px] text-muted-foreground">
                {[row.provider, row.model].filter(Boolean).join(" · ")}
                {typeof row.normalizedUnits === "number" &&
                  ` · ${row.normalizedUnits.toLocaleString()} units`}
              </span>
            )}
          </span>
        ),
      },
      {
        key: "blossomDelta",
        header: "Delta",
        className: "text-xs font-mono text-right",
        render: (row) => row.blossomDelta,
      },
      {
        key: "balanceAfter",
        header: "Balance after",
        className: "text-xs font-mono text-right",
        render: (row) => row.balanceAfter,
      },
      {
        key: "actions",
        header: "Actions",
        className: "text-right",
        render: (row) =>
          // Only a row the server reported as revocable gets the control. The delivered page
          // offered it unconditionally and let the operator find out from a 409.
          can("billing:adjust") && row.availableToRevoke != null ? (
            <Button
              variant="outline"
              size="sm"
              className="text-xs"
              onClick={() => setRevoking(row)}
            >
              Revoke
            </Button>
          ) : null,
      },
    ],
    [can],
  )

  return (
    <div className="admin-container flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Blossom Ledger
          </h2>
          <p className="text-sm text-muted-foreground">
            Statement of account, with the reconciliation check the alerting signal uses.
          </p>
        </div>
        <span
          data-testid="statement-window-cap"
          className="text-[11px] text-muted-foreground"
        >
          Windows up to {statement?.maxWindowDays ?? 400} days
        </span>
      </div>

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Statement</CardTitle>
          <CardDescription className="text-xs">
            Choose an organization, then narrow the window. The balance is read fresh every time.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <OrgPicker value={organization} onSelect={setOrganization} label="Organization" />

          {orgId.length > 0 && (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <label className="flex flex-col gap-1 text-xs font-medium">
                Kind
                <Select
                  value={filters.kind ?? "all"}
                  onValueChange={(value) => setFilter("kind", value === "all" ? "" : value)}
                >
                  <SelectTrigger className="h-9 w-full text-xs">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Everything</SelectItem>
                    <SelectItem value="entitlement">Entitlement only</SelectItem>
                    <SelectItem value="consumption">Consumption only</SelectItem>
                  </SelectContent>
                </Select>
              </label>

              <label className="flex flex-col gap-1 text-xs font-medium">
                Window
                <Select
                  value={filters.window ?? "30"}
                  onValueChange={(value) => setFilter("window", value)}
                >
                  <SelectTrigger className="h-9 w-full text-xs">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {WINDOW_PRESETS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>

              <label className="flex flex-col gap-1 text-xs font-medium">
                Source
                <Select
                  value={filters.sourceKind ?? "any"}
                  onValueChange={(value) => setFilter("sourceKind", value === "any" ? "" : value)}
                >
                  <SelectTrigger className="h-9 w-full text-xs">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="any">Any source</SelectItem>
                    {["Admin", "PaymentProvider", "PlanChange", "Expiry", "System"].map((kind) => (
                      <SelectItem key={kind} value={kind}>
                        {kind}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>

              <label className="flex flex-col gap-1 text-xs font-medium">
                Search
                <Input
                  value={filters.q ?? ""}
                  placeholder="reason or reference"
                  className="h-9 text-xs"
                  onChange={(event) => setFilter("q", event.target.value)}
                />
              </label>
            </div>
          )}

          {statement && <StatementSummary statement={statement} />}
          {statement && <ReconciliationBanner statement={statement} />}

          {statementQuery.isError ? (
            <ErrorState
              error={statementQuery.error}
              title="The statement could not be loaded"
              onRetry={() => void statementQuery.refetch()}
            />
          ) : (
            <>
              <DataTable
                columns={columns}
                rows={items}
                getRowKey={(row) => row.id}
                state={statementQuery.isLoading ? "loading" : "ready"}
                emptyMessage="No ledger entries in this window."
                caption="Blossom statement"
              />
              {total > 0 && (
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={total}
                  pageSizes={PAGE_SIZES}
                  onPageChange={(next) => goToPage(next, pageSize)}
                  onPageSizeChange={(size) => goToPage(1, size)}
                />
              )}
            </>
          )}
        </CardContent>
      </Card>

      {orgId.length > 0 && can("billing:adjust") && <AdjustmentForm orgId={orgId} />}

      {revoking !== null && (
        <RevokeDialog
          entry={revoking}
          organizationId={orgId}
          onClose={() => setRevoking(null)}
        />
      )}
    </div>
  )
}

/**
 * Revoking one grant, started from the row it applies to.
 *
 * The dialog's `ledgerEntryId` comes from the row rather than from an input, which is the whole
 * point: the delivered page asked an operator to paste a GUID, and a mistyped identifier either
 * failed or — worse — revoked the wrong grant.
 */
function RevokeDialog({
  entry,
  organizationId,
  onClose,
}: {
  entry: BlossomStatementItem
  organizationId: string
  onClose: () => void
}) {
  const queryClient = useQueryClient()
  const [reason, setReason] = useState("")

  const snapshot: OperationSnapshot = {
    verb: "revoke",
    organizationId,
    reason: reason.trim(),
    ledgerEntryId: entry.id,
  }

  const execute = useCallback(
    async ({ key, snapshot: current }: { key: string; snapshot: OperationSnapshot }) => {
      const result = await revokeBlossoms(
        current.organizationId,
        { ledgerEntryId: current.ledgerEntryId ?? "", reason: current.reason },
        key,
      )
      return { replayed: result.replayed }
    },
    [],
  )

  return (
    <div
      role="dialog"
      aria-label="Revoke grant"
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4"
    >
      <Card className="w-full max-w-md border-border shadow-lg">
        <CardHeader>
          <CardTitle className="font-serif text-base">Revoke this grant</CardTitle>
          <CardDescription className="text-xs">
            {entry.availableToRevoke} of {entry.blossomDelta} Blossoms can still be revoked from{" "}
            &ldquo;{entry.reason}&rdquo;.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4 text-xs">
          <label className="flex flex-col gap-1 font-medium">
            Reason <span className="text-destructive">*</span>
            <Input
              value={reason}
              placeholder="Why this grant is being revoked"
              className="h-9 text-xs"
              onChange={(event) => setReason(event.target.value)}
            />
          </label>

          <IdempotentActionButton
            snapshot={snapshot}
            execute={execute}
            label="Confirm revoke"
            disabled={reason.trim().length === 0}
            onSettled={(outcome) => {
              if (outcome === "success" || outcome === "replayed") {
                void queryClient.invalidateQueries({ queryKey: ["admin", "blossoms"] })
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

/**
 * The period and its balance movement.
 *
 * A statement that shows a running balance but never states where it started and ended is not a
 * statement. The delivered page rendered the reconciliation banner and the rows, and left the
 * opening and closing figures out entirely.
 */
function StatementSummary({ statement }: { statement: BlossomStatement }) {
  const period = `${new Date(statement.periodStart).toLocaleDateString()} – ${new Date(
    statement.periodEnd,
  ).toLocaleDateString()}`

  const figures = [
    { label: "Period", value: period, mono: false },
    { label: "Opening balance", value: statement.openingBalance, mono: true },
    { label: "Closing balance", value: statement.closingBalance, mono: true },
    { label: "Entries in window", value: statement.total, mono: true },
  ]

  return (
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {figures.map((figure) => (
        <div key={figure.label} className="rounded-md border border-border p-3">
          <dt className="text-[11px] text-muted-foreground">{figure.label}</dt>
          <dd className={figure.mono ? "font-mono text-sm text-foreground" : "text-sm text-foreground"}>
            {figure.value}
          </dd>
        </div>
      ))}
    </dl>
  )
}

/**
 * The reconciliation banner, and the honesty rules that go with it.
 *
 * An unavailable reconciliation reads *"reconciliation status unknown"* and never "consistent" —
 * which is what the delivered page's `isConsistent ? ... : ...` skipped, because it had no way to
 * express a third state.
 */
function ReconciliationBanner({ statement }: { statement: BlossomStatement }) {
  const quality = statement.dataQuality
  const checked = quality?.reconciliationChecked ?? true
  const consistent = statement.reconciliation.isConsistent

  if (!checked) {
    return (
      <div className="rounded-md border border-warning/40 bg-warning/5 p-3 text-xs text-foreground">
        <div className="font-medium">Reconciliation status unknown</div>
        <p className="mt-1 text-muted-foreground">
          The reconciliation could not be evaluated, so this balance is not confirmed against the
          ledger.
        </p>
      </div>
    )
  }

  return (
    <div
      className={
        consistent
          ? "flex flex-col gap-1 rounded-md border border-success/40 bg-success/5 p-3 text-xs text-foreground"
          : "flex flex-col gap-1 rounded-md border border-destructive/40 bg-destructive/5 p-3 text-xs text-foreground"
      }
    >
      <div className="font-medium">
        {consistent ? "Reconciliation consistent" : "Reconciliation drift detected"}
      </div>
      <div className="text-muted-foreground">
        projected {statement.reconciliation.projectedBalance} · ledger-derived{" "}
        {statement.reconciliation.ledgerDerivedBalance} · drift{" "}
        {statement.reconciliation.drift}
      </div>
      {quality?.openingBalanceFromProjection && (
        <div className="text-muted-foreground">
          The opening balance ({statement.openingBalance}) is derived from the balance projection
          rather than accumulated forward from the ledger rows below.
        </div>
      )}
      {quality?.windowCapped && (
        <div className="text-muted-foreground">
          This window is clipped at the {quality.maxWindowDays}-day maximum, so it does not cover
          the full range requested.
        </div>
      )}
    </div>
  )
}

type Verb = "credit" | "debit" | "revoke"

/** The administrative write surface. Each verb is one logical operation with one idempotency key. */
function AdjustmentForm({ orgId }: { orgId: string }) {
  const queryClient = useQueryClient()
  const [verb, setVerb] = useState<Verb>("credit")
  const [amount, setAmount] = useState(100)
  const [reason, setReason] = useState("")
  const [allowNegative, setAllowNegative] = useState(false)

  const snapshot: OperationSnapshot =
    verb === "revoke"
      ? { verb: "revoke", organizationId: orgId, reason: reason.trim(), ledgerEntryId: "" }
      : verb === "debit"
        ? { verb: "debit", organizationId: orgId, reason: reason.trim(), amount, allowNegative }
        : { verb: "credit", organizationId: orgId, reason: reason.trim(), amount }

  const execute = useCallback(
    async ({ key, snapshot: current }: { key: string; snapshot: OperationSnapshot }) => {
      if (current.verb === "debit") {
        const result = await debitBlossoms(
          current.organizationId,
          {
            amount: current.amount ?? 0,
            reason: current.reason,
            allowNegative: current.allowNegative ?? false,
          },
          key,
        )
        return { replayed: result.replayed }
      }
      const result = await creditBlossoms(
        current.organizationId,
        { amount: current.amount ?? 0, reason: current.reason },
        key,
      )
      return { replayed: result.replayed }
    },
    [],
  )

  return (
    <Card className="border-border shadow-xs">
      <CardHeader>
        <CardTitle className="font-serif text-base">Post an adjustment</CardTitle>
        <CardDescription className="text-xs">
          Every entry is permanent, and each is keyed to one logical operation.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 text-xs">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <label className="flex flex-col gap-1 font-medium">
            Operation
            <Select value={verb} onValueChange={(value) => setVerb(value as Verb)}>
              <SelectTrigger className="h-9 w-full text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="credit">Credit</SelectItem>
                <SelectItem value="debit">Debit</SelectItem>
                <SelectItem value="revoke">Revoke</SelectItem>
              </SelectContent>
            </Select>
          </label>

          {verb !== "revoke" && (
            <label className="flex flex-col gap-1 font-medium">
              Amount
              <Input
                type="number"
                min="1"
                value={amount}
                className="h-9 text-xs font-mono"
                onChange={(event) => setAmount(Number(event.target.value))}
              />
            </label>
          )}
        </div>

        {verb === "revoke" && (
          <p className="text-muted-foreground">
            Revoke an individual grant from the statement above; the action is on the row it
            applies to.
          </p>
        )}

        {verb === "debit" && (
          <div className="flex items-center gap-2">
            <Switch
              id="allowNeg"
              checked={allowNegative}
              onCheckedChange={setAllowNegative}
            />
            <label htmlFor="allowNeg" className="text-muted-foreground">
              Allow negative balance (overdraft exception)
            </label>
          </div>
        )}

        <label className="flex flex-col gap-1 font-medium">
          Reason <span className="text-destructive">*</span>
          <Input
            value={reason}
            placeholder="Why this entry is being written"
            className="h-9 text-xs"
            onChange={(event) => setReason(event.target.value)}
          />
        </label>

        {verb !== "revoke" && (
          <IdempotentActionButton
            snapshot={snapshot}
            execute={execute}
            label={`Execute ${verb.toUpperCase()}`}
            disabled={reason.trim().length === 0}
            onSettled={(outcome) => {
              if (outcome === "success" || outcome === "replayed") {
                setReason("")
                // The balance just moved, so the statement is refetched rather than trusted.
                void queryClient.invalidateQueries({ queryKey: ["admin", "blossoms"] })
              }
            }}
          />
        )}
      </CardContent>
    </Card>
  )
}
