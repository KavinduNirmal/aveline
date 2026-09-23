import { useCallback, useEffect, useState } from 'react'
import {
  Ban,
  CheckCircle2,
  Clock,
  Eye,
  Package,
  RefreshCw,
  ShoppingBag,
  TrendingUp,
} from 'lucide-react'
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
import { toApiError } from '@/lib/api-error'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import {
  cancelOrder,
  fetchOrders,
  recalculateOrder,
  updateOrderStatus,
  type OrderResponseDto,
} from '@/lib/orders-api'
import { formatMoney } from '@/lib/format-money'
import { hasPermission } from '@/lib/permissions'
import type { OrganizationProfileDto } from '@/types/organization'

interface OrdersPanelProps {
  organization: OrganizationProfileDto
  role: string
}

const STATUS_FILTERS = [
  { value: 'all', label: 'All statuses' },
  { value: 'pending_approval', label: 'Pending Approval' },
  { value: 'confirmed', label: 'Confirmed' },
  { value: 'processing', label: 'Processing' },
  { value: 'delivered', label: 'Delivered' },
  { value: 'cancelled', label: 'Cancelled' },
] as const

const STATUS_BADGE_VARIANTS: Record<
  string,
  { variant: 'default' | 'secondary' | 'outline' | 'destructive'; label: string }
> = {
  pending_approval: { variant: 'outline', label: 'Pending Approval' },
  confirmed: { variant: 'secondary', label: 'Confirmed' },
  processing: { variant: 'secondary', label: 'Processing' },
  delivered: { variant: 'default', label: 'Delivered' },
  cancelled: { variant: 'destructive', label: 'Cancelled' },
}

