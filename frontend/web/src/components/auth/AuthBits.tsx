import { type ReactNode } from 'react'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/** Row of brand SSO buttons (Google / Facebook). */
export function SocialButtons({
  onGoogle,
  onFacebook,
  busy,
  className,
}: {
  onGoogle: () => void
  onFacebook: () => void
  busy: boolean
  className?: string
}) {
  return (
    <div className={cn('grid grid-cols-2 gap-3', className)}>
      <Button
        type="button"
        variant="outline"
        size="lg"
        className="h-11 border-white/10 bg-white/5 text-[#f3e8ea] backdrop-blur hover:bg-white/10 hover:text-white"
        disabled={busy}
        onClick={onGoogle}
      >
        <svg viewBox="0 0 24 24" className="size-4" aria-hidden>
          <path fill="#EA4335" d="M12 5.4c1.5 0 2.9.5 4 1.5l3-3C17.1 1.8 14.7 1 12 1 7.7 1 4 3.5 2.2 7.2l3.5 2.7C6.7 7.2 9.2 5.4 12 5.4Z" />
          <path fill="#4285F4" d="M21.6 12.2c0-.8-.1-1.5-.2-2.2H12v4.4h5.4c-.2 1.2-.9 2.2-1.9 2.9l3.4 2.6c2-1.9 3.1-4.7 3.1-7.7Z" />
          <path fill="#FBBC05" d="M5.7 14.3c-.3-.9-.4-1.9-.4-2.3s.2-1.4.4-2.3L2.2 7.2C1.4 8.7 1 10.3 1 12s.4 3.3 1.2 4.8l3.5-2.5Z" />
          <path fill="#34A853" d="M12 23c2.7 0 5.1-.9 6.9-2.4l-3.4-2.6c-.9.6-2.1 1-3.5 1-2.8 0-5.3-1.8-6.1-4.3l-3.5 2.5C4 19.9 7.7 23 12 23Z" />
        </svg>
        Google
      </Button>
      <Button
        type="button"
        variant="outline"
        size="lg"
        className="h-11 border-white/10 bg-white/5 text-[#f3e8ea] backdrop-blur hover:bg-white/10 hover:text-white"
        disabled={busy}
        onClick={onFacebook}
      >
        <svg viewBox="0 0 24 24" className="size-4" aria-hidden>
          <path fill="#1877F2" d="M24 12a12 12 0 1 0-13.9 11.9v-8.4h-3V12h3V9.4c0-3 1.8-4.7 4.6-4.7 1.3 0 2.7.2 2.7.2v3h-1.5c-1.5 0-2 .9-2 1.9V12h3.3l-.5 3.5h-2.8v8.4A12 12 0 0 0 24 12Z" />
        </svg>
        Facebook
      </Button>
    </div>
  )
}

/** Divider used between SSO and password forms. */
export function OrDivider({ label = 'or continue with' }: { label?: string }) {
  return (
    <div className="flex items-center gap-3 text-xs uppercase tracking-[0.2em] text-white/35">
      <span className="h-px flex-1 bg-white/10" />
      <span>{label}</span>
      <span className="h-px flex-1 bg-white/10" />
    </div>
  )
}

/** Renders one or more server-side field/global errors. */
export function AuthError({
  message,
  children,
}: {
  message?: string
  children?: ReactNode
}) {
  if (!message && !children) return null
  return (
    <Alert variant="destructive" className="border-red-400/20 bg-red-500/10 text-red-100">
      <AlertDescription className="flex flex-col gap-1 text-sm">
        {message ? <span>{message}</span> : null}
        {children}
      </AlertDescription>
    </Alert>
  )
}
