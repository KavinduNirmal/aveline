import { useAuth } from "@clerk/react"
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useReducer,
  useRef,
  useState,
  type ReactNode,
} from "react"
import { fetchAuthClaims } from "@/lib/admin/api"
import {
  resolvePermissions,
  type Permission,
} from "@/lib/admin/permissions"
import { decodeJwtPayload } from "@/lib/auth"
import { registerForbiddenHandler } from "@/lib/api"
import type { AccountState } from "@/types/user"

interface AdminSessionState {
  status: "idle" | "loading" | "ready" | "error" | "forbidden"
  userId: string | null
  /**
   * The Clerk subject (`sub`) of the caller. Self-approval is keyed on this and not on email:
   * the captured live API returns `email: null` on `/auth/claims` and `""` on every request row,
   * so an email comparison is `undefined === undefined` and passes.
   */
  clerkUserId: string | null
  email: string | null
  roles: string[]
  permissions: Set<Permission>
  accountState: AccountState | null
  hasCompletedOnboarding: boolean
  error: string | null
}

type AdminSessionAction =
  | { type: "START_FETCH" }
  | {
      type: "FETCH_SUCCESS"
      payload: {
        userId: string
        clerkUserId: string | null
        email: string | null
        roles: string[]
        accountState: AccountState | null
        hasCompletedOnboarding: boolean
      }
    }
  | { type: "FETCH_ERROR"; error: string }
  | { type: "CLEAR" }

function adminSessionReducer(
  state: AdminSessionState,
  action: AdminSessionAction,
): AdminSessionState {
  switch (action.type) {
    case "START_FETCH":
      return { ...state, status: "loading", error: null }
    case "FETCH_SUCCESS": {
      const permissions = resolvePermissions(action.payload.roles)
      return {
        ...state,
        status: "ready",
        userId: action.payload.userId,
        clerkUserId: action.payload.clerkUserId,
        email: action.payload.email,
        roles: action.payload.roles,
        permissions,
        accountState: action.payload.accountState,
        hasCompletedOnboarding: action.payload.hasCompletedOnboarding,
        error: null,
      }
    }
    case "FETCH_ERROR":
      return { ...state, status: "error", error: action.error }
    case "CLEAR":
      return {
        status: "idle",
        userId: null,
        clerkUserId: null,
        email: null,
        roles: [],
        permissions: new Set<Permission>(),
        accountState: null,
        hasCompletedOnboarding: false,
        error: null,
      }
    default:
      return state
  }
}

interface AdminSessionContextValue extends AdminSessionState {
  /**
   * True while `roles` comes from the JWT hint rather than from `/auth/claims`, so the console
   * can recognise a signed-in owner on first paint instead of flashing a forbidden state.
   */
  provisional: boolean
  can: (permission: Permission) => boolean
  refresh: () => Promise<void>
}

const AdminSessionContext = createContext<AdminSessionContextValue | undefined>(
  undefined,
)

export function AdminSessionProvider({ children }: { children: ReactNode }) {
  const { isLoaded, isSignedIn, getToken } = useAuth()
  const [state, dispatch] = useReducer(adminSessionReducer, {
    status: "idle",
    userId: null,
    clerkUserId: null,
    email: null,
    roles: [],
    permissions: new Set<Permission>(),
    accountState: null,
    hasCompletedOnboarding: false,
    error: null,
  })

  // Role names from the JWT hint, used only until `/auth/claims` settles. The hint is never
  // authoritative: `state.roles` replaces it the moment the session is ready.
  const [provisionalRoles, setProvisionalRoles] = useState<string[]>([])

  useEffect(() => {
    if (!isLoaded || !isSignedIn || typeof getToken !== "function") {
      setProvisionalRoles([])
      return
    }
    let cancelled = false
    void (async () => {
      const token = await getToken({ template: "jwt-aveline-v1" })
      if (!token || cancelled) return
      const payload = decodeJwtPayload(token)
      const hinted = [payload.user_role, payload.org_role].filter(
        (role): role is string => typeof role === "string" && role.length > 0,
      )
      setProvisionalRoles(hinted)
    })()
    return () => {
      cancelled = true
    }
  }, [isLoaded, isSignedIn, getToken])

  // Latest settled status, read by the 403 handler without re-registering it.
  const statusRef = useRef(state.status)
  useEffect(() => {
    statusRef.current = state.status
  }, [state.status])

  const refresh = useCallback(async () => {
    if (!isSignedIn) {
      dispatch({ type: "CLEAR" })
      return
    }

    dispatch({ type: "START_FETCH" })
    try {
      const claims = await fetchAuthClaims()
      const effectiveRoles = Array.from(
        new Set([
          ...claims.roles,
          ...(claims.account.userRole ? [claims.account.userRole] : []),
          ...(claims.account.organizationRole ? [claims.account.organizationRole] : []),
        ]),
      )
      dispatch({
        type: "FETCH_SUCCESS",
        payload: {
          userId: claims.userId,
          clerkUserId: claims.claims?.sub?.[0] ?? null,
          email: claims.email,
          roles: effectiveRoles,
          accountState: claims.account.accountState,
          hasCompletedOnboarding: claims.account.hasCompletedOnboarding,
        },
      })
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Failed to load admin claims"
      dispatch({ type: "FETCH_ERROR", error: msg })
    }
  }, [isSignedIn])

  useEffect(() => {
    if (!isLoaded) return
    if (isSignedIn) {
      void refresh()
    } else {
      // Signed out. The console has no identity to work with, so the session is cleared.
      //
      // The delivered provider fabricated an admin session here (a literal user id and
      // `roles: ["Admin"]`). That synthesis is what made `AdminRouteGuard` admit a signed-out
      // visitor and what let the dashboard render fiction against six `401`s. There is no
      // fallback identity: a console that cannot identify its caller has nothing to show.
      dispatch({ type: "CLEAR" })
    }
  }, [isLoaded, isSignedIn, refresh])

  useEffect(() => {
    registerForbiddenHandler(() => {
      // Only re-resolve claims while the session is still settling. Once ready, a
      // 403 is a genuine authorization denial: refreshing here loops forever
      // (refresh -> new context value -> dashboard refetch -> 403 -> refresh).
      if (statusRef.current !== "ready") {
        void refresh()
      }
    })
    // Unregister on unmount, so leaving the admin tree does not leave a handler behind that
    // refreshes a session nobody is rendering.
    return () => registerForbiddenHandler(null)
  }, [refresh])

  const can = useCallback(
    (permission: Permission): boolean => {
      return state.permissions.has(permission)
    },
    [state.permissions],
  )

  const provisional = state.status !== "ready" && provisionalRoles.length > 0

  return (
    <AdminSessionContext.Provider
      value={{
        ...state,
        roles: state.status === "ready" ? state.roles : provisionalRoles,
        provisional,
        can,
        refresh,
      }}
    >
      {children}
    </AdminSessionContext.Provider>
  )
}

export function useAdminSession(): AdminSessionContextValue {
  const ctx = useContext(AdminSessionContext)
  if (!ctx) {
    throw new Error("useAdminSession must be used within AdminSessionProvider")
  }
  return ctx
}
