import { useEffect, useState } from "react"
import { listAdminRequests, approveAdminRequest, rejectAdminRequest } from "@/lib/admin/api"
import type { AdminApprovalRequestSummary } from "@/types/admin"
import { Card } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Check, X } from "lucide-react"
import { toast } from "sonner"
import { useAdminSession } from "@/contexts/AdminSessionContext"

export function AdminRequestsView() {
  const [requests, setRequests] = useState<AdminApprovalRequestSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [processingId, setProcessingId] = useState<string | null>(null)
  const { email } = useAdminSession()

  const loadRequests = async () => {
    setLoading(true)
    try {
      const data = await listAdminRequests()
      setRequests(data)
    } catch {
      toast.error("Failed to load admin approval requests")
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadRequests()
  }, [])

  const handleApprove = async (req: AdminApprovalRequestSummary) => {
    if (req.email.toLowerCase() === email?.toLowerCase()) {
      toast.error("Self-approval prohibited: An administrator cannot approve their own elevation request.")
      return
    }

    setProcessingId(req.id)
    try {
      await approveAdminRequest(req.id)
      toast.success(`Approved admin access for ${req.email}`)
      void loadRequests()
    } catch (err: any) {
      toast.error(err?.message || "Failed to approve request")
    } finally {
      setProcessingId(null)
    }
  }

  const handleReject = async (req: AdminApprovalRequestSummary) => {
    setProcessingId(req.id)
    try {
      await rejectAdminRequest(req.id)
      toast.success(`Rejected request for ${req.email}`)
      void loadRequests()
    } catch (err: any) {
      toast.error(err?.message || "Failed to reject request")
    } finally {
      setProcessingId(null)
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Administrator Access Requests
          </h2>
          <p className="text-sm text-muted-foreground">
            Review and grant elevated administrative privileges to verified staff members.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => void loadRequests()} className="text-xs">
          Refresh Queue
        </Button>
      </div>

      <Card className="border-border shadow-xs overflow-hidden">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Candidate</TableHead>
              <TableHead>Email</TableHead>
              <TableHead>Requested Date</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-xs text-muted-foreground">
                  Loading requests queue...
                </TableCell>
              </TableRow>
            ) : requests.length === 0 ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-xs text-muted-foreground">
                  No pending administrator access requests.
                </TableCell>
              </TableRow>
            ) : (
              requests.map((r) => {
                const isSelf = r.email.toLowerCase() === email?.toLowerCase()
                return (
                  <TableRow key={r.id}>
                    <TableCell className="font-medium text-foreground">
                      {r.firstName} {r.lastName}
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">{r.email}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {new Date(r.requestedAt).toLocaleString()}
                    </TableCell>
                    <TableCell>
                      <Badge
                        variant={r.status === "Approved" ? "default" : r.status === "Rejected" ? "destructive" : "secondary"}
                        className="text-[11px]"
                      >
                        {r.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right space-x-2">
                      {r.status === "Pending" && (
                        <>
                          <Button
                            size="sm"
                            variant="outline"
                            className="h-7 text-xs text-destructive border-destructive/40 hover:bg-destructive/10"
                            disabled={processingId === r.id}
                            onClick={() => void handleReject(r)}
                          >
                            <X className="size-3 mr-1" />
                            Reject
                          </Button>
                          <Button
                            size="sm"
                            className="h-7 text-xs"
                            disabled={processingId === r.id || isSelf}
                            onClick={() => void handleApprove(r)}
                            title={isSelf ? "Self-approval disabled" : undefined}
                          >
                            <Check className="size-3 mr-1" />
                            Approve
                          </Button>
                        </>
                      )}
                    </TableCell>
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
