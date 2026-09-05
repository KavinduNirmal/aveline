import { AlertCircle, Building2, KeyRound } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useUserContext } from '@/contexts/UserContext'
import { acceptInvitation, createOrganization } from '@/lib/organizations'

type SetupMode = 'create' | 'join'

/**
 * Second step of onboarding for accounts that have a profile but no organization:
 * an owner creates their boutique, or staff join with an invitation code.
 */
export function OrgSetupPage() {
  const { refreshUser } = useUserContext()
  const navigate = useNavigate()

  const [mode, setMode] = useState<SetupMode>('create')
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)

    if (!name.trim()) {
      setFormError('Please give your boutique a name.')
      return
    }

    try {
      setIsSubmitting(true)
      await createOrganization({ name: name.trim() })
      await refreshUser()
      navigate('/', { replace: true })
    } catch (err: unknown) {
      const msg =
        err instanceof Error
          ? err.message
          : 'Unable to create your boutique. Please try again.'
      setFormError(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleJoin = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)

    const trimmed = code.trim()
    if (!trimmed) {
      setFormError('Please enter the invitation code provided by the boutique owner.')
      return
    }

    try {
      setIsSubmitting(true)
      await acceptInvitation(trimmed)
      await refreshUser()
      navigate('/', { replace: true })
    } catch (err: unknown) {
      const msg =
        err instanceof Error
          ? err.message
          : 'Unable to accept the invitation. Please try again.'
      setFormError(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background px-4 py-12 sm:px-6 lg:px-8">
      <div className="flex w-full max-w-3xl flex-col gap-6">
        <div className="text-center flex flex-col gap-2">
          <span className="text-xs uppercase tracking-[0.25em] font-semibold text-muted-foreground">
            Aveline Boutique Concierge
          </span>
          <h1 className="text-3xl sm:text-4xl font-serif font-medium text-foreground tracking-tight">
            Connect Your Boutique
          </h1>
          <p className="text-sm text-muted-foreground max-w-md mx-auto">
            Your profile is ready. Owners create their boutique; staff join with the
            invitation code their owner shared.
          </p>
        </div>

        {/* Mode switch */}
        <div className="flex justify-center gap-2">
          <Button
            type="button"
            variant={mode === 'create' ? 'default' : 'outline'}
            onClick={() => setMode('create')}
          >
            <Building2 className="size-4" aria-hidden />
            I&apos;m an owner
          </Button>
          <Button
            type="button"
            variant={mode === 'join' ? 'default' : 'outline'}
            onClick={() => setMode('join')}
          >
            <KeyRound className="size-4" aria-hidden />
            I have an invitation code
          </Button>
        </div>

        {formError && (
          <Alert variant="destructive">
            <AlertCircle className="size-4" />
            <AlertDescription>{formError}</AlertDescription>
          </Alert>
        )}

        {mode === 'create' ? (
          <Card>
            <CardHeader>
              <CardTitle className="font-serif text-xl font-medium">
                Create your boutique
              </CardTitle>
              <CardDescription>
                You&apos;ll become the owner. Members join later through invitation codes.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <form onSubmit={handleCreate} className="flex flex-col gap-5">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="orgName" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                    Boutique Name <span className="text-destructive" aria-hidden>*</span>
                  </Label>
                  <Input
                    id="orgName"
                    type="text"
                    required
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. Aveline Boutique Colombo"
                  />
                </div>
                <Button type="submit" disabled={isSubmitting} className="w-full" size="lg">
                  {isSubmitting ? 'Creating boutique…' : 'Create boutique & enter dashboard'}
                </Button>
              </form>
            </CardContent>
          </Card>
        ) : (
          <Card>
            <CardHeader>
              <CardTitle className="font-serif text-xl font-medium">
                Join your boutique
              </CardTitle>
              <CardDescription>
                Enter the invitation code the boutique owner shared with you.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <form onSubmit={handleJoin} className="flex flex-col gap-5">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="inviteCode" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                    Invitation code <span className="text-destructive" aria-hidden>*</span>
                  </Label>
                  <Input
                    id="inviteCode"
                    type="text"
                    required
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    placeholder="e.g. AB-7F3K9"
                    autoCapitalize="characters"
                    className="font-mono uppercase"
                  />
                </div>
                <Button type="submit" disabled={isSubmitting} className="w-full" size="lg">
                  {isSubmitting ? 'Joining boutique…' : 'Join boutique & enter dashboard'}
                </Button>
              </form>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  )
}
