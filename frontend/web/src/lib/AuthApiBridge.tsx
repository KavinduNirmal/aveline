import { useAuth, useClerk } from '@clerk/react'
import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'

import type { ApiError } from './api-error'
import {
  registerAuthTokenGetter,
  registerForbiddenHandler,
  registerUnauthorizedHandler,
} from './api'

/** Clerk JWT template that mints the Aveline role claims. */
const JWT_TEMPLATE = 'jwt-aveline-v1'

/**
 * Mounted inside ClerkProvider + Router. Wires the axios client's token getter
 * and global 401 / 403 handling to Clerk and the router:
 * - requests are authorized with the `jwt-aveline-v1` token
 * - 401 signs the user out and returns to `/sign-in`
 * - 403 routes by problem type: `account-suspended` → `/suspended`,
 *   `onboarding-required` → `/onboarding`, anything else → `/forbidden`
 */
export function AuthApiBridge() {
  const { getToken } = useAuth()
  const { signOut } = useClerk()
  const navigate = useNavigate()

  useEffect(() => {
    registerAuthTokenGetter(() => getToken({ template: JWT_TEMPLATE }))
    registerUnauthorizedHandler(() => {
      signOut(() => navigate('/sign-in', { replace: true }))
    })
    registerForbiddenHandler((error: unknown) => {
      const apiError = error as ApiError | undefined
      const type = apiError?.type ?? ''
      if (type.includes('account-suspended')) {
        navigate('/suspended', { replace: true })
      } else if (type.includes('onboarding-required')) {
        navigate('/onboarding', { replace: true })
      } else {
        navigate('/forbidden', { replace: true })
      }
    })

    return () => {
      registerAuthTokenGetter(null)
      registerUnauthorizedHandler(null)
      registerForbiddenHandler(null)
    }
  }, [getToken, navigate, signOut])

  return null
}
