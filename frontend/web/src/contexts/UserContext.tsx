import { useAuth } from '@clerk/react'
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react'

import {
  apiClient,
  registerAccountStateHandler,
  registerOnboardingStatusHandler,
} from '@/lib/api'
import type { AccountState, CompleteOnboardingRequest, UserDto } from '@/types/user'

interface UserContextValue {
  user: UserDto | null
  isOnboarded: boolean | null
  accountState: AccountState | null
  isLoading: boolean
  error: string | null
  refreshUser: () => Promise<UserDto | null>
  completeOnboarding: (data: CompleteOnboardingRequest) => Promise<UserDto>
}

const UserContext = createContext<UserContextValue | undefined>(undefined)

export function UserProvider({ children }: { children: ReactNode }) {
  const { isLoaded, isSignedIn } = useAuth()
  const [user, setUser] = useState<UserDto | null>(null)
  const [isOnboarded, setIsOnboarded] = useState<boolean | null>(null)
  const [accountState, setAccountState] = useState<AccountState | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [error, setError] = useState<string | null>(null)

  const applyUser = useCallback((next: UserDto | null) => {
    setUser(next)
    setIsOnboarded(next?.hasCompletedOnboarding ?? null)
    setAccountState(next?.accountState ?? null)
  }, [])

  const refreshUser = useCallback(async (): Promise<UserDto | null> => {
    if (!isSignedIn) {
      applyUser(null)
      setIsLoading(false)
      return null
    }

    try {
      setIsLoading(true)
      setError(null)
      const response = await apiClient.get<UserDto>('/api/v1/users/me')
      applyUser(response.data)
      return response.data
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to fetch user profile'
      setError(message)
      return null
    } finally {
      setIsLoading(false)
    }
  }, [applyUser, isSignedIn])

  const completeOnboarding = useCallback(
    async (data: CompleteOnboardingRequest): Promise<UserDto> => {
      setIsLoading(true)
      setError(null)
      try {
        const response = await apiClient.post<UserDto>('/api/v1/users/onboarding', data)
        applyUser(response.data)
        return response.data
      } catch (err: unknown) {
        const message =
          err instanceof Error ? err.message : 'Failed to complete onboarding'
        setError(message)
        throw err
      } finally {
        setIsLoading(false)
      }
    },
    [applyUser],
  )

  useEffect(() => {
    registerOnboardingStatusHandler((status) => {
      setIsOnboarded(status)
      setUser((prev) => (prev ? { ...prev, hasCompletedOnboarding: status } : null))
    })
    registerAccountStateHandler((state) => {
      setAccountState(state)
      setUser((prev) => (prev ? { ...prev, accountState: state } : null))
    })
    return () => {
      registerOnboardingStatusHandler(null)
      registerAccountStateHandler(null)
    }
  }, [])

  useEffect(() => {
    if (!isLoaded) return
    if (isSignedIn) {
      void refreshUser()
    } else {
      applyUser(null)
      setIsLoading(false)
    }
  }, [applyUser, isLoaded, isSignedIn, refreshUser])

  return (
    <UserContext.Provider
      value={{
        user,
        isOnboarded,
        accountState,
        isLoading,
        error,
        refreshUser,
        completeOnboarding,
      }}
    >
      {children}
    </UserContext.Provider>
  )
}

export function useUserContext(): UserContextValue {
  const context = useContext(UserContext)
  if (!context) {
    throw new Error('useUserContext must be used within a UserProvider')
  }
  return context
}
