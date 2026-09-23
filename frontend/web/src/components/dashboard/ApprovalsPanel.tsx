import { useCallback, useEffect, useState } from 'react'
import { Check, ClipboardCheck, RefreshCw, SlidersHorizontal, X } from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { toApiError } from '@/lib/api-error'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import {
  approveApproval,
  availableDecisions,
  fetchApprovals,
  rejectApproval,
  reviseApproval,
  type ApprovalDecision,
  type ApprovalQueueEntry,
} from '@/lib/approvals-api'
import { fetchBusinessRules, type BusinessRuleResponseDto } from '@/lib/business-rules-api'
import { formatMoney } from '@/lib/format-money'
import { hasPermission } from '@/lib/permissions'
import { useNotifications } from '@/contexts/NotificationsContext'
import type { OrganizationProfileDto } from '@/types/organization'
import { BusinessRulesTable } from '@/components/dashboard/rules/BusinessRulesTable'

interface ApprovalsPanelProps {
  organization: OrganizationProfileDto
  role: string
}

const STATUSES = ['pending', 'approved', 'rejected', 'revised'] as const

const VERB_TITLE: Record<ApprovalDecision, string> = {
  approve: 'Approve this order',
  reject: 'Reject this order',
  revise: 'Revise this order',
}

const VERB_DESCRIPTION: Record<ApprovalDecision, string> = {
  approve: 'The order proceeds. The discount the customer was quoted stands.',
  reject: 'Rejecting cancels the order. This cannot be undone from the dashboard.',
  revise: 'Revising rewrites the order’s discount and total. The money figures change.',
}

/**
 * The Approvals section.
 *
 * Real-time queue listening for `ApprovalNeeded` notifications over SignalR.
 * Tabbed interface offering the queue inbox and the dynamic business rules configuration.
 */
