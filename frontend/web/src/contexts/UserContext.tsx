import { useAuth } from '@clerk/react'
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react'

import { apiClient, registerOnboardingStatusHandler } from '@/lib/api'
import type { CompleteOnboardingRequest, UserDto } from '@/types/user'

interface UserContextValue {
  user: UserDto | null
  isOnboarded: boolean | null
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
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [error, setError] = useState<string | null>(null)

  const refreshUser = useCallback(async (): Promise<UserDto | null> => {
    if (!isSignedIn) {
      setUser(null)
      setIsOnboarded(null)
      setIsLoading(false)
      return null
    }

    try {
      setIsLoading(true)
      setError(null)
      const response = await apiClient.get<UserDto>('/api/v1/users/me')
      setUser(response.data)
      setIsOnboarded(response.data.hasCompletedOnboarding)
      return response.data
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to fetch user profile'
      setError(message)
      return null
    } finally {
      setIsLoading(false)
    }
  }, [isSignedIn])

  const completeOnboarding = useCallback(
    async (data: CompleteOnboardingRequest): Promise<UserDto> => {
      setIsLoading(true)
      setError(null)
      try {
        const response = await apiClient.post<UserDto>('/api/v1/users/onboarding', data)
        setUser(response.data)
        setIsOnboarded(true)
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
    [],
  )

  useEffect(() => {
    registerOnboardingStatusHandler((status) => {
      setIsOnboarded(status)
      if (user) {
        setUser((prev) => (prev ? { ...prev, hasCompletedOnboarding: status } : null))
      }
    })
    return () => {
      registerOnboardingStatusHandler(null)
    }
  }, [user])

  useEffect(() => {
    if (!isLoaded) return
    if (isSignedIn) {
      void refreshUser()
    } else {
      setUser(null)
      setIsOnboarded(null)
      setIsLoading(false)
    }
  }, [isLoaded, isSignedIn, refreshUser])

  return (
    <UserContext.Provider
      value={{
        user,
        isOnboarded,
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
