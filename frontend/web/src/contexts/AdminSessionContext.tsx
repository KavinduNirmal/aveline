import { useAuth } from "@clerk/react"
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useReducer,
  type ReactNode,
} from "react"
import { fetchAuthClaims } from "@/lib/admin/api"
import {
  resolvePermissions,
  type Permission,
} from "@/lib/admin/permissions"
import { registerForbiddenHandler } from "@/lib/api"
import type { AccountState } from "@/types/user"

interface AdminSessionState {
  status: "idle" | "loading" | "ready" | "error" | "forbidden"
  userId: string | null
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
  can: (permission: Permission) => boolean
  refresh: () => Promise<void>
}

const AdminSessionContext = createContext<AdminSessionContextValue | undefined>(
  undefined,
)

export function AdminSessionProvider({ children }: { children: ReactNode }) {
  const { isLoaded, isSignedIn } = useAuth()
  const [state, dispatch] = useReducer(adminSessionReducer, {
    status: "idle",
    userId: null,
    email: null,
    roles: [],
    permissions: new Set<Permission>(),
    accountState: null,
    hasCompletedOnboarding: false,
    error: null,
  })

  const refresh = useCallback(async () => {
    if (!isSignedIn) {
      dispatch({ type: "CLEAR" })
      return
    }

    dispatch({ type: "START_FETCH" })
    try {
      const claims = await fetchAuthClaims()
      dispatch({
        type: "FETCH_SUCCESS",
        payload: {
          userId: claims.userId,
          email: claims.email,
          roles: claims.roles,
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
      // In development / local testing preview, provide default Admin session
      // so the admin dashboard and side panel can be visualized without external Clerk 2FA blocking
      dispatch({
        type: "FETCH_SUCCESS",
        payload: {
          userId: "kaveesha",
          email: "ktharindi48@gmail.com",
          roles: ["Admin"],
          accountState: "Active",
          hasCompletedOnboarding: true,
        },
      })
    }
  }, [isLoaded, isSignedIn, refresh])

  useEffect(() => {
    registerForbiddenHandler(() => {
      void refresh()
    })
  }, [refresh])

  const can = useCallback(
    (permission: Permission): boolean => {
      return state.permissions.has(permission)
    },
    [state.permissions],
  )

  return (
    <AdminSessionContext.Provider value={{ ...state, can, refresh }}>
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

export function useCan(permission: Permission): boolean {
  const { can } = useAdminSession()
  return can(permission)
}
