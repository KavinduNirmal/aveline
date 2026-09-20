import { useCallback, useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth, useClerk, useUser } from '@clerk/react'
import { Clock, Loader2, LogOut, RefreshCw } from 'lucide-react'

import { PageLoader } from '@/components/PageLoader'
import { Button } from '@/components/ui/button'
import { submitAdminRequest } from '@/lib/admin'
import { fetchAuthClaims } from '@/lib/admin/api'
import { JWT_TEMPLATE } from '@/lib/AuthApiBridge'
import { hasConsoleRole, isAdminSignUp } from '@/lib/admin-signup'

type Status = 'loading' | 'pending' | 'error'

/**
 * Holding screen for administrator sign-ups awaiting review. Administrator
 * accounts are not boutique tenants, so they are kept out of the onboarding
 * wizard and parked here until the Aveline team approves the request.
 *
 * Visiting this page also (re)queues the access request: `POST /admin/requests`
 * is idempotent while a Pending/Approved request exists, which makes the queue
 * self-healing if the sign-up page's submission was interrupted.
 */
export function AdminPendingPage() {
  const { isLoaded: authLoaded, isSignedIn, getToken } = useAuth()
  const { user, isLoaded: userLoaded } = useUser()
  const { signOut } = useClerk()

  const [status, setStatus] = useState<Status>('loading')
  const [approved, setApproved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const check = useCallback(async () => {
    setError(null)
    try {
      const claims = await fetchAuthClaims()
      if (hasConsoleRole(claims.roles)) {
        setApproved(true)
        return
      }

      await submitAdminRequest()
      setStatus('pending')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to check your request status.')
      setStatus('error')
    }
  }, [])

  useEffect(() => {
    if (!authLoaded || !userLoaded || !isSignedIn) return
    // check() only sets state after its awaits; the rule cannot see through it.
    // oxlint-disable-next-line react/set-state-in-effect
    void check()
  }, [authLoaded, userLoaded, isSignedIn, check])

  const recheck = async () => {
    setStatus('loading')
    setError(null)
    try {
      // Force a fresh token so a role granted since the last check becomes visible.
      await getToken({ template: JWT_TEMPLATE, skipCache: true })
    } catch {
      // Fall through: the claims read below surfaces any real error.
    }
    await check()
  }

  const signOutToLogin = () => {
    signOut(() => {
      window.location.href = '/sign-in'
    })
  }

  if (!authLoaded || !userLoaded) return <PageLoader />
  if (!isSignedIn) return <Navigate to="/sign-in" replace />
  // Only the administrator sign-up flow belongs here; everyone else uses the app.
  if (!isAdminSignUp(user)) return <Navigate to="/app" replace />
  if (approved) return <Navigate to="/admin" replace />

  return (
    <div className="flex min-h-screen items-center justify-center bg-[#faf7f6] px-4 py-10">
      <div className="w-full max-w-md">
        <div className="mb-6 flex flex-col items-center text-center">
          <div className="mb-3 flex size-12 items-center justify-center rounded-full border border-dashed border-[#7a303f]/30 bg-[#7a303f]/5 text-[#7a303f]">
            <Clock className="size-6" aria-hidden />
          </div>
          <h1 className="font-serif text-2xl font-medium text-neutral-900">Aveline</h1>
          <p className="mt-1 text-[11px] uppercase tracking-[0.3em] text-neutral-500">
            Administrator access
          </p>
        </div>

        <div className="rounded-2xl border border-neutral-200 bg-white p-7 text-center shadow-sm">
          <h2 className="font-serif text-xl font-medium text-neutral-900">
            {status === 'error' ? 'Couldn’t check your request' : 'Request pending review'}
          </h2>
          <p className="mt-2 text-sm leading-relaxed text-neutral-600">
            {status === 'error'
              ? error
              : 'Your request is queued for the Aveline team. You’ll be able to sign in to the administrator console once it is approved.'}
          </p>

          <Button
            type="button"
            disabled={status === 'loading'}
            onClick={() => void recheck()}
            className="mt-6 h-11 w-full rounded-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
          >
            {status === 'loading' ? (
              <>
                <Loader2 className="size-4 animate-spin" aria-hidden />
                Checking…
              </>
            ) : (
              <>
                <RefreshCw className="size-4" aria-hidden />
                Check status
              </>
            )}
          </Button>

          <Button
            type="button"
            variant="outline"
            onClick={signOutToLogin}
            className="mt-3 h-11 w-full rounded-full border-neutral-300 text-[#7a303f] hover:bg-neutral-50"
          >
            <LogOut className="size-4" aria-hidden />
            Sign out
          </Button>
        </div>

        {user?.primaryEmailAddress?.emailAddress && (
          <p className="mt-6 text-center text-sm text-neutral-500">
            Signed in as {user.primaryEmailAddress.emailAddress}
          </p>
        )}
      </div>
    </div>
  )
}
