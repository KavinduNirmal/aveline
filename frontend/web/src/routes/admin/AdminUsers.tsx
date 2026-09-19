import { useEffect, useState } from "react"
import { searchAdminUsers, updateUserAccountState } from "@/lib/admin/api"
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

export function AdminUsersView() {
  const [users, setUsers] = useState<AdminUserDto[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState("")
  const [stateFilter, setStateFilter] = useState<string>("")
  const [page] = useState(1)

  const [selectedUser, setSelectedUser] = useState<AdminUserDto | null>(null)
  const [targetState, setTargetState] = useState<AccountState>("Active")
  const [reason, setReason] = useState("")
  const [updating, setUpdating] = useState(false)

  const loadUsers = async () => {
    setLoading(true)
    try {
      const data = await searchAdminUsers({
        q: search || undefined,
        accountState: stateFilter || undefined,
        page,
        pageSize: 20,
      })
      setUsers(data.items)
    } catch {
      toast.error("Failed to load users")
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    const timer = setTimeout(() => {
      void loadUsers()
    }, 250)
    return () => clearTimeout(timer)
  }, [search, stateFilter, page])

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
    <div className="space-y-6">
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
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 text-xs"
            />
          </div>

          <div className="flex items-center gap-2">
            <select
              value={stateFilter}
              onChange={(e) => setStateFilter(e.target.value)}
              className="text-xs h-9 px-3 rounded-md border border-input bg-background text-foreground"
            >
              <option value="">All Account States</option>
              <option value="Active">Active</option>
              <option value="OnboardingPending">Onboarding Pending</option>
              <option value="Suspended">Suspended</option>
            </select>
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

          <div className="space-y-4 py-2 text-xs">
            <div>
              <label className="font-medium block mb-1">Target Account State</label>
              <select
                value={targetState}
                onChange={(e) => setTargetState(e.target.value as AccountState)}
                className="w-full text-xs h-9 px-3 rounded-md border border-input bg-background text-foreground"
              >
                {selectedUser?.accountState === "OnboardingPending" && (
                  <>
                    <option value="Active">Active</option>
                    <option value="Suspended">Suspended</option>
                  </>
                )}
                {selectedUser?.accountState === "Active" && (
                  <option value="Suspended">Suspended</option>
                )}
                {selectedUser?.accountState === "Suspended" && (
                  <option value="Active">Active</option>
                )}
              </select>
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
              disabled={updating}
            >
              {updating ? "Updating..." : "Commit State Change"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
