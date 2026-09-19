import { Navigate, Outlet } from "react-router-dom"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { PageLoader } from "@/components/PageLoader"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { ShieldAlert } from "lucide-react"

export function AdminRouteGuard() {
  const { status, roles, error, refresh } = useAdminSession()

  if (status === "idle" || status === "loading") {
    return <PageLoader />
  }

  if (status === "error") {
    return (
      <div className="flex min-h-screen items-center justify-center p-6 bg-background">
        <Card className="max-w-md w-full border-destructive/30 shadow-md">
          <CardHeader className="text-center">
            <div className="mx-auto w-12 h-12 rounded-full bg-destructive/10 text-destructive flex items-center justify-center mb-2">
              <ShieldAlert className="size-6" />
            </div>
            <CardTitle className="font-serif text-xl">Session Error</CardTitle>
            <CardDescription>{error || "Unable to verify administrative authorization."}</CardDescription>
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

  // Verify that caller holds at least one recognized management or admin role
  const isAdmitted = roles.some((role) => {
    const r = role.toLowerCase()
    return (
      r === "admin" ||
      r === "owner" ||
      r === "moderator" ||
      r === "org:boutique_owner" ||
      r === "org:boutique_manager" ||
      r === "org:boutique_supervisor"
    )
  })

  if (!isAdmitted) {
    return <Navigate to="/forbidden" replace />
  }

  return <Outlet />
}
