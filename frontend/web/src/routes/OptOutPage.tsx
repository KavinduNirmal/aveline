import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AlertCircle, CheckCircle2, Lock, MessageCircle, ShieldOff } from 'lucide-react'

import { SitePage } from '@/components/site/SitePage'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import {
  describeCodeLifetime,
  describeOptOutError,
  parsePrivacyLink,
  startOptOut,
  verifyOptOut,
  type OptOutScope,
} from '@/lib/privacy'

type Stage = 'form' | 'code' | 'revoked' | 'failed-start'

/**
 * The customer-facing opt-out page at `/privacy/opt-out` (privacy plan §5.2, Phase 7 item 7.5).
 *
 * It is the only surface in the product a customer - who has no Aveline account - uses directly,
 * and it carries a legal obligation, so the flow is deliberately plain: prove the number with a
 * one-time code, choose how far the opt-out reaches, and stop. Every control is a labelled,
 * keyboard-reachable native or shadcn primitive, and every state is stated in words, not by colour.
 *
 * **Anti-enumeration.** `start` answers the same accepted body whether or not the number is known,
 * so this page never branches on whether a customer exists: after a successful start it always
 * shows the same neutral "if that number is registered" copy. The only failure it surfaces is a
 * genuine refusal (bad number, rate limit, outage) or a malformed response.
 */
