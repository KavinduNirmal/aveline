import { Navigate, useParams } from "react-router-dom"
import { useUserContext } from "@/contexts/UserContext"
import { AdminSessionProvider } from "@/contexts/AdminSessionContext"
import { AdminRouteGuard } from "@/components/admin/AdminRouteGuard"

export function AdminRootRedirect() {
  const { user } = useUserContext()
  const userId = user?.id || "kaveesha"
  return <Navigate to={`/admin/${userId}/dashboard`} replace />
}

export function AdminLayout() {
  const { userId } = useParams<{ userId: string }>()

  if (!userId) {
    return <AdminRootRedirect />
  }

  return (
    <AdminSessionProvider>
      <AdminRouteGuard />
    </AdminSessionProvider>
  )
}
