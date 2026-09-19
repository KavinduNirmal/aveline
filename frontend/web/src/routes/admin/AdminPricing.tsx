import { useState } from "react"

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { RecomputeButton } from "@/components/admin/pricing/RecomputeButton"
import { recomputePricingRule } from "@/lib/admin/api"
import { LEGACY_FORMULA_BANNER } from "@/lib/admin/pricing"
import { Info } from "lucide-react"

/**
 * The pricing surface.
 *
 * Two things the delivered console got wrong and this page fixes: the legacy-formula warning is
 * stated on every pricing view rather than assumed, and **recompute is gated on the
 * `pricing:backdate` capability** — enabled for `owner`, disabled with the accurate reason for
 * `admin`, which holds `pricing:manage` but never `pricing:backdate`.
 */
export function AdminPricingView() {
  const [ruleId, setRuleId] = useState("")

  return (
    <div className="flex flex-col gap-6 max-w-5xl">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Pricing &amp; Valuation Rules
        </h2>
        <p className="text-sm text-muted-foreground">
          System-wide price books and temporal, non-overlapping rule windows.
        </p>
      </div>

      <Card className="border-warning/30 bg-warning/5 shadow-xs">
        <CardContent className="p-4 flex items-start gap-3 text-xs text-foreground">
          <Info className="size-5 text-warning shrink-0 mt-0.5" />
          <div className="flex flex-col gap-1">
            <div className="font-semibold text-foreground">
              System policy note: legacy formula active
            </div>
            <p className="text-muted-foreground leading-relaxed">
              {LEGACY_FORMULA_BANNER}{' '}
              <code className="bg-muted px-1.5 py-0.5 rounded font-mono">
                Pricing:UseLegacyFormula
              </code>
            </p>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Recompute a pricing rule</CardTitle>
          <CardDescription className="text-xs">
            Recompute applies compensating ledger entries for a backdated rule. It is a
            capability, not a page permission: only a holder of{' '}
            <code className="bg-muted px-1 py-0.5 rounded font-mono">pricing:backdate</code> may run
            it.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <div>
            <label className="font-medium block mb-1 text-xs">Pricing rule ID (GUID)</label>
            <Input
              value={ruleId}
              onChange={(e) => setRuleId(e.target.value)}
              placeholder="00000000-0000-0000-0000-000000000000"
              className="text-xs font-mono max-w-md"
            />
          </div>
          <RecomputeButton ruleId={ruleId.trim()} recompute={recomputePricingRule} />
        </CardContent>
      </Card>
    </div>
  )
}
