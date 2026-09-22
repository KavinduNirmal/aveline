import { useCallback, useEffect, useState } from 'react'
import { useUser } from '@clerk/react'
import { CheckCircle2, Eye, EyeOff } from 'lucide-react'

import { Blossom } from '@/components/auth/Blossom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import {
  fetchDashboardSummary,
  fetchTenantTakings,
  type DashboardWindow,
  type TenantDashboardSummary,
  type TenantTakings,
} from '@/lib/dashboard-api'
import { isCanceledError } from '@/lib/api-error'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import { formatCount } from '@/lib/format-money'
import { hasPermission } from '@/lib/permissions'
import type {
  OrganizationProfileDto,
  OrganizationUsageSummary,
} from '@/types/organization'

import { KpiCard } from './kpi/KpiCard'
import { TakingsCard } from './kpi/TakingsCard'

interface OverviewProps {
  organization: OrganizationProfileDto
  usage: OrganizationUsageSummary | null
  role: string
  window: DashboardWindow
}

/**
 * The dashboard's landing section.
 *
 * **The landing page is one page for every role.** The full KPI strip requires `reports:view`; the
 * reduced takings card is member-level and renders for everyone. An owner who holds `reports:view`
 * can switch the strip off to see exactly what their staff see — a **client-side preview that issues
 * no request and grants no data**, because "what does my staff see?" is a support question rather
 * than a permission.
 *
 * Every absent figure is `null` and renders "not measured". The section never states a number the
 * server did not produce.
 */
