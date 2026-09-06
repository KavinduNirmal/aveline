import { useCallback, useEffect, useState } from 'react'
import { useAuth, useUser } from '@clerk/react'
import { ShieldCheck } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import {
  approveAdminRequest,
  listAdminRequests,
  rejectAdminRequest,
  type AdminApprovalRequestSummary,
} from '@/lib/admin'
import { decodeJwtPayload } from '@/lib/auth'

const REVIEWER_ROLES = new Set(['moderator', 'admin', 'owner'])
const JWT_TEMPLATE = 'jwt-aveline-v1'

/** Post-auth landing page for owners and managers. */
export function Dashboard() {
  const { user } = useUser()
  const { getToken } = useAuth()

  const [isReviewer, setIsReviewer] = useState(false)
  const [requests, setRequests] = useState<AdminApprovalRequestSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [actioning, setActioning] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setRequests(await listAdminRequests())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load requests.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    async function checkReviewer() {
      const token = await getToken({ template: JWT_TEMPLATE })
      const role = token
        ? (decodeJwtPayload(token).user_role as string | undefined)?.toLowerCase()
        : undefined
      const viewer = role != null && REVIEWER_ROLES.has(role)
      if (!cancelled) {
        setIsReviewer(viewer)
        if (viewer) void refresh()
      }
    }
    void checkReviewer()
    return () => {
      cancelled = true
    }
  }, [getToken, refresh])

  const act = async (
    id: string,
    fn: (id: string) => Promise<AdminApprovalRequestSummary>,
  ) => {
    setActioning(id)
    setError(null)
    try {
      await fn(id)
      setRequests((prev) => prev.filter((r) => r.id !== id))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Action failed.')
    } finally {
      setActioning(null)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          Owner overview
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">
          Good day, {user?.firstName ?? user?.id}
        </h1>
      </div>

      <Separator />

      <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <CardTitle className="font-serif text-2xl font-medium">
              Approvals
            </CardTitle>
            <Badge variant="outline">Commerce</Badge>
          </div>
          <CardDescription>
            Pending order approvals land here once the commerce slice is built
            out.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">No pending approvals.</p>
        </CardContent>
      </Card>

      {isReviewer && (
        <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
          <CardHeader>
            <div className="flex items-center justify-between gap-4">
              <CardTitle className="font-serif text-2xl font-medium">
                Administrator access requests
              </CardTitle>
              <Badge variant="outline" className="gap-1">
                <ShieldCheck className="size-3" aria-hidden />
                Security
              </Badge>
            </div>
            <CardDescription>
              Review people who asked for administrator access after signing up.
              Approving grants them the admin role.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {error && <p className="text-sm text-destructive">{error}</p>}

            {loading ? (
              <p className="text-sm text-muted-foreground">Loading requests…</p>
            ) : requests.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No pending administrator access requests.
              </p>
            ) : (
              requests.map((request) => (
                <div
                  key={request.id}
                  className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-background/60 p-4"
                >
                  <div className="min-w-0">
                    <p className="font-medium">
                      {request.firstName} {request.lastName}
                      <span className="ml-2 text-sm font-normal text-muted-foreground">
                        {request.email}
                      </span>
                    </p>
                    <p className="text-xs text-muted-foreground">
                      Requested{' '}
                      {new Date(request.requestedAt).toLocaleString()}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={actioning === request.id}
                      onClick={() => void act(request.id, rejectAdminRequest)}
                    >
                      Reject
                    </Button>
                    <Button
                      size="sm"
                      disabled={actioning === request.id}
                      onClick={() => void act(request.id, approveAdminRequest)}
                    >
                      {actioning === request.id ? 'Working…' : 'Approve'}
                    </Button>
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>
      )}
    </div>
  )
}
