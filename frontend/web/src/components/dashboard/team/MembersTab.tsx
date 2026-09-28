import { useCallback, useEffect, useState } from 'react'
import { RefreshCw, Search, UserCog, UserMinus } from 'lucide-react'
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
  activateMember,
  changeMemberRole,
  fetchMembers,
  memberActionBlockedReason,
  removeMember,
  suspendMember,
  type OrganizationMember,
} from '@/lib/team-api'
import { INVITABLE_ROLES } from '@/types/invitation'

interface MembersTabProps {
  organizationId: string
  currentUserId: string
}

const ROLE_LABEL: Record<string, string> = {
  'org:boutique_owner': 'Owner',
  ...Object.fromEntries(INVITABLE_ROLES.map((role) => [role.value, role.label])),
}

const STATUSES = ['all', 'Active', 'Suspended'] as const

/**
 * The Members tab: the people who already hold a membership, and the three lifecycle actions the
 * server supports (change role, suspend/activate, remove).
 *
 * Two rules the UI keeps rather than delegating to the server:
 *
 * 1. **It never offers an action it knows will fail.** The caller's own row and any owner row are
 *    disabled with a stated reason (`memberActionBlockedReason`), because the server answers those
 *    with a `409`/`400` and a dead control is worse than an explained one.
 * 2. **A failure renders the server's own message.** A `409` from a role change and a `403` from a
 *    permission denial are the server explaining the rule; a generic "something went wrong" would
 *    throw that away.
 */
