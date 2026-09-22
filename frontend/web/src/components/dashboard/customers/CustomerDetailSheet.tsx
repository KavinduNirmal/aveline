import { useCallback, useEffect, useState } from 'react'
import { Pencil, Trash2 } from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import {
  deleteCustomer,
  fetchCustomer,
  fetchCustomerInteractions,
  updateCustomer,
  type CustomerInteractionItem,
  type TenantCustomerDetail,
} from '@/lib/customers-api'
import { toApiError } from '@/lib/api-error'
import { formatMoney } from '@/lib/format-money'

interface CustomerDetailSheetProps {
  organizationId: string
  customerId: string | null
  canManage: boolean
  open: boolean
  onOpenChange: (open: boolean) => void
  onChanged: () => void
  onLogVisit: (customerId: string) => void
}

const LEVELS = ['level1', 'level2', 'level3', 'vip'] as const

/**
 * One client's record and history (E-6, E-9). Edit and delete are **hidden** unless
 * `customers:manage`; a control that the server would refuse with 403 is not offered.
 *
 * A 404 renders a not-found state rather than an empty one: the server deliberately makes "deleted"
 * and "in another boutique" indistinguishable, so the UI must not guess which it was.
 */
export function CustomerDetailSheet({
  organizationId,
  customerId,
  canManage,
  open,
  onOpenChange,
  onChanged,
  onLogVisit,
}: CustomerDetailSheetProps) {
  const [detail, setDetail] = useState<TenantCustomerDetail | null>(null)
  const [history, setHistory] = useState<CustomerInteractionItem[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [isEditing, setIsEditing] = useState(false)
  const [fullName, setFullName] = useState('')
  const [nickname, setNickname] = useState('')
  const [phoneNumber, setPhoneNumber] = useState('')
  const [email, setEmail] = useState('')
  const [level, setLevel] = useState<string>('ungraded')
  const [isSaving, setIsSaving] = useState(false)

  const load = useCallback(async () => {
    if (!customerId) return
    setIsLoading(true)
    setNotFound(false)
    setError(null)
    try {
      const [record, interactions] = await Promise.all([
        fetchCustomer(organizationId, customerId),
        fetchCustomerInteractions(organizationId, customerId, { page: 1, pageSize: 20 }),
      ])
      setDetail(record)
      setHistory(interactions.items)
    } catch (caught) {
      const apiError = toApiError(caught)
      if (apiError.status === 404) {
        setNotFound(true)
        setDetail(null)
        setHistory([])
      } else {
        setError('Could not load this client.')
      }
    } finally {
      setIsLoading(false)
    }
  }, [organizationId, customerId])

  useEffect(() => {
    if (!open) {
      setDetail(null)
      setHistory([])
      setIsEditing(false)
      setNotFound(false)
      setError(null)
      return
    }
    void load()
  }, [open, load])

  const beginEdit = () => {
    if (!detail) return
    setFullName(detail.fullName ?? '')
    setNickname(detail.nickname ?? '')
    setPhoneNumber(detail.phoneNumber ?? '')
    setEmail(detail.email ?? '')
    setLevel(detail.level ?? 'ungraded')
    setIsEditing(true)
  }

  const saveEdit = async () => {
    if (!detail) return
    setIsSaving(true)
    try {
      const updated = await updateCustomer(organizationId, detail.customerId, {
        fullName,
        nickname,
        phoneNumber: phoneNumber.trim().length > 0 ? phoneNumber : undefined,
        email,
        level: level === 'ungraded' ? '' : level,
      })
      setDetail(updated)
      setIsEditing(false)
      onChanged()
      toast.success('Client updated', { description: updated.fullName ?? 'Saved' })
    } catch (caught) {
      const apiError = toApiError(caught)
      // The server's own message is shown: 409 for a phone collision, 400 for a bad value.
      toast.error('Could not save this client', {
        description: apiError.code === 'customer-phone-conflict'
          ? 'Another client in this boutique already has that phone number.'
          : apiError.message,
      })
    } finally {
      setIsSaving(false)
    }
  }

  const remove = async () => {
    if (!detail) return
    setIsSaving(true)
    try {
      await deleteCustomer(organizationId, detail.customerId)
      onChanged()
      onOpenChange(false)
      toast.success('Client removed', {
        description: 'The record is hidden, and their orders stay readable.',
      })
    } catch (caught) {
      const apiError = toApiError(caught)
      toast.error('Could not remove this client', {
        description: apiError.code === 'customer-has-open-orders'
          ? 'This client has orders that are still live. Close or cancel them first.'
          : apiError.message,
      })
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle className="font-serif">
            {detail?.fullName ?? (notFound ? 'Client not found' : 'Client')}
          </SheetTitle>
          <SheetDescription>
            {detail?.status
              ? `Status is derived from spend, visits and recency: ${detail.status}.`
              : 'The client record on the tenant surface.'}
          </SheetDescription>
        </SheetHeader>

        <div className="flex flex-col gap-5 px-4 pb-8">
          {isLoading ? (
            <div className="flex flex-col gap-2">
              <Skeleton className="h-6 w-40" />
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-20 w-full" />
            </div>
          ) : notFound ? (
            <div className="rounded-lg border border-border bg-muted/40 p-4">
              <p className="text-sm font-medium">This client is not in this boutique.</p>
              <p className="mt-1 text-xs text-muted-foreground">
                A removed client and a client belonging to another boutique are deliberately
                indistinguishable, so this view cannot say which it was.
              </p>
            </div>
          ) : error ? (
            <div className="flex flex-col items-start gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-4">
              <p className="text-sm text-destructive">{error}</p>
              <Button type="button" variant="outline" size="sm" onClick={() => void load()}>
                Try again
              </Button>
            </div>
          ) : detail ? (
            <>
              {isEditing ? (
                <div className="flex flex-col gap-4">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="customer-full-name">Name</Label>
                    <Input
                      id="customer-full-name"
                      value={fullName}
                      onChange={(event) => setFullName(event.target.value)}
                    />
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="customer-nickname">Nickname</Label>
                    <Input
                      id="customer-nickname"
                      value={nickname}
                      onChange={(event) => setNickname(event.target.value)}
                    />
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="customer-phone">Phone</Label>
                    <Input
                      id="customer-phone"
                      value={phoneNumber}
                      onChange={(event) => setPhoneNumber(event.target.value)}
                      placeholder="0771234567"
                    />
                    <p className="text-xs text-muted-foreground">
                      The identity key. Another client already holding this number is refused.
                    </p>
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="customer-email">Email</Label>
                    <Input
                      id="customer-email"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                    />
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="customer-level">Level</Label>
                    <Select value={level} onValueChange={setLevel}>
                      <SelectTrigger id="customer-level" aria-label="Client level">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="ungraded">Not graded</SelectItem>
                        {LEVELS.map((option) => (
                          <SelectItem key={option} value={option}>
                            {option}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <p className="text-xs text-muted-foreground">
                      Status is not editable: the loyalty rule derives it from spend and visits.
                    </p>
                  </div>
                  <div className="flex gap-2">
                    <Button type="button" onClick={() => void saveEdit()} disabled={isSaving}>
                      Save changes
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => setIsEditing(false)}
                      disabled={isSaving}
                    >
                      Cancel
                    </Button>
                  </div>
                </div>
              ) : (
                <>
                  <dl className="grid grid-cols-2 gap-4">
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-muted-foreground">Phone</dt>
                      <dd className="text-sm">{detail.phoneNumber || '—'}</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-muted-foreground">Email</dt>
                      <dd className="text-sm">{detail.email ?? '—'}</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-muted-foreground">Level</dt>
                      <dd className="text-sm capitalize">
                        {detail.level ?? 'Not graded'}
                        {detail.loyaltyTierIsDerived === 1 ? (
                          <span className="ml-1 text-xs text-muted-foreground">(tier derived)</span>
                        ) : null}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-muted-foreground">Visits</dt>
                      <dd className="text-sm">{detail.visitCount}</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-muted-foreground">
                        Total spent
                      </dt>
                      <dd className="text-sm">{formatMoney(detail.totalSpent)}</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-muted-foreground">
                        Last visit
                      </dt>
                      <dd className="text-sm">
                        {detail.lastVisitAtUtc
                          ? new Date(detail.lastVisitAtUtc).toLocaleDateString()
                          : 'Never'}
                      </dd>
                    </div>
                  </dl>

                  {detail.tags.length > 0 ? (
                    <div className="flex flex-wrap gap-1.5">
                      {detail.tags.map((tag) => (
                        <Badge key={tag} variant="outline">
                          {tag}
                        </Badge>
                      ))}
                    </div>
                  ) : null}

                  <div className="flex flex-wrap gap-2">
                    <Button type="button" size="sm" onClick={() => onLogVisit(detail.customerId)}>
                      Log a visit
                    </Button>
                    {canManage ? (
                      <>
                        <Button type="button" size="sm" variant="outline" onClick={beginEdit}>
                          <Pencil className="size-3.5" aria-hidden /> Edit
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          onClick={() => void remove()}
                          disabled={isSaving}
                        >
                          <Trash2 className="size-3.5 text-destructive" aria-hidden /> Remove
                        </Button>
                      </>
                    ) : null}
                  </div>

                  <div className="flex flex-col gap-2">
                    <h3 className="text-sm font-medium">
                      Interactions ({detail.interactionCount})
                    </h3>
                    {history.length === 0 ? (
                      <p className="text-xs text-muted-foreground">No interactions recorded yet.</p>
                    ) : (
                      <ul className="flex flex-col gap-2">
                        {history.map((item) => (
                          <li
                            key={item.interactionId}
                            className="rounded-lg border border-border p-3 text-xs"
                          >
                            <div className="flex items-center justify-between">
                              <span className="font-medium capitalize">
                                {item.channel.replace('_', ' ')}
                              </span>
                              <span className="text-muted-foreground">
                                {new Date(item.occurredAtUtc).toLocaleString()}
                              </span>
                            </div>
                            {item.note ? <p className="mt-1">{item.note}</p> : null}
                            {item.countedAsVisit ? (
                              <Badge variant="secondary" className="mt-1.5">
                                Counted as a visit
                              </Badge>
                            ) : (
                              <span className="mt-1 block text-muted-foreground">
                                Recorded, not counted as a visit
                              </span>
                            )}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </>
              )}
            </>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
  )
}
