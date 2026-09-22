import { useCallback, useEffect, useState } from 'react'
import { ArrowUpRight } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { hasPermission } from '@/lib/permissions'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import {
  fetchBillingPeriods,
  fetchBlossomStatement,
  fetchEntitlements,
  fetchSubscription,
  fetchTopUpPacks,
  type BillingPeriod,
  type BlossomStatement,
  type EntitlementItem,
  type SubscriptionView,
  type TopUpPack,
} from '@/lib/billing-api'
import type { OrganizationProfileDto } from '@/types/organization'

import { BillingPeriodsTable } from './BillingPeriodsTable'
import { BlossomStatementTable } from './BlossomStatementTable'
import { EntitlementsTable } from './EntitlementsTable'
import { PlanCard } from './PlanCard'
import { TopUpDialog } from './TopUpDialog'

interface BillingPanelProps {
  organization: OrganizationProfileDto
  role: string
  /** Moves to the upgrade page. The move itself is the shell's; this only asks for it. */
  onUpgrade: () => void
}

interface BillingData {
  subscription: SubscriptionView | null
  entitlements: EntitlementItem[]
  periods: BillingPeriod[]
  statement: BlossomStatement | null
  packs: TopUpPack[]
}

const EMPTY_DATA: BillingData = {
  subscription: null,
  entitlements: [],
  periods: [],
  statement: null,
  packs: [],
}

/**
 * The Billing section: the plan, its entitlements, the billing-period history, the Blossom
 * **statement of account**, and — for a caller holding `billing:manage` — the top-up dialog.
 *
 * There is no invoice anywhere in this panel and no invoice number can be rendered, because the
 * repository has no invoice entity, no payment-provider client and no currency column (D8/Q2). The
 * top-up catalogue is fetched only when the caller may actually purchase, so the section does not
 * issue a request that would 403.
 */
export function BillingPanel({ organization, role, onUpgrade }: BillingPanelProps) {
  const canManage = hasPermission(role, 'billing:manage')
  const canView = hasPermission(role, 'billing:view')

  const [data, setData] = useState<BillingData>(EMPTY_DATA)
  const [statementPage, setStatementPage] = useState(1)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  const load = usePanelLoad(
    (signal?: AbortSignal) =>
      Promise.all([
        fetchSubscription(organization.id, signal),
        fetchEntitlements(organization.id, signal),
        fetchBillingPeriods(organization.id, 12, signal),
        fetchBlossomStatement(organization.id, { page: statementPage }, signal),
        canManage ? fetchTopUpPacks(organization.id, signal) : Promise.resolve([]),
      ]),
    ([subscription, entitlements, periods, statement, packs]) => {
      setData({ subscription, entitlements, periods, statement, packs })
    },
    () => {
      setData(EMPTY_DATA)
      setError('Could not load the billing data.')
    },
    [canManage, organization.id, statementPage],
  )

  const runLoad = useCallback(
    async (signal?: AbortSignal) => {
      setIsLoading(true)
      setError(null)
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

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Billing</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            The plan, what each period consumed, and a statement of account. On LKR list prices only:
            no payment provider is connected, so nothing here is a demand for payment.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* The plan is the subject of this page, so the route to a larger one belongs here. */}
          <Button type="button" variant="outline" onClick={onUpgrade} className="gap-1.5">
            <ArrowUpRight className="size-4" aria-hidden />
            Upgrade plan
          </Button>
          {canManage ? (
            <TopUpDialog
              organizationId={organization.id}
              packs={data.packs}
              onPurchased={() => void runLoad()}
            />
          ) : null}
        </div>
      </div>

      {!canView ? (
        <Card>
          <CardContent className="pt-6">
            <p className="text-sm text-muted-foreground">
              Billing detail is available to a manager or owner.
            </p>
          </CardContent>
        </Card>
      ) : isLoading ? (
        <div className="flex flex-col gap-3">
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : error ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3 pt-6">
            <p className="text-sm text-destructive">{error}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
              Try again
            </Button>
          </CardContent>
        </Card>
      ) : (
        <>
          {data.subscription ? <PlanCard subscription={data.subscription} /> : null}
          <EntitlementsTable items={data.entitlements} />
          <BillingPeriodsTable periods={data.periods} />
          {data.statement ? (
            <BlossomStatementTable statement={data.statement} onPage={setStatementPage} />
          ) : null}
        </>
      )}
    </div>
  )
}
