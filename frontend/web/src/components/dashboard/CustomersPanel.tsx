import { useCallback, useEffect, useState } from 'react'
import { Plus, Search, UserRound } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { fetchCustomerBook, type CustomerBookItem } from '@/lib/customers-api'
import { formatMoney } from '@/lib/format-money'
import { hasPermission } from '@/lib/permissions'
import type { OrganizationProfileDto } from '@/types/organization'

import { usePanelLoad } from '@/hooks/usePanelLoad'

import { CustomerDetailSheet } from './customers/CustomerDetailSheet'
import { LogVisitDialog } from './customers/LogVisitDialog'
import { WalkInDialog } from './customers/WalkInDialog'

interface CustomersPanelProps {
  organization: OrganizationProfileDto
  role: string
}

const LEVEL_FILTERS = [
  { value: 'all', label: 'All levels' },
  { value: 'level1', label: 'Level 1' },
  { value: 'level2', label: 'Level 2' },
  { value: 'level3', label: 'Level 3' },
  { value: 'vip', label: 'VIP' },
] as const

/**
 * The client book (E-6…E-9). Search, level filter and paging are server-side, so the table never
 * invents a total from a page it did not fetch.
 */
export function CustomersPanel({ organization, role }: CustomersPanelProps) {
  const organizationId = organization.id
  const canManage = hasPermission(role, 'customers:manage')

  const [search, setSearch] = useState('')
  const [level, setLevel] = useState<string>('all')
  const [page, setPage] = useState(1)
  const [items, setItems] = useState<CustomerBookItem[]>([])
  const [total, setTotal] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [selected, setSelected] = useState<CustomerBookItem | null>(null)
  const [visitTarget, setVisitTarget] = useState<CustomerBookItem | null>(null)
  const [walkInOpen, setWalkInOpen] = useState(false)

  const pageSize = 25

  // Cancellation is not a failure and a superseded response must not overwrite a newer one - see
  // `usePanelLoad`. Without it the StrictMode abort of the first request rendered an error card on
  // every page until "Try again" was pressed.
  const load = usePanelLoad(
    (signal?: AbortSignal) =>
      fetchCustomerBook(
        organizationId,
        { search, level: level === 'all' ? undefined : level, page, pageSize },
        signal,
      ),
    (book) => {
      setItems(book.items)
      setTotal(book.total)
    },
    () => {
      // An error is shown as an error. The list is cleared rather than left stale, because a table
      // that keeps the previous page's rows silently misreports the current filter.
      setItems([])
      setTotal(0)
      setError('Could not load the client book.')
    },
    [organizationId, search, level, page],
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

  const lastPage = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Customers</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            The client book. A visit counts only for an inbound, in-person interaction.
          </p>
        </div>
        {canManage ? (
          // Creating a client is a write, so the affordance exists only where the server would
          // accept it. A button that opens a form the API refuses is a button that wastes a
          // counter's time.
          <Button
            type="button"
            onClick={() => setWalkInOpen(true)}
            className="gap-1.5"
          >
            <Plus className="size-4" aria-hidden />
            Add client
          </Button>
        ) : null}
      </div>

      <Card>
        <CardContent className="flex flex-col gap-4 pt-6">
          <div className="flex flex-wrap items-center gap-3">
            <div className="relative min-w-[16rem] flex-1">
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
                placeholder="Search by name or phone"
                aria-label="Search clients"
                className="pl-9"
              />
            </div>
            <Select
              value={level}
              onValueChange={(value) => {
                setLevel(value)
                setPage(1)
              }}
            >
              <SelectTrigger aria-label="Filter by level" className="w-[11rem]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {LEVEL_FILTERS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {isLoading ? (
            <div className="flex flex-col gap-2" aria-label="Loading clients">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
            </div>
          ) : error ? (
            <div className="flex flex-col items-start gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-4">
              <p className="text-sm text-destructive">{error}</p>
              <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
                Try again
              </Button>
            </div>
          ) : items.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-10 text-center">
              <UserRound className="size-8 text-muted-foreground" aria-hidden />
              <p className="text-sm font-medium">No clients match this view.</p>
              <p className="text-xs text-muted-foreground">
                A new walk-in appears here as soon as the visit is logged.
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Client</TableHead>
                  <TableHead>Level</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Phone</TableHead>
                  <TableHead>Last visit</TableHead>
                  <TableHead className="text-right">Visits</TableHead>
                  <TableHead className="text-right">Total spent</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => (
                  <TableRow
                    key={item.customerId}
                    className="cursor-pointer"
                    onClick={() => setSelected(item)}
                  >
                    <TableCell className="font-medium">
                      {item.fullName ?? item.nickname ?? 'Unnamed client'}
                    </TableCell>
                    <TableCell>
                      {item.level ? (
                        <Badge variant="secondary" className="capitalize">
                          {item.level}
                        </Badge>
                      ) : (
                        <span className="text-muted-foreground">Not graded</span>
                      )}
                    </TableCell>
                    <TableCell className="capitalize">{item.status}</TableCell>
                    <TableCell>{item.phoneNumber || '—'}</TableCell>
                    <TableCell>
                      {item.lastVisitAtUtc ? (
                        new Date(item.lastVisitAtUtc).toLocaleDateString()
                      ) : (
                        <span className="text-muted-foreground">Never</span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">{item.visitCount}</TableCell>
                    <TableCell className="text-right">
                      {formatMoney(item.totalSpent)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {total > pageSize ? (
            <div className="flex items-center justify-between pt-2">
              <p className="text-xs text-muted-foreground">
                Page {page} of {lastPage} · {total} clients
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
        </CardContent>
      </Card>

      <CustomerDetailSheet
        organizationId={organizationId}
        customerId={selected?.customerId ?? null}
        canManage={canManage}
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelected(null)
        }}
        onChanged={() => void runLoad()}
        onLogVisit={(customerId) => {
          const target = items.find((item) => item.customerId === customerId) ?? null
          setVisitTarget(target)
        }}
      />

      <LogVisitDialog
        organizationId={organizationId}
        customer={visitTarget}
        open={visitTarget !== null}
        onOpenChange={(open) => {
          if (!open) setVisitTarget(null)
        }}
        onRecorded={() => void runLoad()}
      />

      <WalkInDialog
        organizationId={organizationId}
        open={walkInOpen}
        onOpenChange={setWalkInOpen}
        onCreated={() => void runLoad()}
      />
    </div>
  )
}
