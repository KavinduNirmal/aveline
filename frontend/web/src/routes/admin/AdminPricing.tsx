import { Card, CardContent } from "@/components/ui/card"
import { Info } from "lucide-react"

export function AdminPricingView() {
  return (
    <div className="space-y-6 max-w-5xl">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Pricing & Valuation Rules
        </h2>
        <p className="text-sm text-muted-foreground">
          Manage system-wide item pricing books and temporal non-overlapping rule models.
        </p>
      </div>

      <Card className="border-amber-500/30 bg-amber-500/5 shadow-xs">
        <CardContent className="p-4 flex items-start gap-3 text-xs text-foreground">
          <Info className="size-5 text-amber-600 shrink-0 mt-0.5" />
          <div className="space-y-1">
            <div className="font-semibold text-amber-900 dark:text-amber-200">
              System Policy Note: Legacy Formula Active
            </div>
            <p className="text-muted-foreground leading-relaxed">
              Pricing rules configured in this console do not alter customer billing invoices while{" "}
              <code className="bg-muted px-1.5 py-0.5 rounded font-mono">Pricing:UseLegacyFormula</code> is active.
              Dynamic rule evaluation is currently verified in staging environments.
            </p>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
