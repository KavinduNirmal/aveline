import { useState } from "react"
import { executeBlossomOperation } from "@/lib/admin/api"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Coins } from "lucide-react"
import { toast } from "sonner"

export function AdminBlossomsView() {
  const [orgId, setOrgId] = useState("")
  const [amount, setAmount] = useState<number>(100)
  const [reason, setReason] = useState("")
  const [allowNegative, setAllowNegative] = useState(false)
  const [operation, setOperation] = useState<"credit" | "debit" | "revoke">("credit")
  const [executing, setExecuting] = useState(false)

  const handleExecute = async () => {
    if (!orgId.trim()) {
      toast.error("Organization ID (GUID) is required")
      return
    }
    if (!reason.trim()) {
      toast.error("Audit reason is mandatory for ledger operations")
      return
    }
    if (amount <= 0) {
      toast.error("Amount must be positive")
      return
    }

    setExecuting(true)
    try {
      const idempotencyKey = crypto.randomUUID()
      await executeBlossomOperation(orgId.trim(), operation, {
        amount,
        reason: reason.trim(),
        allowNegative,
        idempotencyKey,
      })
      toast.success(`Successfully recorded Blossom ${operation} of ${amount} units.`)
      setReason("")
    } catch (err: any) {
      toast.error(err?.message || `Failed to execute Blossom ${operation}`)
    } finally {
      setExecuting(false)
    }
  }

  return (
    <div className="space-y-6 max-w-4xl">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Blossom Ledger Administration
        </h2>
        <p className="text-sm text-muted-foreground">
          Perform administrative credit grants, debits, and balance revokes across boutique tenants with strict idempotency.
        </p>
      </div>

      <Card className="border-border shadow-xs">
        <CardHeader>
          <div className="flex items-center gap-2">
            <Coins className="size-5 text-primary" />
            <CardTitle className="font-serif text-lg">Post Ledger Adjustment</CardTitle>
          </div>
          <CardDescription className="text-xs">
            Every transaction is recorded permanently in the multi-tenant ledger and verified against balance projection stores.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 text-xs">
          <div>
            <label className="font-medium block mb-1">Target Organization ID (GUID)</label>
            <Input
              placeholder="e.g. 00000000-0000-0000-0000-000000000000"
              value={orgId}
              onChange={(e) => setOrgId(e.target.value)}
              className="text-xs font-mono"
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="font-medium block mb-1">Operation</label>
              <select
                value={operation}
                onChange={(e) => setOperation(e.target.value as any)}
                className="w-full text-xs h-9 px-3 rounded-md border border-input bg-background text-foreground"
              >
                <option value="credit">Credit (Deposit to Boutique)</option>
                <option value="debit">Debit (Deduct from Boutique)</option>
                <option value="revoke">Revoke (Nullify Grant)</option>
              </select>
            </div>

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
          </div>

          {operation === "debit" && (
            <div className="flex items-center gap-2 pt-1">
              <input
                type="checkbox"
                id="allowNeg"
                checked={allowNegative}
                onChange={(e) => setAllowNegative(e.target.checked)}
                className="rounded border-input text-primary focus:ring-primary"
              />
              <label htmlFor="allowNeg" className="text-xs text-muted-foreground">
                Allow negative balance (overdraft exception)
              </label>
            </div>
          )}

          <div>
            <label className="font-medium block mb-1">
              Mandatory Business Reason <span className="text-destructive">*</span>
            </label>
            <Input
              placeholder="Reason for ledger entry..."
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="text-xs"
            />
          </div>

          <div className="pt-2">
            <Button
              onClick={() => void handleExecute()}
              disabled={executing}
              className="text-xs h-9"
            >
              {executing ? "Executing Transaction..." : `Execute ${operation.toUpperCase()}`}
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