export function OrdersPanel({ organization, role }: OrdersPanelProps) {
  const canManageOrders = hasPermission(role, 'orders:manage')

  const [status, setStatus] = useState<string>('all')
  const [page, setPage] = useState(1)
  const [orders, setOrders] = useState<OrderResponseDto[]>([])
  const [total, setTotal] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Order Details Modal
  const [selectedOrder, setSelectedOrder] = useState<OrderResponseDto | null>(null)
  const [detailsLoading, setDetailsLoading] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  // Cancel reason prompt
  const [cancelModalOpen, setCancelModalOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  const pageSize = 20
  const lastPage = Math.max(1, Math.ceil(total / pageSize))

  const load = usePanelLoad(
    (signal?: AbortSignal) =>
      fetchOrders(
        organization.id,
        {
          status: status === 'all' ? undefined : status,
          page,
          pageSize,
        },
        signal,
      ),
    (result) => {
      setOrders(result.items)
      setTotal(result.total)
    },
    () => {
      setOrders([])
      setTotal(0)
      setError('Could not load orders.')
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

  useEffect(() => {
    const controller = new AbortController()
    void runLoad(controller.signal)
    return () => controller.abort()
  }, [runLoad])

  // Aggregate summary metrics on current list
  const grossRevenue = orders.reduce((sum, o) => sum + (o.status !== 'cancelled' ? o.total : 0), 0)
  const totalMargin = orders.reduce((sum, o) => sum + (o.status !== 'cancelled' ? o.margin : 0), 0)
  const avgMargin = grossRevenue > 0 ? (totalMargin / grossRevenue) * 100 : 0
  const pendingCount = orders.filter((o) => o.status === 'pending_approval').length

  const handleStatusTransition = async (orderId: string, nextStatus: string) => {
    if (!canManageOrders) return
    setDetailsLoading(true)
    setActionError(null)
    try {
      const updated = await updateOrderStatus(organization.id, orderId, nextStatus)
      toast.success(`Order status changed to ${nextStatus}.`)
      setSelectedOrder(updated)
      await runLoad()
    } catch (err) {
      setActionError(toApiError(err).message || 'Failed to update order status.')
    } finally {
      setDetailsLoading(false)
    }
  }

  const handleRecalculate = async (orderId: string) => {
    if (!canManageOrders) return
    setDetailsLoading(true)
    setActionError(null)
    try {
      const updated = await recalculateOrder(organization.id, orderId)
      toast.success('Order recalculated.')
      setSelectedOrder(updated)
      await runLoad()
    } catch (err) {
      setActionError(toApiError(err).message || 'Failed to recalculate order.')
    } finally {
      setDetailsLoading(false)
    }
  }

  const handleCancelOrder = async () => {
    if (!selectedOrder || !canManageOrders) return
    setIsSubmitting(true)
    setActionError(null)
    try {
      await cancelOrder(organization.id, selectedOrder.id, cancelReason)
      toast.success('Order cancelled.')
      setCancelModalOpen(false)
      setSelectedOrder(null)
      setCancelReason('')
      await runLoad()
    } catch (err) {
      setActionError(toApiError(err).message || 'Failed to cancel order.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Live Orders</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Real-time commerce orders, wholesale margins, lifecycle fulfillment and financial reconciliation.
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="gap-2"
          onClick={() => void runLoad()}
        >
          <RefreshCw className="size-3.5" aria-hidden /> Refresh
        </Button>
      </div>

      {/* KPI Highlights */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-0 pb-2">
            <CardTitle className="text-sm font-medium">Orders Count</CardTitle>
            <ShoppingBag className="size-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{total}</div>
            <p className="text-xs text-muted-foreground mt-1">Tenant orders recorded</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-0 pb-2">
            <CardTitle className="text-sm font-medium">Page Revenue</CardTitle>
            <TrendingUp className="size-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{formatMoney(grossRevenue)}</div>
            <p className="text-xs text-muted-foreground mt-1">Confirmed & active volume</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-0 pb-2">
            <CardTitle className="text-sm font-medium">Average Margin</CardTitle>
            <TrendingUp className="size-4 text-primary" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-primary">
              {avgMargin.toFixed(1)}%
            </div>
            <p className="text-xs text-muted-foreground mt-1">Wholesale margin average</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-0 pb-2">
            <CardTitle className="text-sm font-medium">Pending Approval</CardTitle>
            <Clock className="size-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-foreground">
              {pendingCount}
            </div>
            <p className="text-xs text-muted-foreground mt-1">Awaiting decision</p>
          </CardContent>
        </Card>
      </div>

      {/* Orders Table */}
      <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <CardTitle className="font-serif text-lg font-medium">Order Register</CardTitle>
              <CardDescription>
                Browse order status, customer names, total sums, and profitability.
              </CardDescription>
            </div>
            <Select
              value={status}
              onValueChange={(val) => {
                setStatus(val)
                setPage(1)
              }}
            >
              <SelectTrigger aria-label="Filter by order status" className="w-[12rem]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {STATUS_FILTERS.map((f) => (
                  <SelectItem key={f.value} value={f.value}>
                    {f.label}
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
          ) : orders.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-10 text-center">
              <Package className="size-10 text-muted-foreground/40 mb-2" />
              <p className="text-sm text-muted-foreground">No orders matching this criteria.</p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Customer</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                  <TableHead className="text-right">Cost</TableHead>
                  <TableHead className="text-right">Margin</TableHead>
                  <TableHead>Date</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {orders.map((o) => {
                  const badge = STATUS_BADGE_VARIANTS[o.status] ?? {
                    variant: 'outline',
                    label: o.status,
                  }
                  return (
                    <TableRow key={o.id}>
                      <TableCell className="font-medium">
                        <div className="flex flex-col">
                          <span>{o.customerName || 'Walk-in Customer'}</span>
                          <span className="font-mono text-[11px] text-muted-foreground">
                            {o.id.slice(0, 8)}...
                          </span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline">{o.orderType || 'Store'}</Badge>
                      </TableCell>
                      <TableCell>
                        <Badge variant={badge.variant}>{badge.label}</Badge>
                      </TableCell>
                      <TableCell className="text-right font-medium">
                        {formatMoney(o.total)}
                      </TableCell>
                      <TableCell className="text-right text-muted-foreground">
                        {formatMoney(o.totalCost)}
                      </TableCell>
                      <TableCell className="text-right font-medium text-primary">
                        {formatMoney(o.margin)}
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {new Date(o.createdAt).toLocaleDateString()}
                      </TableCell>
                      <TableCell className="text-right">
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="gap-1"
                          onClick={() => setSelectedOrder(o)}
                        >
                          <Eye className="size-3.5" /> Details
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          )}

          {total > pageSize && (
            <div className="flex items-center justify-between pt-2">
              <p className="text-xs text-muted-foreground">
                Page {page} of {lastPage} · {total} orders
              </p>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => setPage((c) => Math.max(1, c - 1))}
                >
                  Previous
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={page >= lastPage}
                  onClick={() => setPage((c) => c + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Order Details Modal */}
      <Dialog open={selectedOrder !== null} onOpenChange={(open) => !open && setSelectedOrder(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>Order Details</DialogTitle>
            <DialogDescription>
              {selectedOrder?.customerName} · Order #{selectedOrder?.id.slice(0, 8)}
            </DialogDescription>
          </DialogHeader>

          {selectedOrder && (
            <div className="flex flex-col gap-4 py-2">
              {/* Order status banner */}
              <div className="flex items-center justify-between rounded-lg border p-3 bg-muted/40">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium">Status:</span>
                  <Badge variant={STATUS_BADGE_VARIANTS[selectedOrder.status]?.variant ?? 'outline'}>
                    {STATUS_BADGE_VARIANTS[selectedOrder.status]?.label ?? selectedOrder.status}
                  </Badge>
                </div>
                <div className="text-sm">
                  Created: {new Date(selectedOrder.createdAt).toLocaleString()}
                </div>
              </div>

              {/* Items Table */}
              <div className="border rounded-md overflow-hidden">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Item</TableHead>
                      <TableHead className="text-right">Qty</TableHead>
                      <TableHead className="text-right">Price</TableHead>
                      <TableHead className="text-right">Cost</TableHead>
                      <TableHead className="text-right">Total</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {selectedOrder.items.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={5} className="text-center text-muted-foreground py-4">
                          No line items recorded.
                        </TableCell>
                      </TableRow>
                    ) : (
                      selectedOrder.items.map((item) => (
                        <TableRow key={item.id}>
                          <TableCell className="font-medium">{item.title}</TableCell>
                          <TableCell className="text-right">{item.quantity}</TableCell>
                          <TableCell className="text-right">{formatMoney(item.unitPrice)}</TableCell>
                          <TableCell className="text-right text-muted-foreground">
                            {formatMoney(item.unitCost)}
                          </TableCell>
                          <TableCell className="text-right font-medium">
                            {formatMoney(item.totalPrice)}
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </div>

              {/* Financial summary */}
              <div className="flex justify-end">
                <div className="w-64 flex flex-col gap-1.5 text-sm">
                  <div className="flex justify-between text-muted-foreground">
                    <span>Subtotal:</span>
                    <span>{formatMoney(selectedOrder.subtotal)}</span>
                  </div>
                  {selectedOrder.discount > 0 && (
                    <div className="flex justify-between text-destructive">
                      <span>Discount:</span>
                      <span>-{formatMoney(selectedOrder.discount)}</span>
                    </div>
                  )}
                  <div className="flex justify-between font-bold text-base border-t pt-1">
                    <span>Total:</span>
                    <span>{formatMoney(selectedOrder.total)}</span>
                  </div>
                  <div className="flex justify-between text-xs text-muted-foreground pt-1">
                    <span>Total Cost:</span>
                    <span>{formatMoney(selectedOrder.totalCost)}</span>
                  </div>
                  <div className="flex justify-between text-xs text-primary font-medium">
                    <span>Calculated Margin:</span>
                    <span>{formatMoney(selectedOrder.margin)}</span>
                  </div>
                </div>
              </div>

              {actionError && <p className="text-sm text-destructive">{actionError}</p>}
            </div>
          )}

          <DialogFooter className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
            <div className="flex gap-2">
              {canManageOrders && selectedOrder && selectedOrder.status !== 'cancelled' && (
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  disabled={detailsLoading}
                  onClick={() => setCancelModalOpen(true)}
                >
                  <Ban className="size-3.5 mr-1" /> Cancel Order
                </Button>
              )}
              {canManageOrders && selectedOrder && (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={detailsLoading}
                  onClick={() => void handleRecalculate(selectedOrder.id)}
                >
                  Recalculate
                </Button>
              )}
            </div>

            <div className="flex gap-2">
              {canManageOrders && selectedOrder?.status === 'confirmed' && (
                <Button
                  type="button"
                  size="sm"
                  disabled={detailsLoading}
                  onClick={() => void handleStatusTransition(selectedOrder.id, 'processing')}
                >
                  Mark Processing
                </Button>
              )}
              {canManageOrders && selectedOrder?.status === 'processing' && (
                <Button
                  type="button"
                  size="sm"
                  disabled={detailsLoading}
                  onClick={() => void handleStatusTransition(selectedOrder.id, 'delivered')}
                >
                  <CheckCircle2 className="size-3.5 mr-1" /> Mark Delivered
                </Button>
              )}
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setSelectedOrder(null)}
              >
                Close
              </Button>
            </div>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Cancel Order Dialog */}
      <Dialog open={cancelModalOpen} onOpenChange={setCancelModalOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Cancel Order</DialogTitle>
            <DialogDescription>
              Are you sure you want to cancel this order? This action cannot be reversed.
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-2 py-2">
            <Label htmlFor="cancel-reason">Reason for cancellation (optional)</Label>
            <Input
              id="cancel-reason"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="e.g. Customer requested cancellation"
            />
          </div>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setCancelModalOpen(false)}
              disabled={isSubmitting}
            >
              Back
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={isSubmitting}
              onClick={() => void handleCancelOrder()}
            >
              {isSubmitting && <RefreshCw className="size-4 animate-spin mr-1.5" aria-hidden />}
              Confirm Cancellation
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