export function MembersTab({ organizationId, currentUserId }: MembersTabProps) {
  const [members, setMembers] = useState<OrganizationMember[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<string>('all')
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyUserId, setBusyUserId] = useState<string | null>(null)

  const [roleTarget, setRoleTarget] = useState<OrganizationMember | null>(null)
  const [selectedRole, setSelectedRole] = useState('org:boutique_staff')
  const [removeTarget, setRemoveTarget] = useState<OrganizationMember | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const pageSize = 20
  const lastPage = Math.max(1, Math.ceil(total / pageSize))

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  const load = usePanelLoad(
    (signal?: AbortSignal) =>
      fetchMembers(
        organizationId,
        { status: status === 'all' ? undefined : status, q: search || undefined, page, pageSize },
        signal,
      ),
    (result) => {
      setMembers(result.items)
      setTotal(result.total)
    },
    () => {
      setMembers([])
      setTotal(0)
      setError('Could not load the member list.')
    },
    [organizationId, status, search, page],
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

  const runAction = async (userId: string, action: () => Promise<unknown>, success: string) => {
    setBusyUserId(userId)
    setActionError(null)
    try {
      await action()
      toast.success(success)
      setRoleTarget(null)
      setRemoveTarget(null)
      await runLoad()
    } catch (caught) {
      // The server's own message is the explanation: a 409 names the rule that was broken, and a
      // 403 names the permission. Collapsing it into "failed" would discard it.
      const message = toApiError(caught).message
      setActionError(message)
      toast.error(message)
    } finally {
      setBusyUserId(null)
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle className="font-serif text-lg font-medium">Members</CardTitle>
          <CardDescription>
            People who already belong to this boutique. An invitation creates a code; this is the
            membership itself.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
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
                placeholder="Search by name or email"
                aria-label="Search members"
                className="pl-9"
              />
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
                {STATUSES.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option === 'all' ? 'All statuses' : option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {isLoading ? (
            <Skeleton className="h-48 w-full" />
          ) : error ? (
            <div className="flex flex-col items-start gap-3">
              <p className="text-sm text-destructive">{error}</p>
              <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
                Try again
              </Button>
            </div>
          ) : members.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No members match this view.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Member</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Joined</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {members.map((member) => {
                  const blocked = memberActionBlockedReason(member, currentUserId)
                  const busy = busyUserId === member.userId
                  return (
                    <TableRow key={member.userId}>
                      <TableCell>
                        <div className="flex flex-col">
                          <span className="font-medium">
                            {member.displayName ?? `${member.firstName} ${member.lastName}`.trim()}
                          </span>
                          <span className="text-xs text-muted-foreground">{member.email}</span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline">
                          {ROLE_LABEL[member.boutiqueRole] ?? member.boutiqueRole}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge variant={member.status === 'Active' ? 'secondary' : 'outline'}>
                          {member.status}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {new Date(member.joinedAt).toLocaleDateString([], {
                          day: 'numeric',
                          month: 'short',
                          year: 'numeric',
                        })}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap items-center justify-end gap-2">
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={blocked !== null || busy}
                            title={blocked ?? undefined}
                            onClick={() => {
                              setActionError(null)
                              setSelectedRole(member.boutiqueRole)
                              setRoleTarget(member)
                            }}
                          >
                            <UserCog className="size-3.5" aria-hidden /> Change role
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={blocked !== null || busy}
                            title={blocked ?? undefined}
                            onClick={() =>
                              void runAction(
                                member.userId,
                                member.status === 'Active'
                                  ? () => suspendMember(organizationId, member.userId)
                                  : () => activateMember(organizationId, member.userId),
                                member.status === 'Active' ? 'Member suspended.' : 'Member activated.',
                              )
                            }
                          >
                            {member.status === 'Active' ? 'Suspend' : 'Activate'}
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={blocked !== null || busy}
                            title={blocked ?? undefined}
                            onClick={() => {
                              setActionError(null)
                              setRemoveTarget(member)
                            }}
                          >
                            <UserMinus className="size-3.5" aria-hidden /> Remove
                          </Button>
                        </div>
                        {blocked ? (
                          <p className="mt-1 text-right text-xs italic text-muted-foreground">
                            {blocked}
                          </p>
                        ) : null}
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          )}

          {total > pageSize ? (
            <div className="flex items-center justify-between">
              <p className="text-xs text-muted-foreground">
                Page {page} of {lastPage} · {total} members
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

      <Dialog open={roleTarget !== null} onOpenChange={(open) => !open && setRoleTarget(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Change role</DialogTitle>
            <DialogDescription>
              {roleTarget ? `${roleTarget.firstName} ${roleTarget.lastName}` : ''} will gain the
              permissions of the role chosen here.
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="member-role">Boutique role</Label>
            <Select value={selectedRole} onValueChange={setSelectedRole}>
              <SelectTrigger id="member-role" aria-label="Choose a boutique role">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {INVITABLE_ROLES.map((role) => (
                  <SelectItem key={role.value} value={role.value}>
                    {role.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {actionError ? <p className="text-sm text-destructive">{actionError}</p> : null}
          <DialogFooter>
            <Button
              type="button"
              disabled={busyUserId !== null}
              onClick={() =>
                roleTarget &&
                void runAction(
                  roleTarget.userId,
                  () => changeMemberRole(organizationId, roleTarget.userId, selectedRole),
                  'Role updated.',
                )
              }
            >
              {busyUserId !== null ? (
                <RefreshCw className="size-4 animate-spin" aria-hidden />
              ) : null}
              Save role
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={removeTarget !== null} onOpenChange={(open) => !open && setRemoveTarget(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Remove member</DialogTitle>
            <DialogDescription>
              {removeTarget
                ? `${removeTarget.firstName} ${removeTarget.lastName} will lose access to this boutique.`
                : ''}
            </DialogDescription>
          </DialogHeader>
          {actionError ? <p className="text-sm text-destructive">{actionError}</p> : null}
          <DialogFooter>
            <Button
              type="button"
              variant="destructive"
              disabled={busyUserId !== null}
              onClick={() =>
                removeTarget &&
                void runAction(
                  removeTarget.userId,
                  () => removeMember(organizationId, removeTarget.userId),
                  'Member removed.',
                )
              }
            >
              Remove
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
