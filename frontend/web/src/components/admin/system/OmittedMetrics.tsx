import { Card, CardContent } from '@/components/ui/card'
import { ShieldCheck } from 'lucide-react'

/**
 * The system family's honesty notice.
 *
 * Every omitted name is rendered. The server omits a metric it cannot determine rather than
 * recording it as `0` (`BR-7.10`), so an omitted name is a **named gap**, not a value to
 * substitute with zero.
 */
export function OmittedMetrics({ omitted }: { omitted: readonly string[] }) {
  const names = Array.from(new Set(omitted))
  if (names.length === 0) return null

  return (
    <Card className="border-border/60 bg-muted/20 shadow-xs" data-testid="omitted-metrics">
      <CardContent className="flex items-start gap-2 p-3 text-xs text-muted-foreground">
        <ShieldCheck className="size-4 shrink-0 text-primary" />
        <div>
          <div className="font-medium text-foreground">Not measured on this host</div>
          <ul className="mt-1 list-disc flex flex-col gap-0.5 pl-4">
            {names.map((name) => (
              <li key={name} className="font-mono">
                {name}
              </li>
            ))}
          </ul>
          <p className="mt-1">
            The server omits a metric it cannot determine rather than recording it as 0.
          </p>
        </div>
      </CardContent>
    </Card>
  )
}
