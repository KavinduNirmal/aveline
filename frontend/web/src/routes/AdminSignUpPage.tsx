import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth, useClerk, useSignUp } from '@clerk/react'
import { BadgeCheck, Loader2, ShieldCheck } from 'lucide-react'

import { Blossom } from '@/components/auth/Blossom'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { submitAdminRequest } from '@/lib/admin'

type RequestState = 'idle' | 'submitting' | 'done' | 'failed'

/**
 * Administrator sign-up — intentionally quiet and plain. After the email is
 * verified the access request is queued for review by the Aveline team; approval
 * grants the Clerk admin role (issue #59).
 */
export function AdminSignUpPage() {
  const { isLoaded } = useAuth()
  const { signOut } = useClerk()
  const { signUp, errors, fetchStatus } = useSignUp()

  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [verifying, setVerifying] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [requestState, setRequestState] = useState<RequestState>('idle')
  // Tracked separately from requestState so "Try again" retries only the request
  // submission once Clerk has already turned the sign-up into an active session.
  const [finalized, setFinalized] = useState(false)

  const busyState = busy || fetchStatus === 'fetching'

  const surf = (e: { message?: string | null } | null | undefined) => {
    if (e?.message) setError(e.message)
  }

  /**
   * Turns the completed Clerk sign-up into an active session, then submits the
   * admin access request. Deliberately does NOT pass a `navigate` callback to
   * `finalize`: on a Clerk development instance that callback redirects with the
   * dev-browser token, which unloads the page and drops the request submission.
   * We stay on this screen and let the user sign out from the success state.
   */
  async function finish() {
    setError(null)
    setRequestState('submitting')

    try {
      if (!finalized) {
        const { error: finalizeError } = (await signUp!.finalize()) ?? { error: null }
        if (finalizeError) {
          surf(finalizeError)
          setRequestState('failed')
          return
        }
        setFinalized(true)
      }

      await submitAdminRequest()
      setRequestState('done')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to complete your request.')
      setRequestState('failed')
    }
  }

  /**
   * Advances a verified sign-up. Clerk's verify call can report
   * "already verified" for an attempt that a previous (interrupted) click left
   * verified but unfinalised, so we always re-check the live status before
   * deciding that verification failed.
   */
  async function continueAfterVerification(verifyError: { message?: string | null } | null) {
    // Widened to string: Clerk mutates the resource in place, so TypeScript's
    // control-flow narrowing would otherwise reject the re-read below.
    const status: string = signUp!.status
    if (status === 'complete') {
      await finish()
      return
    }

    if (verifyError) {
      surf(verifyError)
      return
    }

    // Supply any field Clerk still requires (username is the usual one) before
    // concluding that the sign-up cannot be completed.
    const missing = signUp!.missingFields ?? []
    if (missing.includes('username')) {
      const fallback =
        (email.split('@')[0] || 'admin').toLowerCase().replace(/[^a-z0-9_]/g, '') || 'admin'
      const { error: updateError } = await signUp!.update({ username: fallback })
      if (updateError) {
        surf(updateError)
        return
      }
    }

    const finalStatus: string = signUp!.status
    if (finalStatus === 'complete') {
      await finish()
      return
    }

    setError(
      missing.length > 0
        ? `Your email is verified, but these fields are still required: ${missing.join(', ')}.`
        : 'Your email is verified, but the account could not be finalised. Select "Start over" and try again.',
    )
  }

  const signOutToLogin = () => {
    signOut(() => {
      window.location.href = '/sign-in'
    })
  }

  const create = async (e: FormEvent) => {
    e.preventDefault()
    if (!email || !password) return
    setError(null)
    setBusy(true)
    try {
      const { error: err } = await signUp!.password({
        emailAddress: email,
        password,
        firstName: firstName || undefined,
        lastName: lastName || undefined,
        unsafeMetadata: { accountType: 'admin' },
      })
      if (err) {
        surf(err)
        return
      }

      if (signUp!.status === 'complete') {
        await finish()
        return
      }

      if ((signUp!.unverifiedFields ?? []).includes('email_address')) {
        const send = await signUp!.verifications.sendEmailCode()
        if (send.error) {
          surf(send.error)
          return
        }
        setVerifying(true)
      }
    } catch (err) {
      surf(err as { message?: string })
    } finally {
      setBusy(false)
    }
  }

  const verify = async (e: FormEvent) => {
    e.preventDefault()
    if (!code) return
    setError(null)
    setBusy(true)
    try {
      const { error: err } = await signUp!.verifications.verifyEmailCode({ code })
      await continueAfterVerification(err)
    } catch (err) {
      surf(err as { message?: string })
    } finally {
      setBusy(false)
    }
  }

  const resend = async () => {
    setError(null)
    setBusy(true)
    try {
      surf((await signUp!.verifications.sendEmailCode()).error)
    } finally {
      setBusy(false)
    }
  }

  const startOver = async () => {
    setError(null)
    setBusy(true)
    try {
      await signUp?.reset()
      setVerifying(false)
      setCode('')
      setFinalized(false)
      setRequestState('idle')
    } finally {
      setBusy(false)
    }
  }

  const fieldErr = (errors?.fields as
    | { emailAddress?: { message?: string }; password?: { message?: string }; code?: { message?: string } }
    | undefined)
  const shownError =
    error ??
    (errors?.global as { message?: string }[] | null)?.[0]?.message ??
    (verifying
      ? fieldErr?.code?.message
      : fieldErr?.emailAddress?.message ?? fieldErr?.password?.message)

  const heading = (
    <div className="mb-6 flex flex-col items-center text-center">
      <div className="mb-3 flex size-12 items-center justify-center rounded-full border border-dashed border-[#7a303f]/30 bg-[#7a303f]/5 text-[#7a303f]">
        <Blossom className="size-6" />
      </div>
      <h1 className="font-serif text-2xl font-medium text-neutral-900">Aveline</h1>
      <p className="mt-1 text-[11px] uppercase tracking-[0.3em] text-neutral-500">
        Administrator sign-up
      </p>
    </div>
  )

  return (
    <div className="flex min-h-screen items-center justify-center bg-[#faf7f6] px-4 py-10">
      <div className="w-full max-w-md">
        {heading}

        <div className="rounded-2xl border border-neutral-200 bg-white p-7 shadow-sm">
          {/*
            Clerk's bot sign-up protection renders its challenge into this node.
            It must stay mounted for the whole sign-up attempt, so it lives here
            rather than inside the (conditionally rendered) credentials step.
          */}
          <div id="clerk-captcha" />

          {requestState === 'done' ? (
            <div className="flex flex-col items-center py-4 text-center">
              <div className="mb-3 flex size-14 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
                <BadgeCheck className="size-7" aria-hidden />
              </div>
              <h2 className="font-serif text-xl font-medium text-neutral-900">
                Request received
              </h2>
              <p className="mt-2 text-sm leading-relaxed text-neutral-600">
                Your email is verified. The Aveline team will review your request —
                you’ll be able to sign in as an administrator once it’s approved.
              </p>
              <ol className="mt-5 w-full space-y-2 text-left text-sm text-neutral-500">
                <li className="flex gap-2"><span className="text-[#7a303f]">1.</span> Sign out below.</li>
                <li className="flex gap-2"><span className="text-[#7a303f]">2.</span> Wait for approval from the Aveline team.</li>
                <li className="flex gap-2"><span className="text-[#7a303f]">3.</span> Sign in again — your session will carry the admin role.</li>
              </ol>
              <Button
                type="button"
                variant="outline"
                onClick={signOutToLogin}
                className="mt-6 h-11 w-full rounded-full border-neutral-300 text-[#7a303f] hover:bg-neutral-50"
              >
                Sign out &amp; return to login
              </Button>
            </div>
          ) : requestState === 'failed' || requestState === 'submitting' ? (
            <div className="flex flex-col items-center py-4 text-center">
              <div className="mb-3 flex size-14 items-center justify-center rounded-full bg-red-100 text-red-700">
                <ShieldCheck className="size-7" aria-hidden />
              </div>
              <h2 className="font-serif text-xl font-medium text-neutral-900">
                Couldn’t submit your request
              </h2>
              <p className="mt-2 text-sm leading-relaxed text-neutral-600">
                {shownError}
              </p>
              <Button
                type="button"
                disabled={requestState === 'submitting'}
                onClick={() => void finish()}
                className="mt-6 h-11 w-full rounded-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
              >
                {requestState === 'submitting' ? (
                  <>
                    <Loader2 className="size-4 animate-spin" aria-hidden />
                    Submitting…
                  </>
                ) : (
                  'Try again'
                )}
              </Button>
            </div>
          ) : (
            <>
              <p className="text-sm leading-relaxed text-neutral-600">
                Administrator access is restricted and granted after review by the
                Aveline team.
              </p>

              <div className="my-5 border-t-2 border-dashed border-neutral-200" />

              {shownError && (
                <div className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
                  {shownError}
                </div>
              )}

              {!isLoaded || !signUp ? (
                <p className="py-8 text-center text-sm text-neutral-400">Preparing…</p>
              ) : !verifying ? (
                <form onSubmit={create} className="flex flex-col gap-4">
                  <div className="grid grid-cols-2 gap-3">
                    <div className="flex flex-col gap-2">
                      <Label htmlFor="admin-first" className="text-neutral-600">
                        First name
                      </Label>
                      <Input
                        id="admin-first"
                        value={firstName}
                        onChange={(e) => setFirstName(e.target.value)}
                        className="h-11 rounded-full border-neutral-300 bg-neutral-50 px-4 text-[15px]"
                      />
                    </div>
                    <div className="flex flex-col gap-2">
                      <Label htmlFor="admin-last" className="text-neutral-600">
                        Last name
                      </Label>
                      <Input
                        id="admin-last"
                        value={lastName}
                        onChange={(e) => setLastName(e.target.value)}
                        className="h-11 rounded-full border-neutral-300 bg-neutral-50 px-4 text-[15px]"
                      />
                    </div>
                  </div>
                  <div className="flex flex-col gap-2">
                    <Label htmlFor="admin-email" className="text-neutral-600">
                      Work email
                    </Label>
                    <Input
                      id="admin-email"
                      type="email"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="you@aveline.lk"
                      className="h-11 rounded-full border-neutral-300 bg-neutral-50 px-4 text-[15px]"
                    />
                  </div>
                  <div className="flex flex-col gap-2">
                    <Label htmlFor="admin-password" className="text-neutral-600">
                      Password
                    </Label>
                    <Input
                      id="admin-password"
                      type="password"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="At least 8 characters"
                      className="h-11 rounded-full border-neutral-300 bg-neutral-50 px-4 text-[15px]"
                    />
                  </div>

                  <Button
                    type="submit"
                    disabled={busyState || !email || !password}
                    className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-[15px] font-semibold text-white hover:bg-[#8c3b4c]"
                  >
                    {busyState ? 'Submitting…' : 'Request administrator access'}
                  </Button>
                </form>
              ) : (
                <form onSubmit={verify} className="flex flex-col gap-4">
                  <div className="flex flex-col gap-2">
                    <Label htmlFor="admin-code" className="text-neutral-600">
                      Verification code
                    </Label>
                    <Input
                      id="admin-code"
                      inputMode="numeric"
                      value={code}
                      onChange={(e) =>
                        setCode(e.target.value.replace(/\D/g, '').slice(0, 6))
                      }
                      placeholder="••••••"
                      className="h-11 rounded-full border-neutral-300 bg-neutral-50 text-center font-mono text-lg tracking-[0.35em]"
                    />
                    <p className="text-sm text-neutral-500">
                      We emailed a code to {email}.
                    </p>
                  </div>
                  <Button
                    type="submit"
                    disabled={busyState || !code}
                    className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
                  >
                    {busyState ? 'Verifying…' : 'Verify email'}
                  </Button>
                  <div className="flex items-center justify-between text-sm">
                    <button
                      type="button"
                      onClick={resend}
                      disabled={busyState}
                      className="text-neutral-500 hover:text-neutral-700"
                    >
                      Resend code
                    </button>
                    <button
                      type="button"
                      onClick={startOver}
                      disabled={busyState}
                      className="text-neutral-500 hover:text-neutral-700"
                    >
                      Start over
                    </button>
                  </div>
                </form>
              )}
            </>
          )}
        </div>

        <p className="mt-6 text-center text-sm text-neutral-500">
          <Link to="/sign-in" className="text-[#7a303f] hover:underline">
            Back to sign in
          </Link>
        </p>
      </div>
    </div>
  )
}