export function ApprovalsPanel({ organization, role }: ApprovalsPanelProps) {
  const decisions = availableDecisions(
    hasPermission(role, 'approvals:approve'),
    hasPermission(role, 'orders:manage'),
  )
  const canManageRules = hasPermission(role, 'orders:manage')

  const [activeTab, setActiveTab] = useState<'queue' | 'rules'>('queue')
  const [status, setStatus] = useState<string>('pending')
  const [page, setPage] = useState(1)
  const [entries, setEntries] = useState<ApprovalQueueEntry[]>([])
  const [total, setTotal] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Rules state
  const [rules, setRules] = useState<BusinessRuleResponseDto[]>([])
  const [rulesLoading, setRulesLoading] = useState(false)

  const [target, setTarget] = useState<ApprovalQueueEntry | null>(null)
  const [verb, setVerb] = useState<ApprovalDecision>('approve')
  const [reason, setReason] = useState('')
  const [revisedDiscount, setRevisedDiscount] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  const pageSize = 20
  const lastPage = Math.max(1, Math.ceil(total / pageSize))

  // Real-time signal integration:
  const { lastNotification } = useNotifications()

  const load = usePanelLoad(
    (signal?: AbortSignal) =>
      fetchApprovals(
        organization.id,
        { status: status === 'all' ? undefined : status, page, pageSize },
        signal,
      ),
    (result) => {
      setEntries(result.items)
      setTotal(result.total)
    },
    () => {
      setEntries([])
      setTotal(0)
      setError('Could not load the approval queue.')
    },
    [organization.id, status, page],
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

  const loadRules = useCallback(async () => {
    if (!canManageRules) return
    setRulesLoading(true)
    try {
      const data = await fetchBusinessRules(organization.id, false)
      setRules(data)
    } catch {
      toast.error('Could not load business rules.')
    } finally {
      setRulesLoading(false)
    }
  }, [organization.id, canManageRules])

  useEffect(() => {
    const controller = new AbortController()
    void runLoad(controller.signal)
    if (canManageRules) {
      void loadRules()
    }
    return () => controller.abort()
  }, [runLoad, loadRules, canManageRules])

  // Automatically refresh queue on incoming ApprovalNeeded SignalR notification
  useEffect(() => {
    if (lastNotification?.type === 'ApprovalNeeded') {
      toast.info('New approval request received', {
        description: lastNotification.body,
      })
      void runLoad()
    }
  }, [lastNotification, runLoad])

  const openDecision = (entry: ApprovalQueueEntry, next: ApprovalDecision) => {
    setActionError(null)
    setReason('')
    setRevisedDiscount('')
    setVerb(next)
    setTarget(entry)
  }

  const submit = async () => {
    if (!target) return
    setIsSubmitting(true)
    setActionError(null)
    const payload = {
      reason: reason.trim() || undefined,
      ...(verb === 'revise' && revisedDiscount.trim() !== ''
        ? { revisedDiscount: Number(revisedDiscount) }
        : {}),
    }

    try {
      if (verb === 'approve') {
        await approveApproval(organization.id, target.id, payload)
      } else if (verb === 'reject') {
        await rejectApproval(organization.id, target.id, payload)
      } else {
        await reviseApproval(organization.id, target.id, payload)
      }
      toast.success(`Order ${verb}d.`)
      setTarget(null)
      await runLoad()
    } catch (caught) {
      const message = toApiError(caught).message
      setActionError(message)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Approvals</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Discounts and orders waiting on a decision. Approving keeps the quoted price; rejecting
            cancels the order, and revising rewrites it.
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="gap-2"
          onClick={() => {
            void runLoad()
            if (canManageRules) void loadRules()
          }}
        >
          <RefreshCw className="size-3.5" aria-hidden /> Refresh
        </Button>
      </div>

      <Tabs value={activeTab} onValueChange={(val) => setActiveTab(val as 'queue' | 'rules')}>
        <TabsList>
          <TabsTrigger value="queue" className="gap-2">
            Approval Queue
            {total > 0 && status === 'pending' && (
              <Badge variant="secondary" className="px-1.5 py-0 text-xs">
                {total}
              </Badge>
            )}
          </TabsTrigger>
          {canManageRules && (
            <TabsTrigger value="rules" className="gap-2">
              <SlidersHorizontal className="size-3.5" />
              Rules & Thresholds
            </TabsTrigger>
          )}
        </TabsList>

        <TabsContent value="queue" className="mt-4">
          <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
            <CardHeader>
              <div className="flex flex-wrap items-center justify-between gap-4">
                <div>
                  <CardTitle className="font-serif text-lg font-medium">Queue</CardTitle>
                  <CardDescription>
                    {decisions.length === 0
                      ? 'You may view this queue, but no decision verb is available to your role.'
                      : `You may ${decisions.join(', ')}.`}
                  </CardDescription>
                </div>
                <Select
                  value={status}
                  onValueChange={(value) => {
                    setStatus(value)
                    setPage(1)
                  }}
                >
                  <SelectTrigger aria-label="Filter by status" className="w-[11rem]">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All statuses</SelectItem>
                    {STATUSES.map((option) => (
                      <SelectItem key={option} value={option}>
                        {option}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              {isLoading ? (
                <Skeleton className="h-48 w-full" />
              ) : error ? (
                <div className="flex flex-col items-start gap-3">
                  <p className="text-sm text-destructive">{error}</p>
                  <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
                    Try again
                  </Button>
                </div>
              ) : entries.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  Nothing waiting. An order that exceeds a discount threshold appears here.
                </p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Order</TableHead>
                      <TableHead>Type</TableHead>
                      <TableHead>Reason</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead className="text-right">Decision</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {entries.map((entry) => (
                      <TableRow key={entry.id}>
                        <TableCell>
                          <div className="flex flex-col">
                            <span className="font-medium">
                              {entry.order?.customerName ?? 'Order unavailable'}
                            </span>
                            <span className="text-xs text-muted-foreground">
                              {entry.order ? formatMoney(entry.order.total) : entry.orderId}
                            </span>
                          </div>
                        </TableCell>
                        <TableCell>
                          <Badge variant="outline">{entry.approvalType}</Badge>
                          {entry.thresholdExceeded ? (
                            <Badge variant="outline" className="ml-2 text-destructive">
                              over threshold
                            </Badge>
                          ) : null}
                        </TableCell>
                        <TableCell className="text-muted-foreground">{entry.reason}</TableCell>
                        <TableCell>
                          <Badge variant={entry.status === 'pending' ? 'secondary' : 'outline'}>
                            {entry.status}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          <div className="flex flex-wrap items-center justify-end gap-2">
                            {decisions.includes('approve') ? (
                              <Button
                                type="button"
                                size="sm"
                                variant="outline"
                                onClick={() => openDecision(entry, 'approve')}
                              >
                                <Check className="size-3.5" aria-hidden /> Approve
                              </Button>
                            ) : null}
                            {decisions.includes('reject') ? (
                              <Button
                                type="button"
                                size="sm"
                                variant="outline"
                                onClick={() => openDecision(entry, 'reject')}
                              >
                                <X className="size-3.5" aria-hidden /> Reject
                              </Button>
                            ) : null}
                            {decisions.includes('revise') ? (
                              <Button
                                type="button"
                                size="sm"
                                variant="outline"
                                onClick={() => openDecision(entry, 'revise')}
                              >
                                <ClipboardCheck className="size-3.5" aria-hidden /> Revise
                              </Button>
                            ) : null}
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}

              {total > pageSize ? (
                <div className="flex items-center justify-between">
                  <p className="text-xs text-muted-foreground">
                    Page {page} of {lastPage} · {total} entries
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
        </TabsContent>

        {canManageRules && (
          <TabsContent value="rules" className="mt-4">
            <BusinessRulesTable
              organizationId={organization.id}
              rules={rules}
              isLoading={rulesLoading}
              canManage={canManageRules}
              onReload={loadRules}
            />
          </TabsContent>
        )}
      </Tabs>

      <Dialog open={target !== null} onOpenChange={(open) => !open && setTarget(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{VERB_TITLE[verb]}</DialogTitle>
            <DialogDescription>{VERB_DESCRIPTION[verb]}</DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="decision-reason">Reason (optional)</Label>
              <Input
                id="decision-reason"
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                placeholder="Why this decision"
              />
            </div>
            {verb === 'revise' ? (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="revised-discount">Revised discount</Label>
                <Input
                  id="revised-discount"
                  type="number"
                  value={revisedDiscount}
                  onChange={(event) => setRevisedDiscount(event.target.value)}
                  placeholder="0"
                />
              </div>
            ) : null}
            {actionError ? <p className="text-sm text-destructive">{actionError}</p> : null}
          </div>

          <DialogFooter>
            <Button type="button" disabled={isSubmitting} onClick={() => void submit()}>
              {isSubmitting ? <RefreshCw className="size-4 animate-spin" aria-hidden /> : null}
              Confirm {verb}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
