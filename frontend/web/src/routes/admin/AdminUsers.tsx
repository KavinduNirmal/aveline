import { useCallback, useEffect, useState } from "react"
import { useSearchParams } from "react-router-dom"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { searchAdminUsers, updateUserAccountState } from "@/lib/admin/api"
import { Pagination } from "@/components/admin/data/Pagination"
import {
  USER_PAGE_SIZES,
  readListParams,
  writeListParams,
} from "@/lib/admin/query-params"
import type { AdminUserDto } from "@/types/admin"
import type { AccountState } from "@/types/user"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent } from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Search } from "lucide-react"
import { toast } from "sonner"

const LIST_OPTIONS = {
  pageSizes: USER_PAGE_SIZES,
  defaultPageSize: 25,
  filters: ['q', 'accountState'] as const,
}

export function AdminUsersView() {
  // Filters and paging live in the URL, so a filtered view survives the back button.
  const [searchParams, setSearchParams] = useSearchParams()
  const { page, pageSize, filters } = readListParams(searchParams, LIST_OPTIONS)
  const q = filters.q ?? ''
  const stateFilter = filters.accountState ?? 'all'

  const [searchInput, setSearchInput] = useState(q)
  const [users, setUsers] = useState<AdminUserDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)

  const [selectedUser, setSelectedUser] = useState<AdminUserDto | null>(null)
  const [targetState, setTargetState] = useState<AccountState>("Active")
  const [reason, setReason] = useState("")
  const [updating, setUpdating] = useState(false)

  const applyParams = useCallback(
    (next: { page?: number; pageSize?: number; q?: string; accountState?: string }) => {
      const nextAccountState = next.accountState ?? stateFilter
      setSearchParams(
        writeListParams(
          {
            page: next.page ?? page,
            pageSize: next.pageSize ?? pageSize,
            filters: {
              q: next.q ?? q,
              accountState: nextAccountState === 'all' ? '' : nextAccountState,
            },
          },
          LIST_OPTIONS,
        ),
      )
    },
    [page, pageSize, q, stateFilter, setSearchParams],
  )

  const loadUsers = useCallback(async () => {
    setLoading(true)
    try {
      const data = await searchAdminUsers({
        q: q || undefined,
        accountState: stateFilter === "all" ? undefined : stateFilter,
        page,
        pageSize,
      })
      setUsers(data.items)
      setTotal(data.total)
    } catch {
      toast.error("Failed to load users")
    } finally {
      setLoading(false)
    }
  }, [q, stateFilter, page, pageSize])

  useEffect(() => {
    const timer = setTimeout(() => {
      void loadUsers()
    }, 250)
    return () => clearTimeout(timer)
  }, [loadUsers])

  // Typing debounces into the URL rather than into local state, so the URL is always the
  // single source of truth the fetch reads from.
  useEffect(() => {
    const timer = setTimeout(() => {
      if (searchInput !== q) applyParams({ q: searchInput, page: 1 })
    }, 250)
    return () => clearTimeout(timer)
  }, [searchInput, q, applyParams])

  const openStateModal = (user: AdminUserDto) => {
    setSelectedUser(user)
    if (user.accountState === "Active") {
      setTargetState("Suspended")
    } else {
      setTargetState("Active")
    }
    setReason("")
  }

  const handleStateSubmit = async () => {
    if (!selectedUser) return
    if (!reason.trim()) {
      toast.error("A reason is mandatory for administrative account transitions.")
      return
    }

    setUpdating(true)
    try {
      await updateUserAccountState(selectedUser.id, {
        accountState: targetState,
        reason: reason.trim(),
      })
      toast.success(`Account state changed to ${targetState}`)
      setSelectedUser(null)
      void loadUsers()
    } catch (err: any) {
      toast.error(err?.message || "Failed to update account state")
    } finally {
      setUpdating(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex justify-between items-center">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            User Accounts
          </h2>
          <p className="text-sm text-muted-foreground">
            Directory of registered boutique associates and system administrators.
          </p>
        </div>
      </div>

      <Card className="border-border shadow-xs">
        <CardContent className="p-4 flex flex-wrap gap-4 items-center justify-between">
          <div className="relative flex-1 min-w-[240px]">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
            <Input
              placeholder="Search by name, email, clerk ID..."
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              className="pl-9 text-xs"
            />
          </div>

          <div className="flex items-center gap-2">
            <Select
              value={stateFilter}
              onValueChange={(value) => applyParams({ accountState: value, page: 1 })}
            >
              <SelectTrigger className="text-xs h-9 w-[190px]">
                <SelectValue placeholder="All Account States" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All Account States</SelectItem>
                <SelectItem value="Active">Active</SelectItem>
                <SelectItem value="OnboardingPending">Onboarding Pending</SelectItem>
                <SelectItem value="Suspended">Suspended</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="outline" size="sm" onClick={() => void loadUsers()} className="text-xs">
              Refresh
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>User / Identity</TableHead>
              <TableHead>Role</TableHead>
              <TableHead>Account State</TableHead>
              <TableHead>Joined</TableHead>
              <TableHead className="text-right">Action</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-muted-foreground text-xs">
                  Loading users...
                </TableCell>
              </TableRow>
            ) : users.length === 0 ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-muted-foreground text-xs">
                  No users found matching query.
                </TableCell>
              </TableRow>
            ) : (
              users.map((u) => (
                <TableRow key={u.id}>
                  <TableCell>
                    <div className="font-medium text-foreground">
                      {u.firstName} {u.lastName}
                    </div>
                    <div className="text-xs text-muted-foreground font-mono">{u.email}</div>
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-col gap-1">
                      <Badge variant="outline" className="w-fit text-[10px] font-mono">
                        {u.userRole || "staff"}
                      </Badge>
                      {u.organizationRole && (
                        <span className="text-[10px] text-muted-foreground font-mono">
                          {u.organizationRole}
                        </span>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={
                        u.accountState === "Active"
                          ? "default"
                          : u.accountState === "Suspended"
                            ? "destructive"
                            : "secondary"
                      }
                      className="text-[11px]"
                    >
                      {u.accountState}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {new Date(u.createdAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button
                      variant="outline"
                      size="sm"
                      className="text-xs h-8"
                      onClick={() => openStateModal(u)}
                    >
                      Change State
                    </Button>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
        <Pagination
          page={page}
          pageSize={pageSize}
          total={total}
          pageSizes={USER_PAGE_SIZES}
          onPageChange={(next) => applyParams({ page: next })}
          onPageSizeChange={(next) => applyParams({ pageSize: next, page: 1 })}
        />
      </Card>

      <Dialog open={!!selectedUser} onOpenChange={(o) => !o && setSelectedUser(null)}>
        <DialogContent className="sm:max-w-md bg-card">
          <DialogHeader>
            <DialogTitle className="font-serif">Modify Account State</DialogTitle>
            <DialogDescription className="text-xs">
              Transition account lifecycle for {selectedUser?.firstName} {selectedUser?.lastName} (
              {selectedUser?.email}).
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-4 py-2 text-xs">
            <div>
              <label className="font-medium block mb-1">Target Account State</label>
              <Select value={targetState} onValueChange={(value) => setTargetState(value as AccountState)}>
                <SelectTrigger className="w-full text-xs h-9">
                  <SelectValue placeholder="Select a state" />
                </SelectTrigger>
                <SelectContent>
                  {selectedUser?.accountState === "OnboardingPending" && (
                    <>
                      <SelectItem value="Active">Active</SelectItem>
                      <SelectItem value="Suspended">Suspended</SelectItem>
                    </>
                  )}
                  {selectedUser?.accountState === "Active" && (
                    <SelectItem value="Suspended">Suspended</SelectItem>
                  )}
                  {selectedUser?.accountState === "Suspended" && (
                    <SelectItem value="Active">Active</SelectItem>
                  )}
                </SelectContent>
              </Select>
            </div>

            <div>
              <label className="font-medium block mb-1">
                Mandatory Reason <span className="text-destructive">*</span>
              </label>
              <Input
                placeholder="Reason for audit log trail..."
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="text-xs"
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" size="sm" onClick={() => setSelectedUser(null)}>
              Cancel
            </Button>
            <Button
              size="sm"
              onClick={() => void handleStateSubmit()}
              disabled={updating || reason.trim().length === 0}
              title={
                reason.trim().length === 0 ? "A reason is required for the audit trail" : undefined
              }
            >
              {updating ? "Updating..." : "Commit State Change"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
