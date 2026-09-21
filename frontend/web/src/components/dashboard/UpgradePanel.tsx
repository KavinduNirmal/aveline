import { ArrowLeft, Mail } from 'lucide-react'
import { Link } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import type { OrganizationProfileDto } from '@/types/organization'

interface UpgradePanelProps {
  organization: OrganizationProfileDto
}

/**
 * The upgrade path, reached from Usage and Billing.
 *
 * Self-serve plan changes are **deferred**: there is no checkout, no payment-provider client and no
 * invoice entity, so this page cannot take money and does not pretend to. It states the plan the
 * boutique is on, says what an upgrade involves, and hands the decision to a conversation. When
 * self-serve billing lands, this page is the one thing that changes — the CTAs already point here.
 */
export function UpgradePanel({ organization }: UpgradePanelProps) {
  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Upgrade plan</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Move this boutique to a larger plan when the work outgrows it.
          </p>
        </div>
        <Button asChild variant="outline" className="gap-1.5">
          <Link to={`/app/b/${organization.slug}/billing`}>
            <ArrowLeft className="size-4" aria-hidden />
            Back to billing
          </Link>
        </Button>
      </div>

      <Card>
        <CardContent className="flex flex-col gap-4 pt-6">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Current plan
            </p>
            <p className="mt-1 font-serif text-2xl font-medium">{organization.planTier}</p>
          </div>

          <p className="max-w-2xl text-sm text-muted-foreground">
            A plan change is arranged with us rather than applied instantly: staff seats, the
            monthly Blossom allowance and the billing cycle all move together, and we confirm the
            effective date with you before anything changes.
          </p>

          <div className="flex flex-wrap items-center gap-3">
            <Button asChild className="gap-1.5">
              <Link to="/contact">
                <Mail className="size-4" aria-hidden />
                Contact us
              </Link>
            </Button>
            <Button asChild variant="outline">
              <Link to={`/app/b/${organization.slug}/billing`}>See current entitlements</Link>
            </Button>
          </div>

          <p className="text-xs text-muted-foreground">
            No payment is taken on this page. Quoted plan prices are LKR list prices; nothing here
            is a demand for payment.
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
