import {
  AlertTriangle,
  CalendarCheck,
  CalendarDays,
  CircleCheck,
  CircleDashed,
  CircleX,
  Clock,
  Gem,
  Info,
  Tag,
  Users,
} from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { formatCount, formatMoney } from '@/lib/format-money'
import { cn } from '@/lib/utils'
import type { SubscriptionView } from '@/lib/billing-api'

interface PlanCardProps {
  subscription: SubscriptionView
}

function formatDate(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' })
}

/**
 * How a subscription status reads to a person. The server sends the enum name verbatim, and a bare
 * "None" pill told the reader nothing: it is not a plan state, it is the absence of a billing row.
 */
function statusPresentation(status: string): {
  label: string
  explanation: string
  tone: string
  Icon: typeof Gem
} {
  switch (status.toLowerCase()) {
    case 'active':
      return {
        label: 'Active',
        explanation: 'This boutique has a billing record for the current period.',
        tone: 'border-success/40 bg-success/10 text-success',
        Icon: CircleCheck,
      }
    case 'trialing':
      return {
        label: 'Trial',
        explanation: 'A trial period is running; no charge is recorded during it.',
        tone: 'border-primary/30 bg-primary/10 text-primary',
        Icon: Clock,
      }
    case 'pastdue':
      return {
        label: 'Past due',
        explanation:
          'A period payment is outstanding. No payment provider is connected, so nothing is collected automatically.',
        tone: 'border-destructive/40 bg-destructive/10 text-destructive',
        Icon: AlertTriangle,
      }
    case 'cancelled':
      return {
        label: 'Cancelled',
        explanation: 'The subscription is cancelled and will not renew at the period end.',
        tone: 'border-warning/40 bg-warning/10 text-warning',
        Icon: CircleX,
      }
    case 'expired':
      return {
        label: 'Expired',
        explanation: 'The subscription period ended without a renewal.',
        tone: 'border-destructive/40 bg-destructive/10 text-destructive',
        Icon: CircleX,
      }
    case 'none':
      return {
        label: 'No billing record',
        explanation:
          'No subscription row exists for this boutique yet. The plan and its limits are read from the assigned tier, and nothing is charged.',
        tone: 'border-border bg-muted text-muted-foreground',
        Icon: CircleDashed,
      }
    default:
      return {
        label: status,
        explanation:
          'The server reported this status verbatim; the dashboard has no plainer wording for it.',
        tone: 'border-border bg-muted text-muted-foreground',
        Icon: Info,
      }
  }
}

/** One plan fact, with the glyph that says what kind of fact it is. */
function PlanMetric({
  icon: Icon,
  label,
  children,
}: {
  icon: typeof Gem
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="flex items-start gap-2.5">
      <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
        <Icon className="size-3.5" aria-hidden />
      </span>
      <div className="min-w-0">
        <p className="text-xs uppercase tracking-wide text-muted-foreground">{label}</p>
        <p className="mt-1 font-serif text-lg font-medium">{children}</p>
      </div>
    </div>
  )
}

/**
 * The plan the boutique is on.
 *
 * The list price is shown **only when the column holds a real value**. `PriceLkr` is never assigned
 * in the product, so it is permanently `0`; rendering it would print "LKR 0.00" as the price of a
 * paid plan. A zero is therefore reported as "no list price configured", the same rule E-12 uses
 * (C-4), and the card says *why* rather than leaving the reader to guess.
 */
export function PlanCard({ subscription }: PlanCardProps) {
  const hasListPrice = subscription.priceLkr > 0
  const status = statusPresentation(subscription.status)
  const StatusIcon = status.Icon

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex items-center gap-3">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Gem className="size-4" aria-hidden />
            </span>
            <div>
              <CardTitle className="font-serif text-lg font-medium">Plan</CardTitle>
              <CardDescription>
                {subscription.planTier} plan · billed {subscription.billingCycle.toLowerCase()}
                {subscription.cancelAtPeriodEnd ? ' · cancels at period end' : ''}
              </CardDescription>
            </div>
          </div>
          <Badge variant="outline" className={cn('gap-1', status.tone)}>
            <StatusIcon className="size-3" aria-hidden />
            {status.label}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <PlanMetric icon={Tag} label="List price">
            {hasListPrice ? (
              formatMoney(subscription.priceLkr, subscription.currency)
            ) : (
              <span className="font-sans text-sm italic text-muted-foreground">
                no list price configured
              </span>
            )}
          </PlanMetric>
          <PlanMetric icon={Users} label="Seats included">
            {formatCount(subscription.seatsIncluded)}
          </PlanMetric>
          <PlanMetric icon={CalendarDays} label="Period start">
            {formatDate(subscription.currentPeriodStart)}
          </PlanMetric>
          <PlanMetric icon={CalendarCheck} label="Period end">
            {formatDate(subscription.currentPeriodEnd)}
          </PlanMetric>
        </div>

        <div className="flex items-start gap-2 rounded-lg border border-border/70 bg-muted/30 p-3 text-xs text-muted-foreground">
          <Info className="mt-0.5 size-3.5 shrink-0 text-primary" aria-hidden />
          <div className="flex flex-col gap-1">
            <p>{status.explanation}</p>
            {!hasListPrice ? (
              <p>
                The plan has no LKR list price on file, so no amount is shown rather than a
                fabricated one. No payment provider is connected, so nothing here is a charge.
              </p>
            ) : null}
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
