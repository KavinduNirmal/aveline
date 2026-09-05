import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth, useSignUp } from '@clerk/react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { AuthError, OrDivider, SocialButtons } from './AuthBits'

const SSO_REDIRECT = () => `${window.location.origin}/sso-callback`

type Step = 'details' | 'verify'

export function SignUpForm({
  accountType,
  onResetAccountType,
}: {
  accountType: 'owner' | 'staff' | 'admin' | undefined
  onResetAccountType?: () => void
}) {
  const { isLoaded } = useAuth()
  const { signUp, errors, fetchStatus } = useSignUp()
  const navigate = useNavigate()

  const [step, setStep] = useState<Step>('details')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)

  const busyState = busy || fetchStatus === 'fetching'
  const status = signUp?.status ?? null
  const needsEmailVerify =
    step === 'verify' ||
    (status === 'missing_requirements' &&
      (signUp?.unverifiedFields ?? []).includes('email_address') &&
      (signUp?.missingFields ?? []).length === 0)

  async function finalize() {
    await signUp?.finalize({
      navigate: ({ decorateUrl }) => {
        const url = decorateUrl('/')
        if (url.startsWith('http')) {
          window.location.href = url
        } else {
          navigate(url)
        }
      },
    })
  }

  function surfaceError(e: { message?: string | null } | null | undefined) {
    if (e?.message) setLocalError(e.message)
  }

  const handleDetails = async (e: FormEvent) => {
    e.preventDefault()
    if (!email || !password) return
    setLocalError(null)
    setBusy(true)
    try {
      const { error } = await signUp!.password({
        emailAddress: email,
        password,
        firstName: firstName || undefined,
        lastName: lastName || undefined,
        username: username || undefined,
        unsafeMetadata: accountType ? { accountType } : undefined,
      })
      surfaceError(error)

      if (!error) {
        if (
          signUp!.status === 'missing_requirements' &&
          (signUp!.unverifiedFields ?? []).includes('email_address')
        ) {
          const send = await signUp!.verifications.sendEmailCode()
          surfaceError(send.error)
          if (!send.error) setStep('verify')
          return
        }
        if (signUp!.status === 'complete') {
          await finalize()
        }
      }
    } finally {
      setBusy(false)
    }
  }

  const handleVerify = async (e: FormEvent) => {
    e.preventDefault()
    if (!code) return
    setLocalError(null)
    setBusy(true)
    try {
      const { error } = await signUp!.verifications.verifyEmailCode({ code })
      surfaceError(error)
      if (!error && signUp!.status === 'complete') {
        await finalize()
      }
    } finally {
      setBusy(false)
    }
  }

  const resendCode = async () => {
    setLocalError(null)
    setBusy(true)
    try {
      surfaceError((await signUp!.verifications.sendEmailCode()).error)
    } finally {
      setBusy(false)
    }
  }

  const handleSocial = async (strategy: 'oauth_google' | 'oauth_facebook') => {
    setLocalError(null)
    setBusy(true)
    try {
      const { error } = await signUp!.sso({
        strategy,
        redirectUrl: SSO_REDIRECT(),
        redirectCallbackUrl: SSO_REDIRECT(),
      })
      surfaceError(error)
    } finally {
      setBusy(false)
    }
  }

  const fieldErrors = errors?.fields as
    | {
        firstName?: { message?: string }
        lastName?: { message?: string }
        username?: { message?: string }
        emailAddress?: { message?: string }
        password?: { message?: string }
        code?: { message?: string }
      }
    | undefined

  const firstError =
    Object.values(fieldErrors ?? {})
      .map((f) => f?.message)
      .find(Boolean) ?? undefined
  const globalError =
    localError ??
    (errors?.global as { message?: string }[] | null)?.[0]?.message ??
    firstError

  if (!isLoaded || !signUp) {
    return (
      <p className="py-10 text-center text-sm text-white/40">
        Preparing your secure sign-up…
      </p>
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <AuthError message={globalError} />

      {!needsEmailVerify ? (
        <form onSubmit={handleDetails} className="flex flex-col gap-3">
          {accountType && onResetAccountType && (
            <div className="flex items-center justify-between gap-3 rounded-full border border-white/10 bg-white/5 py-2 pl-4 pr-2 text-[13px] text-white/70">
              <span>
                Setting up as{' '}
                <span className="font-semibold text-white">
                  {accountType === 'owner' ? 'boutique owner' : `${accountType} member`}
                </span>
              </span>
              <button
                type="button"
                onClick={onResetAccountType}
                className="rounded-full bg-white/10 px-3 py-1 font-medium text-rose-100 transition-colors hover:bg-white/20"
              >
                Change
              </button>
            </div>
          )}
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label htmlFor="firstName" className="text-white/70">
                First name
              </Label>
              <Input
                id="firstName"
                autoComplete="given-name"
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                placeholder="Kasun"
                className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-[15px] text-white placeholder:text-white/40"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="lastName" className="text-white/70">
                Last name
              </Label>
              <Input
                id="lastName"
                autoComplete="family-name"
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                placeholder="Delpachithra"
                className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-[15px] text-white placeholder:text-white/40"
              />
            </div>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="username" className="text-white/70">
              Username
            </Label>
            <Input
              id="username"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              value={username}
              onChange={(e) => setUsername(e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, ''))}
              placeholder="kasun_d"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-[15px] text-white placeholder:text-white/40"
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="email" className="text-white/70">
              Email
            </Label>
            <Input
              id="email"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@example.com"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-[15px] text-white placeholder:text-white/40"
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="password" className="text-white/70">
              Password
            </Label>
            <Input
              id="password"
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="At least 8 characters"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-[15px] text-white placeholder:text-white/40"
            />
          </div>

          {/* Clerk bot sign-up protection mounts here when enabled */}
          <div id="clerk-captcha" />

          <Button
            type="submit"
            disabled={busyState || !email || !password}
            className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-[15px] font-semibold text-white shadow-[0_10px_30px_-8px_rgba(122,48,63,0.7)] transition-all hover:bg-[#8c3b4c] disabled:opacity-60"
          >
            {busyState ? 'Creating account…' : 'Create account'}
          </Button>
        </form>
      ) : (
        <form onSubmit={handleVerify} className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="verify-code" className="text-white/70">
              Verification code
            </Label>
            <Input
              id="verify-code"
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
              placeholder="••••••"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-center font-mono text-lg tracking-[0.35em] text-white placeholder:text-white/40"
            />
            <p className="text-xs text-white/40">
              We sent a one-time code to {email}. Enter it to verify your account.
            </p>
          </div>
          <Button
            type="submit"
            disabled={busyState || !code}
            className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
          >
            {busyState ? 'Verifying…' : 'Verify & continue'}
          </Button>
          <button
            type="button"
            onClick={resendCode}
            disabled={busyState}
            className="text-center text-xs text-white/50 hover:text-white/80"
          >
            Resend code
          </button>
        </form>
      )}

      {!needsEmailVerify && (
        <>
          <OrDivider />
          <SocialButtons
            onGoogle={() => void handleSocial('oauth_google')}
            onFacebook={() => void handleSocial('oauth_facebook')}
            busy={busyState}
          />
        </>
      )}
    </div>
  )
}
