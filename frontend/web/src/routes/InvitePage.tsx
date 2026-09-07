import { useUser } from '@clerk/react'
import { CheckCircle2, Loader2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { AuroraField } from '@/components/site/AuroraField'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { useUserContext } from '@/contexts/UserContext'
import { acceptInvitation } from '@/lib/organizations'

export function InvitePage() {
  const { isLoaded: clerkLoaded } = useUser()
  const { refreshUser } = useUserContext()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [status, setStatus] = useState<'idle' | 'working' | 'error'>('idle')
  const [error, setError] = useState<string | null>(null)

  const code = params.get('code')

  useEffect(() => {
    if (!clerkLoaded || !code || status !== 'idle') return
    let mounted = true
    async function run() {
      if (!code) {
        setStatus('error')
        setError('This invitation link is missing its code.')
        return
      }
      setStatus('working')
      try {
        await acceptInvitation(code)
        await refreshUser()
        if (mounted) navigate('/app', { replace: true })
      } catch (err: unknown) {
        if (mounted) {
          setError(
            err instanceof Error
              ? err.message
              : 'Unable to accept this invitation. The code may be invalid or expired.',
          )
          setStatus('error')
        }
      }
    }
    void run()
    return () => {
      mounted = false
    }
  }, [clerkLoaded, code, status, navigate, refreshUser])

  const ready = clerkLoaded && code !== null

  return (
    <div className="relative min-h-screen bg-background overflow-hidden flex items-center justify-center px-4">
      <AuroraField />
      <div className="relative z-10 flex flex-col items-center gap-4 text-center max-w-md w-full">
        {!ready ? (
          <p className="text-sm text-muted-foreground">
            This invitation link is missing its code. Please use the full link your boutique owner shared.
          </p>
        ) : status === 'error' ? (
          <div className="flex flex-col gap-4 w-full">
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
            <div className="flex justify-center gap-2">
              <Button variant="outline" onClick={() => navigate('/onboarding')}>
                Go to onboarding
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-col items-center gap-3">
            <Loader2 className="size-8 animate-spin text-primary" aria-hidden />
            <p className="text-sm text-muted-foreground">Joining your boutique…</p>
          </div>
        )}

        {status === 'idle' && ready && (
          <div className="flex items-center gap-2 text-xs text-emerald-700 dark:text-emerald-300">
            <CheckCircle2 className="size-4" aria-hidden /> Invitation ready to accept
          </div>
        )}
      </div>
    </div>
  )
}
