import { useCallback, useEffect, useState } from 'react'
import { Search } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import { formatMoney } from '@/lib/format-money'
import {
  fetchIncomeAccounts,
  fetchIncomeLedger,
  type BoutiqueIncomeAccounts,
  type BoutiqueIncomeLedgerPage,
} from '@/lib/income-api'

import { IncomeLedgerTable } from './income/IncomeLedgerTable'
import { IncomeReconciliationBanner } from './income/IncomeReconciliationBanner'

interface IncomePanelProps {
  organizationId: string
  organizationName: string
}

const KINDS = ['all', 'Sale', 'PaymentReceived', 'Refund', 'Adjustment'] as const
const BASES = ['all', 'Verified', 'Derived'] as const

/**
 * The Income section: the shop's own takings.
 *
 * It is gated on `reports:view`, so a staff member never sees it; their reduced view is the takings
 * card on Overview. Everything here reports **per basis and per kind**, and there is no single
 * unlabelled "income" number anywhere on the screen.
 */
export function IncomePanel({ organizationId, organizationName }: IncomePanelProps) {
  const [kind, setKind] = useState<string>('all')
  const [basis, setBasis] = useState<string>('all')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)

  const [ledger, setLedger] = useState<BoutiqueIncomeLedgerPage | null>(null)
  const [accounts, setAccounts] = useState<BoutiqueIncomeAccounts | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const pageSize = 50

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  const load = usePanelLoad(
    (signal?: AbortSignal) =>
      Promise.all([
        fetchIncomeLedger(
          organizationId,
          {
            kind: kind === 'all' ? undefined : (kind as never),
            basis: basis === 'all' ? undefined : (basis as never),
            q: search || undefined,
            page,
            pageSize,
          },
          signal,
        ),
        fetchIncomeAccounts(organizationId, {}, signal),
      ]),
    ([ledgerPage, accountsPage]) => {
      setLedger(ledgerPage)
      setAccounts(accountsPage)
    },
    () => {
      // Clear rather than keep the previous window's figures under a new filter: a stale register
      // labelled with today's dates is worse than an error.
      setLedger(null)
      setAccounts(null)
      setError('Could not load the income register.')
    },
    [organizationId, kind, basis, search, page],
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

  const lastPage = ledger ? Math.max(1, Math.ceil(ledger.total / pageSize)) : 1
  const currency = ledger?.currency ?? accounts?.currency ?? 'LKR'

  return (
    <div className="flex flex-col gap-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          {organizationName}
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Income</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
          What this boutique took from its clients. Billed value and money taken are reported
          separately and never added together.
        </p>
      </div>

      {isLoading ? (
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
      ) : ledger && accounts ? (
        <>
          <IncomeReconciliationBanner
            reconciliation={ledger.reconciliation}
            currency={currency}
            windowCapped={ledger.windowCapped}
            windowFrom={ledger.window.from}
            windowTo={ledger.window.to}
            notes={ledger.dataQuality.notes}
            onShowLedger={() => {
              /* the register is already below; the banner's action scrolls a reader to it */
              document.getElementById('income-register')?.scrollIntoView({ behavior: 'smooth' })
            }}
          />

          <Card>
            <CardHeader>
              <CardTitle className="font-serif text-lg font-medium">By kind</CardTitle>
              <CardDescription>
                Each kind in its own total, so a refund is never netted into a sale figure without a
                label.
              </CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                {accounts.items.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No entries in this window yet.
                  </p>
                ) : (
                  accounts.items.map((item) => (
                    <div key={item.kind}>
                      <p className="text-xs uppercase tracking-wide text-muted-foreground">
                        {item.kind}
                      </p>
                      <p className="font-serif text-xl font-medium">
                        {formatMoney(item.total, currency)}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {item.count} {item.count === 1 ? 'entry' : 'entries'}
                      </p>
                    </div>
                  ))
                )}
              </div>

              {accounts.byPaymentMethod.length > 0 ? (
                <div className="flex flex-wrap gap-2">
                  {accounts.byPaymentMethod.map((method) => (
                    <Badge key={method.paymentMethod} variant="outline">
                      {method.paymentMethod}: {formatMoney(method.total, currency)}
                    </Badge>
                  ))}
                </div>
              ) : null}
            </CardContent>
          </Card>

          <div id="income-register" className="flex flex-col gap-4">
            <div className="flex flex-wrap items-center gap-3">
              <div className="relative min-w-[14rem] flex-1">
                <Search
                  className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                  aria-hidden
                />
                <Input
                  value={search}
                  onChange={(event) => {
                    setSearch(event.target.value)
                    setPage(1)
                  }}
                  placeholder="Search the reason or reference"
                  aria-label="Search the register"
                  className="pl-9"
                />
              </div>
              <Select
                value={kind}
                onValueChange={(value) => {
                  setKind(value)
                  setPage(1)
                }}
              >
                <SelectTrigger aria-label="Filter by kind" className="w-[12rem]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {KINDS.map((option) => (
                    <SelectItem key={option} value={option}>
                      {option === 'all' ? 'All kinds' : option}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Select
                value={basis}
                onValueChange={(value) => {
                  setBasis(value)
                  setPage(1)
                }}
              >
                <SelectTrigger aria-label="Filter by basis" className="w-[12rem]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {BASES.map((option) => (
                    <SelectItem key={option} value={option}>
                      {option === 'all'
                        ? 'All bases'
                        : option === 'Verified'
                          ? 'Money taken'
                          : 'Billed, unconfirmed'}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            {ledger.items.length === 0 ? (
              <Card>
                <CardContent className="pt-6">
                  <p className="text-sm font-medium">The register is empty for this view.</p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    A counter sale, a confirmed payment or a completed order appears here as soon as
                    it happens.
                  </p>
                </CardContent>
              </Card>
            ) : (
              <IncomeLedgerTable items={ledger.items} />
            )}

            {ledger.total > pageSize ? (
              <div className="flex items-center justify-between">
                <p className="text-xs text-muted-foreground">
                  Page {ledger.page} of {lastPage} · {ledger.total} entries ·{' '}
                  {formatMoney(ledger.totals.saleTotal, currency)} in sales this window
                </p>
                <div className="flex gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={page <= 1}
                    onClick={() => setPage((current) => Math.max(1, current - 1))}
                  >
                    Previous
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={page >= lastPage}
                    onClick={() => setPage((current) => current + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            ) : null}
          </div>
        </>
      ) : null}
    </div>
  )
}