export function OptOutPage() {
  const [searchParams] = useSearchParams()
  const link = useMemo(() => parsePrivacyLink(searchParams.toString()), [searchParams])

  const [stage, setStage] = useState<Stage>('form')
  const [phone, setPhone] = useState('')
  const [scope, setScope] = useState<OptOutScope>('org')
  const [otp, setOtp] = useState('')
  const [handle, setHandle] = useState<string | null>(null)
  const [expiresInSeconds, setExpiresInSeconds] = useState<number | null>(null)
  const [revokedScope, setRevokedScope] = useState<OptOutScope>('org')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (link === null) {
    return (
      <OptOutShell>
        <h1 className="font-serif text-3xl font-medium text-neutral-900 sm:text-4xl">
          This opt-out link is not valid
        </h1>
        <p className="mt-4 text-sm leading-relaxed text-neutral-600">
          The link is missing the values that prove which boutique it came from. Open the link from
          the WhatsApp message the boutique sent you, or contact the boutique directly and ask it to
          stop processing your messages.
        </p>
        <p className="mt-4 text-sm leading-relaxed text-neutral-600">
          Nothing was changed by opening this page.
        </p>
        <p className="mt-6 text-sm">
          <Link to="/privacy" className="text-primary hover:underline">
            Read the Data Policy
          </Link>
        </p>
      </OptOutShell>
    )
  }

  const reset = () => {
    setStage('form')
    setOtp('')
    setHandle(null)
    setExpiresInSeconds(null)
    setError(null)
  }

  const onStart = async (event: FormEvent) => {
    event.preventDefault()
    const trimmed = phone.trim()
    if (trimmed === '') {
      setError('Enter your WhatsApp number, including the area code, for example 0771234567.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const result = await startOptOut(link, trimmed, scope)
      if (result.handle === null) {
        // The endpoint mints the handle unconditionally, so a missing one is a regression. Fail
        // visibly rather than collect a code the page could never redeem.
        setStage('failed-start')
        return
      }
      setHandle(result.handle)
      setExpiresInSeconds(result.expiresInSeconds)
      setStage('code')
    } catch (thrown) {
      setError(describeOptOutError(thrown))
    } finally {
      setBusy(false)
    }
  }

  const onVerify = async (event: FormEvent) => {
    event.preventDefault()
    const code = otp.trim()
    if (!/^\d{6}$/.test(code)) {
      setError('Enter the six-digit code from the WhatsApp message.')
      return
    }
    if (handle === null) {
      setStage('failed-start')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const result = await verifyOptOut({
        link,
        handle,
        phoneNumber: phone.trim(),
        otp: code,
        scope,
      })
      setRevokedScope(result.scope === 'all' ? 'all' : 'org')
      setStage('revoked')
    } catch (thrown) {
      setError(describeOptOutError(thrown))
    } finally {
      setBusy(false)
    }
  }

  if (stage === 'revoked') {
    return (
      <OptOutShell>
        <span className="flex size-12 items-center justify-center rounded-full bg-emerald-50 text-emerald-700">
          <CheckCircle2 className="size-6" aria-hidden="true" />
        </span>
        <h1 className="mt-5 font-serif text-3xl font-medium text-neutral-900 sm:text-4xl">
          You have opted out
        </h1>
        <p className="mt-4 text-sm leading-relaxed text-neutral-700">
          {revokedScope === 'all'
            ? 'Every Aveline boutique holding your number will not process your messages or use what it remembered.'
            : 'This boutique will not process your messages or use what it remembered.'}
        </p>
        <p className="mt-3 text-sm leading-relaxed text-neutral-600">
          We will send one confirmation to your WhatsApp number. You can still contact the boutique
          directly, and you can change this choice later.
        </p>
        <p className="mt-6 text-sm">
          <Link to="/privacy/consent-flow" className="text-primary hover:underline">
            See what each consent state means
          </Link>
        </p>
      </OptOutShell>
    )
  }

  if (stage === 'failed-start') {
    return (
      <OptOutShell>
        <span className="flex size-12 items-center justify-center rounded-full bg-amber-50 text-amber-700">
          <AlertCircle className="size-6" aria-hidden="true" />
        </span>
        <h1 className="mt-5 font-serif text-3xl font-medium text-neutral-900 sm:text-4xl">
          We could not start the opt-out
        </h1>
        <p className="mt-4 text-sm leading-relaxed text-neutral-700">
          Nothing was changed. Check your connection and try again, or contact the boutique that
          messaged you and ask it to stop processing your messages.
        </p>
        <div className="mt-6 flex flex-col gap-3 sm:flex-row">
          <Button type="button" onClick={reset}>
            Try again
          </Button>
          <Button asChild variant="outline">
            <Link to="/privacy">Read the Data Policy</Link>
          </Button>
        </div>
      </OptOutShell>
    )
  }

  if (stage === 'code') {
    return (
      <OptOutShell>
        <span className="flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
          <MessageCircle className="size-6" aria-hidden="true" />
        </span>
        <h1 className="mt-5 font-serif text-3xl font-medium text-neutral-900 sm:text-4xl">
          Enter the code we sent you
        </h1>
        {/* One neutral sentence for every start outcome, because start cannot reveal whether the
            number is known. Never branch this copy on existence. */}
        <p className="mt-4 text-sm leading-relaxed text-neutral-700">
          If that number is registered with a boutique on Aveline, a six-digit code is on its way by
          WhatsApp. {describeCodeLifetime(expiresInSeconds)}
        </p>

        {error ? <ErrorAlert message={error} /> : null}

        <form className="mt-6 flex flex-col gap-5" onSubmit={onVerify} noValidate>
          <div className="flex flex-col gap-2">
            <Label htmlFor="opt-out-code">Six-digit code</Label>
            <Input
              id="opt-out-code"
              name="otp"
              value={otp}
              onChange={(event) => setOtp(event.target.value.replace(/\D/g, '').slice(0, 6))}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              aria-describedby="opt-out-code-hint"
              className="max-w-48 text-lg tracking-[0.4em]"
              disabled={busy}
            />
            <p id="opt-out-code-hint" className="text-xs text-neutral-500">
              Six digits, from the WhatsApp message. It can be used once.
            </p>
          </div>

          <div className="flex flex-col gap-2 sm:flex-row">
            <Button type="submit" disabled={busy}>
              {busy ? 'Checking…' : 'Confirm opt-out'}
            </Button>
            <Button type="button" variant="ghost" onClick={reset} disabled={busy}>
              Start over
            </Button>
          </div>
        </form>
      </OptOutShell>
    )
  }

  return (
    <OptOutShell>
      <span className="flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
        <ShieldOff className="size-6" aria-hidden="true" />
      </span>
      <h1 className="mt-5 font-serif text-3xl font-medium text-neutral-900 sm:text-4xl">
        Opt out of messages
      </h1>
      <p className="mt-4 text-sm leading-relaxed text-neutral-700">
        Tell us the number the boutique messaged and we will send it a one-time code. You do not
        need an account. The link you opened proves which boutique this is; the code proves the
        number.
      </p>

      {error ? <ErrorAlert message={error} /> : null}

      <form className="mt-6 flex flex-col gap-5" onSubmit={onStart} noValidate>
        <div className="flex flex-col gap-2">
          <Label htmlFor="opt-out-phone">Your WhatsApp number</Label>
          <Input
            id="opt-out-phone"
            name="phone"
            type="tel"
            value={phone}
            onChange={(event) => setPhone(event.target.value)}
            autoComplete="tel"
            inputMode="tel"
            placeholder="0771234567"
            aria-describedby="opt-out-phone-hint"
            className="max-w-64"
            disabled={busy}
          />
          <p id="opt-out-phone-hint" className="text-xs text-neutral-500">
            The number that received the boutique&rsquo;s message, for example 0771234567.
          </p>
        </div>

        <fieldset className="flex flex-col gap-3 rounded-2xl border-2 border-dashed border-neutral-200 bg-white p-4">
          <legend className="px-1 text-sm font-medium text-neutral-900">
            What should we stop?
          </legend>
          <ToggleGroup
            type="single"
            value={scope}
            onValueChange={(next) => {
              if (next === 'org' || next === 'all') setScope(next)
            }}
            variant="outline"
            aria-label="What should we stop?"
            className="w-full flex-col items-stretch sm:w-auto sm:flex-row sm:items-center"
          >
            <ToggleGroupItem value="org" className="justify-start sm:justify-center">
              This boutique only
            </ToggleGroupItem>
            <ToggleGroupItem value="all" className="justify-start sm:justify-center">
              Every Aveline boutique
            </ToggleGroupItem>
          </ToggleGroup>
          <p className="text-xs text-neutral-500">
            {scope === 'all'
              ? 'Applies to every Aveline boutique that holds this number.'
              : 'Applies only to the boutique that sent you this link.'}
          </p>
        </fieldset>

        <div className="flex flex-col gap-3 sm:flex-row">
          <Button type="submit" disabled={busy}>
            {busy ? 'Sending…' : 'Send me a code'}
          </Button>
          <Button asChild variant="outline">
            <Link to="/privacy">Read the Data Policy</Link>
          </Button>
        </div>
      </form>

      <p className="mt-6 flex items-start gap-2 text-xs leading-relaxed text-neutral-500">
        <Lock className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
        We never ask for a password. The code proves the number, and the opt-out is recorded whether
        or not the number is one we already know.
      </p>
    </OptOutShell>
  )
}

function OptOutShell({ children }: { children: ReactNode }) {
  return (
    <SitePage>
      <main className="mx-auto w-full max-w-2xl px-5 py-16 lg:px-8">
        <div className="rounded-3xl border-2 border-dashed border-neutral-200 bg-white p-6 sm:p-10">
          {children}
        </div>
      </main>
    </SitePage>
  )
}

function ErrorAlert({ message }: { message: string }) {
  return (
    <Alert variant="destructive" className="mt-6">
      <AlertCircle className="size-4" aria-hidden="true" />
      <AlertTitle>We could not continue</AlertTitle>
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}
