import { useCallback, useState } from "react"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { IdempotentActionButton } from "@/components/admin/blossoms/IdempotentActionButton"
import type { OperationSnapshot } from "@/lib/admin/idempotency"
import {
  creditBlossoms,
  debitBlossoms,
  fetchBlossomStatement,
  revokeBlossoms,
} from "@/lib/admin/api"
import type { BlossomStatement } from "@/types/admin"
import { Coins, ShieldAlert } from "lucide-react"

type Verb = "credit" | "debit" | "revoke"

/**
 * The ledger's write surface.
 *
 * The delivered page sent `{ amount, reason, allowNegative }` for **all three** verbs, so
 * `revoke` — which binds `RevokeBlossomsRequest(Guid LedgerEntryId, string Reason)` — could never
 * succeed. It also minted the idempotency key inside the submit handler, so a retry produced a
 * second distinct operation. Both are fixed here: the verbs are split, the key belongs to the
 * operation, and the statement's reconciliation banner is rendered rather than assumed.
 */
export function AdminBlossomsView() {
  const { can } = useAdminSession()

  const [orgId, setOrgId] = useState("")
  const [ledgerEntryId, setLedgerEntryId] = useState("")
  const [amount, setAmount] = useState<number>(100)
  const [reason, setReason] = useState("")
  const [allowNegative, setAllowNegative] = useState(false)
  const [operation, setOperation] = useState<Verb>("credit")

  const [statement, setStatement] = useState<BlossomStatement | null>(null)
  const [statementError, setStatementError] = useState<string | null>(null)
  const [loadingStatement, setLoadingStatement] = useState(false)

  const snapshot: OperationSnapshot =
    operation === "revoke"
      ? {
          verb: "revoke",
          organizationId: orgId.trim(),
          reason: reason.trim(),
          ledgerEntryId: ledgerEntryId.trim(),
        }
      : operation === "debit"
        ? {
            verb: "debit",
            organizationId: orgId.trim(),
            reason: reason.trim(),
            amount,
            allowNegative,
          }
        : { verb: "credit", organizationId: orgId.trim(), reason: reason.trim(), amount }

  const execute = useCallback(
    async ({ key, snapshot: current }: { key: string; snapshot: OperationSnapshot }) => {
      if (current.verb === "revoke") {
        const result = await revokeBlossoms(
          current.organizationId,
          { ledgerEntryId: current.ledgerEntryId ?? "", reason: current.reason },
          key,
        )
        return { replayed: result.replayed }
      }
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

  const loadStatement = useCallback(async () => {
    if (!orgId.trim()) return
    setLoadingStatement(true)
    setStatementError(null)
    try {
      setStatement(await fetchBlossomStatement(orgId.trim(), { pageSize: 25 }))
    } catch (err: unknown) {
      setStatement(null)
      setStatementError(err instanceof Error ? err.message : "Statement unavailable")
    } finally {
      setLoadingStatement(false)
    }
  }, [orgId])

  const canWrite = can("billing:adjust")

  return (
    <div className="space-y-6 max-w-5xl">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Blossom Ledger Administration
        </h2>
        <p className="text-sm text-muted-foreground">
          Administrative credit grants, debits and revokes, each keyed to one logical operation.
        </p>
      </div>

      {!canWrite && (
        <Card className="border-destructive/30 bg-destructive/5 shadow-xs">
          <CardContent className="p-4 flex items-center gap-2 text-xs text-foreground">
            <ShieldAlert className="size-4 text-destructive shrink-0" />
            Writing to the ledger requires the billing:adjust grant.
          </CardContent>
        </Card>
      )}

      <Card className="border-border shadow-xs">
        <CardHeader>
          <div className="flex items-center gap-2">
            <Coins className="size-5 text-primary" />
            <CardTitle className="font-serif text-lg">Post Ledger Adjustment</CardTitle>
          </div>
          <CardDescription className="text-xs">
            Every entry is recorded permanently and reconciled against the balance projection.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 text-xs">
          <div>
            <label className="font-medium block mb-1">Target Organization ID (GUID)</label>
            <Input
              placeholder="00000000-0000-0000-0000-000000000000"
              value={orgId}
              onChange={(e) => setOrgId(e.target.value)}
              className="text-xs font-mono"
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="font-medium block mb-1">Operation</label>
              <Select value={operation} onValueChange={(value) => setOperation(value as Verb)}>
                <SelectTrigger className="w-full text-xs h-9">
                  <SelectValue placeholder="Select an operation" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="credit">Credit (deposit to boutique)</SelectItem>
                  <SelectItem value="debit">Debit (deduct from boutique)</SelectItem>
                  <SelectItem value="revoke">Revoke (nullify a grant)</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {operation === "revoke" ? (
              <div>
                <label className="font-medium block mb-1">Ledger entry ID (GUID)</label>
                <Input
                  placeholder="the grant being revoked"
                  value={ledgerEntryId}
                  onChange={(e) => setLedgerEntryId(e.target.value)}
                  className="text-xs font-mono"
                />
              </div>
            ) : (
              <div>
                <label className="font-medium block mb-1">Amount</label>
                <Input
                  type="number"
                  min="1"
                  value={amount}
                  onChange={(e) => setAmount(Number(e.target.value))}
                  className="text-xs font-mono"
                />
              </div>
            )}
          </div>

          {operation === "debit" && (
            <div className="flex items-center gap-2 pt-1">
              <Switch id="allowNeg" checked={allowNegative} onCheckedChange={setAllowNegative} />
              <label htmlFor="allowNeg" className="text-xs text-muted-foreground">
                Allow negative balance (overdraft exception)
              </label>
            </div>
          )}

          <div>
            <label className="font-medium block mb-1">
              Mandatory business reason <span className="text-destructive">*</span>
            </label>
            <Input
              placeholder="Reason for the ledger entry…"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="text-xs"
            />
          </div>

          <IdempotentActionButton
            snapshot={snapshot}
            execute={execute}
            label={`Execute ${operation.toUpperCase()}`}
            disabled={
              !canWrite ||
              orgId.trim().length === 0 ||
              reason.trim().length === 0 ||
              (operation === "revoke" && ledgerEntryId.trim().length === 0)
            }
            onSettled={(outcome) => {
              if (outcome === "success" || outcome === "replayed") {
                setReason("")
                void loadStatement()
              }
            }}
          />
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        <CardHeader className="flex flex-row items-center justify-between gap-2">
          <div>
            <CardTitle className="font-serif text-base">Ledger statement</CardTitle>
            <CardDescription className="text-xs">
              The balance is read fresh every time and never cached.
            </CardDescription>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="text-xs"
            onClick={() => void loadStatement()}
            disabled={loadingStatement || orgId.trim().length === 0}
          >
            {loadingStatement ? "Loading…" : "Load statement"}
          </Button>
        </CardHeader>

        {statementError !== null && (
          <CardContent className="text-xs text-muted-foreground">
            Statement unavailable — reconciliation status unknown: {statementError}
          </CardContent>
        )}

        {statement !== null && (
          <>
            <CardContent className="pt-0">
              <div
                className={
                  statement.reconciliation.isConsistent
                    ? "rounded-md border border-success/40 bg-success/5 p-3 text-xs text-foreground"
                    : "rounded-md border border-destructive/40 bg-destructive/5 p-3 text-xs text-foreground"
                }
              >
                <div className="font-medium">
                  {statement.reconciliation.isConsistent
                    ? "Reconciliation consistent"
                    : "Reconciliation drift detected"}
                </div>
                <div className="mt-1 text-muted-foreground">
                  projected {statement.reconciliation.projectedBalance} · ledger-derived{" "}
                  {statement.reconciliation.ledgerDerivedBalance} · drift{" "}
                  {statement.reconciliation.drift}
                </div>
              </div>
            </CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>When</TableHead>
                  <TableHead>Kind</TableHead>
                  <TableHead>Delta</TableHead>
                  <TableHead>Balance after</TableHead>
                  <TableHead>Reason</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {statement.items.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={5}
                      className="py-6 text-center text-xs text-muted-foreground"
                    >
                      No ledger entries in this window.
                    </TableCell>
                  </TableRow>
                ) : (
                  statement.items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell className="text-xs text-muted-foreground">
                        {new Date(item.occurredAt).toLocaleString()}
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline" className="text-[10px] font-mono">
                          {item.kind}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-xs font-mono">{item.blossomDelta}</TableCell>
                      <TableCell className="text-xs font-mono">{item.balanceAfter}</TableCell>
                      <TableCell className="text-xs text-muted-foreground">{item.reason}</TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </>
        )}
      </Card>
    </div>
  )
}
