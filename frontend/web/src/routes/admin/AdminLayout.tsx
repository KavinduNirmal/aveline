import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { useState } from "react"
import { Navigate, useParams } from "react-router-dom"

import { AdminSessionProvider } from "@/contexts/AdminSessionContext"
import { useUserContext } from "@/contexts/UserContext"
import { AdminErrorBoundary } from "@/components/admin/guard/AdminErrorBoundary"
import { AdminRouteGuard } from "@/components/admin/guard/AdminRouteGuard"

/** Creates the admin tree's server-state cache. */
export function createAdminQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        retry: 1,
        refetchOnWindowFocus: false,
      },
    },
  })
}

export function AdminRootRedirect() {
  const { user } = useUserContext()
  if (!user?.id) {
    // No identity, no console. The delivered version fell back to a literal user id, which
    // is how a signed-out visitor reached a fabricated dashboard.
    return <Navigate to="/sign-in" replace />
  }
  return <Navigate to={`/admin/${user.id}/dashboard`} replace />
}

/**
 * The admin tree's entry element.
 *
 * `QueryClientProvider` is mounted **here**, inside the admin tree, and never at the app root
 * (strategy C4 + Q8). The tenant dashboard therefore shares no query cache with the console and
 * its data path is untouched.
 */
export function AdminLayout() {
  const { userId } = useParams<{ userId: string }>()
  const [queryClient] = useState(createAdminQueryClient)

  if (!userId) {
    return <AdminRootRedirect />
  }

  return (
    <QueryClientProvider client={queryClient}>
      <AdminSessionProvider>
        <AdminErrorBoundary>
          <AdminRouteGuard />
        </AdminErrorBoundary>
      </AdminSessionProvider>
    </QueryClientProvider>
  )
}
