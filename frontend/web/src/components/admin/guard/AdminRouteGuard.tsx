import { useUser } from "@clerk/react"
import { Navigate, Outlet } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { useAdminScope } from "@/hooks/useAdminScope"
import { hasConsoleRole, isAdminSignUp } from "@/lib/admin-signup"
import { PageLoader } from "@/components/PageLoader"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ShieldAlert } from "lucide-react"

import { ForbiddenState } from "./ForbiddenState"

/**
 * The console's front door.
 *
 * Two checks, in order:
 *
 * 1. **Who may open the console at all** — `owner` or `admin` (strategy C2). The role list comes
 *    from {@link hasConsoleRole} in `lib/admin-signup.ts`; this component does not re-list roles.
 *    A `moderator` is refused **with a stated reason** rather than admitted to a panel of four
 *    sections that can only return `403`.
 * 2. **What `{user_Id}` means** — the resolved {@link AdminScope}. A segment that resolves to
 *    nothing renders an in-shell not-available state, never a guessed user's data.
 */
export function AdminRouteGuard() {
  const { status, roles, error, refresh, userId } = useAdminSession()
  const { user } = useUser()
  const scopeState = useAdminScope()

  if (status === "idle" || status === "loading") {
    return <PageLoader />
  }

  if (status === "error") {
    return (
      <div className="flex min-h-dvh items-center justify-center p-6 bg-background">
        <Card className="max-w-md w-full border-destructive/30 shadow-md">
          <CardHeader className="text-center">
            <div className="mx-auto w-12 h-12 rounded-full bg-destructive/10 text-destructive flex items-center justify-center mb-2">
              <ShieldAlert className="size-6" />
            </div>
            <CardTitle className="font-serif text-xl">Session Error</CardTitle>
            <CardDescription>
              {error || "Unable to verify administrative authorization."}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex justify-center">
            <Button onClick={() => void refresh()} variant="outline">
              Retry Authorization
            </Button>
          </CardContent>
        </Card>
      </div>
    )
  }

  if (!hasConsoleRole(roles)) {
    // A pending administrator sign-up has no role yet: park them on the review screen rather
    // than the generic forbidden state.
    if (isAdminSignUp(user)) {
      return <Navigate to="/admin/pending" replace />
    }
    return (
      <ForbiddenState reason="The administrator console is limited to the owner and admin roles. Your account holds neither, so no administrator sections are shown." />
    )
  }

  if (scopeState.status === "resolving") {
    return <PageLoader />
  }

  if (scopeState.scope.kind === "unknown") {
    // C1 option (c): a segment that does not resolve to a user becomes a restatement of the
    // caller rather than a guessed user. Falling back to the caller's own console keeps the
    // console usable when the resolver is unproven (Q1) or the id simply does not exist, and it
    // still shows nobody else's data.
    if (userId !== null) {
      return <Navigate to={`/admin/${userId}/dashboard`} replace />
    }
    return (
      <ForbiddenState
        title="Console scope not available"
        reason="This console address does not resolve to an account you can open. Nothing is shown rather than a guessed user's data."
      />
    )
  }

  if (scopeState.scope.kind === "forbidden") {
    return (
      <ForbiddenState
        title="Console scope not permitted"
        reason="Your account may not open the console for this user."
      />
    )
  }

  return <Outlet />
}
