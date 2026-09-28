import { useQuery } from "@tanstack/react-query"
import { useState } from "react"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { DataTable, type Column } from "@/components/admin/data/DataTable"
import { listAdminRequests, approveAdminRequest, rejectAdminRequest } from "@/lib/admin/api"
import type { AdminApprovalRequestSummary } from "@/types/admin"
import { Check, X } from "lucide-react"
import { toast } from "sonner"

/**
 * The access-request queue.
 *
 * The self-approval guard is keyed on **`clerkUserId` vs the caller's `sub`**, not on email. The
 * captured live payloads return `email: null` on `/auth/claims` and `""` on every request row, so
 * an email comparison is `undefined === undefined` and admits the very approval it exists to
 * block. `GET /admin/requests` is a bare, unpaginated array, so the whole queue is one read.
 */
export function AdminRequestsView() {
  const [processingId, setProcessingId] = useState<string | null>(null)
  const { clerkUserId } = useAdminSession()

  /**
   * §3.7: the access queue is small (`ListAllAsync` takes no paging) and the action is the point,
   * so it is never served stale and it refreshes on a 60 s cadence.
   */
  const requestsQuery = useQuery({
    queryKey: ['admin', 'requests'],
    queryFn: listAdminRequests,
    staleTime: 0,
    refetchInterval: 60_000,
  })

  const requests = requestsQuery.data ?? []
  const state = requestsQuery.isPending
    ? ({ kind: 'loading' } as const)
    : requestsQuery.isError
      ? ({
          kind: 'error',
          message:
            requestsQuery.error instanceof Error
              ? requestsQuery.error.message
              : 'Failed to load admin approval requests',
        } as const)
      : ({ kind: 'ready' } as const)

  const loadRequests = async () => {
    await requestsQuery.refetch()
  }
  const pendingCount = requests.filter((request) => request.status === "Pending").length
  const isSelf = (request: AdminApprovalRequestSummary): boolean =>
    clerkUserId !== null && request.clerkUserId === clerkUserId

  const handleApprove = async (request: AdminApprovalRequestSummary) => {
    if (isSelf(request)) {
      toast.error("Self-approval prohibited: an administrator cannot approve their own request.")
      return
    }
    setProcessingId(request.id)
    try {
      await approveAdminRequest(request.id)
      toast.success(`Approved administrator access for ${request.email || request.clerkUserId}`)
      await loadRequests()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : "Failed to approve request")
    } finally {
      setProcessingId(null)
    }
  }

  const handleReject = async (request: AdminApprovalRequestSummary) => {
    setProcessingId(request.id)
    try {
      await rejectAdminRequest(request.id)
      toast.success(`Rejected request for ${request.email || request.clerkUserId}`)
      await loadRequests()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : "Failed to reject request")
    } finally {
      setProcessingId(null)
    }
  }

  const columns: Column<AdminApprovalRequestSummary>[] = [
    {
      key: "candidate",
      header: "Candidate",
      render: (request) => (
        <span className="font-medium text-foreground">
          {request.firstName} {request.lastName}
        </span>
      ),
    },
    {
      key: "email",
      header: "Email",
      render: (request) => (
        <span className="font-mono text-xs text-muted-foreground">
          {request.email || "—"}
        </span>
      ),
    },
    {
      key: "requested",
      header: "Requested",
      render: (request) => (
        <span className="text-xs text-muted-foreground">
          {new Date(request.requestedAt).toLocaleString()}
        </span>
      ),
    },
    {
      key: "status",
      header: "Status",
      render: (request) => (
        <Badge
          variant={
            request.status === "Approved"
              ? "default"
              : request.status === "Rejected"
                ? "destructive"
                : "secondary"
          }
          className="text-[11px]"
        >
          {request.status}
        </Badge>
      ),
    },
    {
      key: "actions",
      header: "Actions",
      className: "text-right",
      render: (request) =>
        request.status === "Pending" ? (
          <div className="flex justify-end gap-2">
            <Button
              size="sm"
              variant="outline"
              className="h-7 text-xs text-destructive border-destructive/40 hover:bg-destructive/10"
              disabled={processingId === request.id}
              onClick={() => void handleReject(request)}
            >
              <X className="size-3 mr-1" />
              Reject
            </Button>
            <Button
              size="sm"
              className="h-7 text-xs"
              disabled={processingId === request.id || isSelf(request)}
              title={
                isSelf(request)
                  ? "Self-approval is prohibited by the server and by this guard"
                  : undefined
              }
              onClick={() => void handleApprove(request)}
            >
              <Check className="size-3 mr-1" />
              Approve
            </Button>
          </div>
        ) : null,
    },
  ]

  return (
    <div className="flex flex-col gap-6">
      <div className="flex justify-between items-center gap-3 flex-wrap">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Administrator Access Requests
          </h2>
          <p className="text-sm text-muted-foreground">
            {state.kind === "ready"
              ? `${pendingCount} pending · ${requests.length} total`
              : "Review and grant elevated administrative privileges."}
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => void loadRequests()} className="text-xs">
          Refresh Queue
        </Button>
      </div>

      <Card className="border-border shadow-xs overflow-hidden">
        <CardContent className="p-0">
          <DataTable<AdminApprovalRequestSummary>
            columns={columns}
            rows={requests}
            getRowKey={(request) => request.id}
            state={state.kind}
            emptyMessage="No administrator access requests."
            errorMessage={state.kind === "error" ? state.message : undefined}
            onRetry={() => void loadRequests()}
            caption="Administrator access requests"
          />
        </CardContent>
      </Card>
    </div>
  )
}
