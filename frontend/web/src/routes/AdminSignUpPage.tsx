import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth, useSignUp } from '@clerk/react'

import { Blossom } from '@/components/auth/Blossom'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * Unassuming administrator sign-up. Plain, quiet, deliberately free of the
 * marketing art — admin access is provisioned and verified by the Aveline team.
 */
export function AdminSignUpPage() {
  const { isLoaded } = useAuth()
  const { signUp, errors, fetchStatus } = useSignUp()
  const navigate = useNavigate()

  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [verifying, setVerifying] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const busyState = busy || fetchStatus === 'fetching'

  const surf = (e: { message?: string | null } | null | undefined) => {
    if (e?.message) setError(e.message)
  }

  async function finish() {
    await signUp?.finalize({
      navigate: ({ decorateUrl }) => {
        const url = decorateUrl('/')
        if (url.startsWith('http')) window.location.href = url
        else navigate(url)
      },
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
      surf(err)
      if (err) return
      if (
        signUp!.status === 'missing_requirements' &&
        (signUp!.unverifiedFields ?? []).includes('email_address')
      ) {
        const send = await signUp!.verifications.sendEmailCode()
        surf(send.error)
        if (send.error) return
        setVerifying(true)
        return
      }
      if (signUp!.status === 'complete') await finish()
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
      surf(err)
      if (!err && signUp!.status === 'complete') await finish()
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

  const fieldErr = (errors?.fields as
    | { emailAddress?: { message?: string }; password?: { message?: string }; code?: { message?: string } }
    | undefined)
  const shownError =
    error ??
    (errors?.global as { message?: string }[] | null)?.[0]?.message ??
    (verifying
      ? fieldErr?.code?.message
      : fieldErr?.emailAddress?.message ?? fieldErr?.password?.message)

  return (
    <div className="flex min-h-screen items-center justify-center bg-[#faf7f6] px-4 py-10">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex flex-col items-center">
          <div className="mb-2 flex size-11 items-center justify-center rounded-full bg-[#7a303f]/10 text-[#7a303f]">
            <Blossom className="size-6" />
          </div>
          <h1 className="font-serif text-xl font-medium text-neutral-900">Aveline</h1>
          <p className="mt-1 text-[11px] uppercase tracking-[0.28em] text-neutral-500">
            Administrator sign-up
          </p>
        </div>

        <div className="rounded-2xl border border-neutral-200 bg-white p-6 shadow-sm">
          <p className="mb-4 text-center text-[13px] leading-relaxed text-neutral-500">
            Administrator access is restricted and granted after review by the
            Aveline team.
          </p>

          {shownError && (
            <div className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-[13px] text-red-700">
              {shownError}
            </div>
          )}

          {!isLoaded || !signUp ? (
            <p className="py-8 text-center text-sm text-neutral-400">
              Preparing…
            </p>
          ) : !verifying ? (
            <form onSubmit={create} className="flex flex-col gap-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="admin-first" className="text-neutral-600">
                    First name
                  </Label>
                  <Input
                    id="admin-first"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    className="h-10 rounded-full bg-neutral-50"
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="admin-last" className="text-neutral-600">
                    Last name
                  </Label>
                  <Input
                    id="admin-last"
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    className="h-10 rounded-full bg-neutral-50"
                  />
                </div>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="admin-email" className="text-neutral-600">
                  Work email
                </Label>
                <Input
                  id="admin-email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@aveline.lk"
                  className="h-10 rounded-full bg-neutral-50"
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="admin-password" className="text-neutral-600">
                  Password
                </Label>
                <Input
                  id="admin-password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="h-10 rounded-full bg-neutral-50"
                />
              </div>

              <div id="clerk-captcha" />

              <Button
                type="submit"
                disabled={busyState || !email || !password}
                className="mt-1 h-10 w-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
              >
                {busyState ? 'Submitting…' : 'Request administrator access'}
              </Button>
            </form>
          ) : (
            <form onSubmit={verify} className="flex flex-col gap-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="admin-code" className="text-neutral-600">
                  Verification code
                </Label>
                <Input
                  id="admin-code"
                  inputMode="numeric"
                  value={code}
                  onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                  placeholder="••••••"
                  className="h-10 rounded-full bg-neutral-50 text-center font-mono tracking-[0.35em]"
                />
                <p className="text-xs text-neutral-400">
                  We emailed a code to {email}.
                </p>
              </div>
              <Button
                type="submit"
                disabled={busyState || !code}
                className="mt-1 h-10 w-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
              >
                {busyState ? 'Verifying…' : 'Verify email'}
              </Button>
              <button
                type="button"
                onClick={resend}
                disabled={busyState}
                className="text-center text-xs text-neutral-500 hover:text-neutral-700"
              >
                Resend code
              </button>
            </form>
          )}
        </div>

        <p className="mt-5 text-center text-sm text-neutral-500">
                <Link to="/sign-in" className="text-neutral-600 hover:underline">
                  Back to sign in
                </Link>
        </p>
      </div>
    </div>
  )
}
