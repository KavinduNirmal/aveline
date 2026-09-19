import { useEffect, useState } from "react"
import { useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { useUserContext } from "@/contexts/UserContext"
import { searchAdminUsers } from "@/lib/admin/api"
import { resolveAdminScope, type AdminScope } from "@/lib/admin/scope"

export interface AdminScopeState {
  status: "resolving" | "settled"
  scope: AdminScope
}

/**
 * Resolves the `{user_Id}` segment for the admin tree (strategy C1).
 *
 * The resolver is injected rather than inlined so the exact-`id` rule is unit-testable without
 * React: `searchAdminUsers` matches `q` against email, name, username and `clerkId`
 * (`UserRepository.cs:61-70`) — **not** against `id` — so an item is only accepted when its `id`
 * is exactly the segment.
 *
 * Two id spaces meet here and both must be treated as "the caller":
 * `/auth/claims` returns the **Clerk subject** (`user_…`), while the console's URLs carry the
 * application user's **database id**, a UUIDv7 `Guid` (`User.cs:11`) from `GET /users/me`. The
 * URL is built from the database id, so comparing only against the Clerk subject would never
 * recognise a caller's own console.
 */
export function useAdminScope(): AdminScopeState {
  const { userId: segment } = useParams<{ userId: string }>()
  const { status, userId, can } = useAdminSession()
  const { user } = useUserContext()
  const [state, setState] = useState<AdminScopeState>({
    status: "resolving",
    scope: { kind: "unknown" },
  })

  const canReadUsers = can("admin:users:read")
  const databaseUserId = user?.id ?? null

  useEffect(() => {
    if (status !== "ready") {
      setState({ status: "resolving", scope: { kind: "unknown" } })
      return
    }

    let cancelled = false

    void (async () => {
      let scope: AdminScope
      try {
        scope = await resolveAdminScope({
          segment,
          sessionUserId: userId,
          selfUserIds: [
            ...(databaseUserId !== null ? [databaseUserId] : []),
            ...(userId !== null ? [userId] : []),
          ],
          canReadUsers,
          findUserById: async (id) => {
            const page = await searchAdminUsers({ q: id, pageSize: 5 })
            return page.items.find((candidate) => candidate.id === id) ?? null
          },
        })
      } catch {
        // A failed lookup (401, network, 403 on the users route) is not a scope. Resolving it to
        // `unknown` lets `AdminRouteGuard` fall back to the caller's own console instead of
        // leaving the console stuck on a loader forever.
        scope = { kind: "unknown" }
      }
      if (!cancelled) setState({ status: "settled", scope })
    })()

    return () => {
      cancelled = true
    }
  }, [segment, userId, databaseUserId, status, canReadUsers])

  return state
}
