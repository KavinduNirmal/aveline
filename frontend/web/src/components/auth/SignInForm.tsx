import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth, useSignIn } from '@clerk/react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { AuthError, OrDivider, SocialButtons } from './AuthBits'

const SSO_REDIRECT = () =>
  `${window.location.origin}/sso-callback`

type View = 'credentials' | 'mfa' | 'forgot-request' | 'forgot-verify'

type Factor = 'totp' | 'email_code' | 'phone_code' | 'backup_code'

export function SignInForm() {
  const { isLoaded } = useAuth()
  const { signIn, errors, fetchStatus } = useSignIn()
  const navigate = useNavigate()

  const [view, setView] = useState<View>('credentials')
  const [identifier, setIdentifier] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)

  const busyState = busy || fetchStatus === 'fetching'

  const status = signIn?.status ?? null
  const supported = (signIn?.supportedSecondFactors ?? []) as { strategy?: string }[]

  useEffect(() => {
    if (status === 'needs_second_factor' || status === 'needs_client_trust') {
      setView('mfa')
    } else if (status === 'needs_new_password') {
      setView('forgot-verify')
    }
  }, [status])

  async function finalize() {
    await signIn?.finalize({
      navigate: ({ decorateUrl }) => {
        const url = decorateUrl('/app')
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

  function pickFactor(): Factor {
    const strat = supported.find((s) => s.strategy && s.strategy !== 'password')
    const value = (strat?.strategy ?? 'totp') as Factor
    return value
  }

  const handlePassword = async (e: FormEvent) => {
    e.preventDefault()
    if (!identifier || !password) return
    setLocalError(null)
    setBusy(true)
    try {
      const { error } = await signIn!.password({ identifier, password })
      surfaceError(error)
      if (!error && signIn!.status === 'complete') {
        await finalize()
      }
    } finally {
      setBusy(false)
    }
  }

  const handleSocial = async (strategy: 'oauth_google' | 'oauth_facebook') => {
    setLocalError(null)
    setBusy(true)
    try {
      const { error } = await signIn!.sso({
        strategy,
        redirectUrl: SSO_REDIRECT(),
        redirectCallbackUrl: SSO_REDIRECT(),
      })
      surfaceError(error)
    } finally {
      setBusy(false)
    }
  }

  const beginForgot = async (e: FormEvent) => {
    e.preventDefault()
    if (!identifier) {
      setLocalError('Enter your email or username to reset your password.')
      return
    }
    setLocalError(null)
    setBusy(true)
    try {
      const { error } = await signIn!.create({ identifier })
      surfaceError(error)
      if (!error) {
        const send = await signIn!.resetPasswordEmailCode.sendCode()
        surfaceError(send.error)
        if (!send.error) setView('forgot-verify')
      }
    } finally {
      setBusy(false)
    }
  }

  const verifyResetCode = async (e: FormEvent) => {
    e.preventDefault()
    if (!code) return
    setLocalError(null)
    setBusy(true)
    try {
      if (signIn!.status === 'needs_new_password' && newPassword) {
        const submit = await signIn!.resetPasswordEmailCode.submitPassword({ password: newPassword })
        surfaceError(submit.error)
        if (submit.error) return
      } else {
        const verify = await signIn!.resetPasswordEmailCode.verifyCode({ code })
        surfaceError(verify.error)
        if (verify.error) return
      }
      if (signIn!.status === 'complete') {
        await finalize()
      } else if (signIn!.status === 'needs_new_password') {
        setView('forgot-verify')
      }
    } finally {
      setBusy(false)
    }
  }

  const handleMfa = async (e: FormEvent) => {
    e.preventDefault()
    if (!code) return
    setLocalError(null)
    setBusy(true)
    try {
      const factor = pickFactor()
      let error: { message?: string } | null = null
      if (factor === 'totp') {
        error = (await signIn!.mfa.verifyTOTP({ code })).error
      } else if (factor === 'email_code') {
        error = (await signIn!.mfa.verifyEmailCode({ code })).error
      } else if (factor === 'phone_code') {
        error = (await signIn!.mfa.verifyPhoneCode({ code })).error
      } else {
        error = (await signIn!.mfa.verifyBackupCode({ code })).error
      }
      surfaceError(error)
      if (!error && signIn!.status === 'complete') {
        await finalize()
      }
    } finally {
      setBusy(false)
    }
  }

  const resendFactor = async () => {
    const factor = pickFactor()
    setLocalError(null)
    setBusy(true)
    try {
      if (factor === 'email_code') {
        surfaceError((await signIn!.mfa.sendEmailCode()).error)
      } else {
        surfaceError((await signIn!.mfa.sendPhoneCode()).error)
      }
    } finally {
      setBusy(false)
    }
  }

  const isMfa = view === 'mfa'

  const fieldErrors = errors?.fields as
    | { identifier?: { message?: string }; password?: { message?: string }; code?: { message?: string } }
    | undefined
  const globalError =
    localError ??
    (errors?.global as { message?: string }[] | null)?.[0]?.message ??
    (isMfa
      ? fieldErrors?.code?.message
      : (fieldErrors?.identifier?.message ?? fieldErrors?.password?.message)) ??
    undefined

  if (!isLoaded || !signIn) {
    return (
      <p className="py-10 text-center text-sm text-white/40">
        Preparing your secure sign-in…
      </p>
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <AuthError message={globalError} />

      {view === 'credentials' && (
        <form onSubmit={handlePassword} className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="identifier" className="text-white/70">
              Email or username
            </Label>
            <Input
              id="identifier"
              type="text"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              value={identifier}
              onChange={(e) => setIdentifier(e.target.value)}
              placeholder="you@example.com"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-base text-white placeholder:text-white/40"
            />
          </div>
          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between">
              <Label htmlFor="password" className="text-white/70">
                Password
              </Label>
              <button
                type="button"
                onClick={() => setView('forgot-request')}
                className="text-xs font-medium text-rose-200/80 transition-colors hover:text-rose-100"
              >
                Forgot password?
              </button>
            </div>
            <Input
              id="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••••"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-base text-white placeholder:text-white/40"
            />
          </div>
          <Button
            type="submit"
            disabled={busyState || !identifier || !password}
            className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-[15px] font-semibold text-white shadow-[0_10px_30px_-8px_rgba(122,48,63,0.7)] transition-all hover:bg-[#8c3b4c] disabled:opacity-60"
          >
            {busyState ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>
      )}

      {view === 'forgot-request' && (
        <form onSubmit={beginForgot} className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="forgot-identifier" className="text-white/70">
              Email or username
            </Label>
            <Input
              id="forgot-identifier"
              type="text"
              autoComplete="username"
              value={identifier}
              onChange={(e) => setIdentifier(e.target.value)}
              placeholder="you@example.com"
              className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-base text-white placeholder:text-white/40"
            />
          </div>
          <Button
            type="submit"
            disabled={busyState || !identifier}
            className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
          >
            {busyState ? 'Sending…' : 'Send reset code'}
          </Button>
          <button
            type="button"
            onClick={() => setView('credentials')}
            className="text-center text-xs text-white/50 hover:text-white/80"
          >
            Back to sign in
          </button>
        </form>
      )}

      {(view === 'forgot-verify' || isMfa) && (
        <form onSubmit={isMfa ? handleMfa : verifyResetCode} className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="code" className="text-white/70">
              {isMfa ? 'Two-factor code' : 'Verification code'}
            </Label>
            <Input
              id="code"
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
              placeholder="••••••"
              className="h-11 rounded-full border-dashed border-white/25 bg-white/[0.06] px-4 text-center font-mono text-lg tracking-[0.35em] text-white placeholder:text-white/40"
            />
            <p className="text-xs text-white/40">
              {isMfa
                ? `Use the code from your ${
                    pickFactor() === 'totp' ? 'authenticator app' : pickFactor()
                  }.`
                : `We emailed a reset code to ${identifier}.`}
            </p>
          </div>

          {!isMfa && signIn?.status === 'needs_new_password' && (
            <div className="flex flex-col gap-2">
              <Label htmlFor="new-password" className="text-white/70">
                New password
              </Label>
              <Input
                id="new-password"
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                placeholder="••••••••••"
                className="h-11 rounded-full border-white/10 bg-white/5 px-4 text-base text-white placeholder:text-white/40"
              />
            </div>
          )}

          <Button
            type="submit"
            disabled={busyState || !code}
            className="mt-1 h-11 w-full rounded-full bg-[#7a303f] text-white hover:bg-[#8c3b4c]"
          >
            {busyState ? 'Verifying…' : 'Verify'}
          </Button>
          {isMfa && (
            <button
              type="button"
              onClick={resendFactor}
              disabled={busyState}
              className="text-center text-xs text-white/50 hover:text-white/80"
            >
              Resend code
            </button>
          )}
          {!isMfa && (
            <button
              type="button"
              onClick={() => setView('forgot-request')}
              className="text-center text-xs text-white/50 hover:text-white/80"
            >
              Back
            </button>
          )}
        </form>
      )}

      <OrDivider />

      <SocialButtons
        onGoogle={() => void handleSocial('oauth_google')}
        onFacebook={() => void handleSocial('oauth_facebook')}
        busy={busyState}
      />
    </div>
  )
}