export function Overview({ organization, usage, role, window }: OverviewProps) {
  const { user } = useUser()
  const canSeeStrip = hasPermission(role, 'reports:view')

  const [summary, setSummary] = useState<TenantDashboardSummary | null>(null)
  const [takings, setTakings] = useState<TenantTakings | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  // The preview toggle starts off, so an owner sees their own view by default.
  const [previewingStaffView, setPreviewingStaffView] = useState(false)

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  //
  // The two reads have different weights: the reduced takings card is what every role sees, and the
  // strip is owner/manager-only. A strip failure must not blank the landing page, so the failure is
  // absorbed here rather than routed through `fail`. Cancellation is re-thrown rather than absorbed,
  // because an aborted read must never be mistaken for "this boutique has no takings".
  const load = usePanelLoad(
    async (signal?: AbortSignal) => {
      let reduced: TenantTakings | null = null
      try {
        reduced = await fetchTenantTakings(organization.id, window, signal)
      } catch (caught) {
        if (isCanceledError(caught)) throw caught
        reduced = null
      }

      let nextSummary: TenantDashboardSummary | null = null
      if (canSeeStrip) {
        try {
          nextSummary = await fetchDashboardSummary(organization.id, window, signal)
        } catch (caught) {
          if (isCanceledError(caught)) throw caught
          nextSummary = null
        }
      }

      return { reduced, nextSummary }
    },
    ({ reduced, nextSummary }) => {
      setTakings(reduced)
      setSummary(nextSummary)
      // Only the reduced read decides the failure flag: that card exists for every role, and the
      // strip is an addition on top of it.
      setFailed(reduced === null)
    },
    () => {
      setTakings(null)
      setSummary(null)
      setFailed(true)
    },
    [organization.id, window, canSeeStrip],
  )

  const runLoad = useCallback(
    async (signal?: AbortSignal) => {
      setIsLoading(true)
      setFailed(false)
      await load(signal)
      setIsLoading(false)
    },
    [load],
  )

  useEffect(() => {
    const controller = new AbortController()
    void runLoad(controller.signal)
    return () => controller.abort()
  }, [runLoad])

  const showStrip = canSeeStrip && !previewingStaffView

  return (
    <div className="flex flex-col gap-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          {organization.name}
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">
          Good day, {user?.firstName ?? 'there'}
        </h1>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Badge className="capitalize">{organization.planTier} plan</Badge>
        {organization.logoUrl ? (
          <Badge variant="outline" className="gap-1">
            <CheckCircle2 className="size-3" aria-hidden /> Boutique active
          </Badge>
        ) : null}
        {canSeeStrip ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => setPreviewingStaffView((current) => !current)}
          >
            {previewingStaffView ? (
              <>
                <Eye className="size-3.5" aria-hidden /> Show my full view
              </>
            ) : (
              <>
                <EyeOff className="size-3.5" aria-hidden /> Preview the staff view
              </>
            )}
          </Button>
        ) : null}
      </div>

      {organization.description ? (
        <p className="max-w-2xl text-sm text-muted-foreground">{organization.description}</p>
      ) : null}

      <Separator />

      {/* The reduced takings card renders for every role, and in the owner's preview. */}
      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : takings ? (
        <TakingsCard takings={takings} />
      ) : failed ? (
        <Card>
          <CardContent className="pt-6">
            <p className="text-sm text-destructive">Could not load the boutique&apos;s takings.</p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="mt-3"
              onClick={() => void runLoad()}
            >
              Try again
            </Button>
          </CardContent>
        </Card>
      ) : null}

      {showStrip ? (
        isLoading ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-32 w-full" />
          </div>
        ) : summary ? (
          <div className="flex flex-col gap-6">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <KpiCard
                label="Gross order value"
                value={summary.sales.grossOrderValue}
                currency={summary.currency}
                description="Billed value, not cash."
                note={
                  summary.sales.grossOrderValue === null
                    ? 'No orders were placed in this window.'
                    : `${formatCount(summary.sales.orderCount)} orders`
                }
              />
              <KpiCard
                label="Average order"
                value={summary.sales.averageOrderValue}
                currency={summary.currency}
                note="Not measured when there are no orders to average."
              />
              <KpiCard
                label="Collected"
                value={summary.cash.collected}
                currency={summary.currency}
                description="Confirmed money, excluding refunds and outstanding requests."
              />
              <KpiCard
                label="Margin"
                value={summary.sales.marginAmount}
                currency={summary.currency}
                caveat={summary.sales.marginCostsComplete ? undefined : 'cost data incomplete'}
                note={
                  summary.sales.marginCostsComplete
                    ? 'Total minus recorded cost.'
                    : 'At least one order carries no wholesale cost, so this may be overstated.'
                }
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <KpiCard
                label="Outstanding"
                value={summary.cash.outstanding}
                currency={summary.currency}
                description="Requested and not yet confirmed. Not a receivable."
              />
              <KpiCard
                label="Refunded"
                value={summary.cash.refunded}
                currency={summary.currency}
                note={`${formatCount(summary.cash.refundCount)} refunds`}
              />
              <KpiCard
                label="Clients on file"
                value={summary.customers.totalCount}
                format="count"
                note={`${formatCount(summary.customers.repeatCount)} have visited twice or more`}
              />
              <KpiCard
                label="Items listed"
                value={summary.catalog.itemCount}
                format="count"
                note={`${formatCount(summary.catalog.lowStockCount)} low on stock`}
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <KpiCard
                label="Pending approvals"
                value={summary.operations.pendingApprovals}
                format="count"
                note="Orders waiting on a decision."
              />
              <KpiCard
                label="Open conversations"
                value={summary.operations.openConversations}
                format="count"
              />
              <KpiCard
                label="Stock value at cost"
                value={summary.catalog.stockValueAtCost}
                currency={summary.currency}
                description="At cost, never retail value."
              />
            </div>

            {summary.dataQuality.notes.length > 0 ? (
              <Card>
                <CardHeader>
                  <CardTitle className="font-serif text-base font-medium">
                    What these figures do not say
                  </CardTitle>
                  <CardDescription>
                    Stated rather than implied, so a blank is not read as a zero.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <ul className="flex flex-col gap-1 text-xs text-muted-foreground">
                    {summary.dataQuality.notes.map((note) => (
                      <li key={note}>{note}</li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
            ) : null}
          </div>
        ) : (
          <Card>
            <CardContent className="pt-6">
              <p className="text-sm text-muted-foreground">
                The detailed figures could not be loaded. The takings above are unaffected.
              </p>
            </CardContent>
          </Card>
        )
      ) : (
        <p className="text-xs text-muted-foreground">
          {previewingStaffView
            ? 'Previewing the staff view: the detailed figures are hidden, exactly as a staff member experiences this page.'
            : 'This view shows total earnings. Detailed figures are available to an owner, manager or supervisor.'}
        </p>
      )}

      <Separator />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
          <CardHeader>
            <div className="flex items-center justify-between gap-4">
              <CardTitle className="font-serif text-lg font-medium">Blossoms</CardTitle>
              <Badge variant="outline" className="gap-1">
                <Blossom className="size-3 text-primary" /> {organization.planTier}
              </Badge>
            </div>
            <CardDescription>Remaining this billing period.</CardDescription>
          </CardHeader>
          <CardContent>
            {usage ? (
              <div className="flex items-baseline gap-2">
                <span className="font-serif text-4xl font-medium">
                  {usage.blossomRemaining.toLocaleString()}
                </span>
                <span className="text-sm text-muted-foreground">
                  of {usage.monthlyBlossomLimit.toLocaleString()}
                </span>
              </div>
            ) : (
              // Stated as unavailable rather than rendered as a zero. The shell used to say
              // "Demo mode" here, which was a claim about the product rather than about the data.
              <p className="text-sm text-muted-foreground">
                Balance unavailable — the API did not return a usage summary for this period.
              </p>
            )}
          </CardContent>
        </Card>

        <KpiCard
          label="Pending approvals"
          value={summary?.operations.pendingApprovals ?? null}
          format="count"
          description={
            hasPermission(role, 'approvals:approve')
              ? 'Orders waiting on a decision.'
              : 'Visible to an approver.'
          }
          note={
            summary === null
              ? 'Sign in as an owner, manager or supervisor to see the queue.'
              : undefined
          }
        />

        <KpiCard
          label="Clients active"
          value={summary?.customers.activeCount ?? null}
          format="count"
          description={`Visited in the last ${summary?.customers.inactivityThresholdDays ?? 90} days.`}
          note={summary === null ? 'Available to an owner, manager or supervisor.' : undefined}
        />
      </div>
    </div>
  )
}
