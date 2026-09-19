import { useEffect, useState } from "react"
import { useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
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
 * (`UserRepository.cs:61-70`), so an item is only accepted when its `id` is **exactly** the
 * segment.
 */
export function useAdminScope(): AdminScopeState {
  const { userId: segment } = useParams<{ userId: string }>()
  const { status, userId, can } = useAdminSession()
  const [state, setState] = useState<AdminScopeState>({
    status: "resolving",
    scope: { kind: "unknown" },
  })

  const canReadUsers = can("admin:users:read")

  useEffect(() => {
    if (status !== "ready") {
      setState({ status: "resolving", scope: { kind: "unknown" } })
      return
    }

    let cancelled = false

    void (async () => {
      const scope = await resolveAdminScope({
        segment,
        sessionUserId: userId,
        canReadUsers,
        findUserById: async (id) => {
          const page = await searchAdminUsers({ q: id, pageSize: 5 })
          return page.items.find((candidate) => candidate.id === id) ?? null
        },
      })
      if (!cancelled) setState({ status: "settled", scope })
    })()

    return () => {
      cancelled = true
    }
  }, [segment, userId, status, canReadUsers])

  return state
}
