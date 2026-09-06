import { useUser } from '@clerk/react'
import { AlertCircle } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
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
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useUserContext } from '@/contexts/UserContext'
import type { ContactPreferences } from '@/types/user'

export function OnboardingPage() {
  const { user: clerkUser, isLoaded } = useUser()
  const { completeOnboarding, refreshUser } = useUserContext()
  const navigate = useNavigate()

  const defaultDisplayName = clerkUser?.fullName ?? clerkUser?.firstName ?? ''
  const defaultEmail = clerkUser?.primaryEmailAddress?.emailAddress ?? ''

  const [displayName, setDisplayName] = useState(defaultDisplayName)
  const [phoneNumber, setPhoneNumber] = useState('+94 ')
  const [address, setAddress] = useState('')
  const [contactPreference, setContactPreference] = useState<ContactPreferences>('WhatsApp')
  const [pushNotificationsEnabled, setPushNotificationsEnabled] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)

    if (!displayName.trim()) {
      setFormError('Please provide your full display name.')
      return
    }

    if (!phoneNumber.trim() || phoneNumber.trim() === '+94') {
      setFormError('Please enter a valid contact phone number.')
      return
    }

    if (!address.trim()) {
      setFormError('Please provide your boutique or delivery address.')
      return
    }

    try {
      setIsSubmitting(true)
      await completeOnboarding({
        displayName: displayName.trim(),
        phoneNumber: phoneNumber.trim(),
        address: address.trim(),
        profileImageUrl: clerkUser?.imageUrl,
        contactPreference,
        pushNotificationsEnabled,
      })
      const me = await refreshUser()
      navigate(me?.accountState === 'Active' ? '/app' : '/org-setup', { replace: true })
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unable to complete onboarding. Please try again.'
      setFormError(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="min-h-screen bg-background flex flex-col justify-center items-center py-12 px-4 sm:px-6 lg:px-8">
      {/* Background decorative gradient orbs */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none -z-10">
        <div className="absolute -top-40 -right-40 w-96 h-96 rounded-full bg-secondary opacity-40 blur-3xl" />
        <div className="absolute -bottom-40 -left-40 w-96 h-96 rounded-full bg-muted opacity-30 blur-3xl" />
      </div>

      <div className="max-w-xl w-full flex flex-col gap-6">
        {/* Header branding */}
        <div className="text-center flex flex-col gap-2">
          <span className="text-xs uppercase tracking-[0.25em] font-semibold text-muted-foreground">
            Aveline Assistant
          </span>
          <h1 className="text-3xl sm:text-4xl font-serif font-medium text-foreground tracking-tight">
            Complete Your Profile
          </h1>
          <p className="text-sm text-muted-foreground max-w-md mx-auto">
            Welcome to the curated concierge ecosystem. Confirm your boutique details to personalize client interactions.
          </p>
        </div>

        <Card className="shadow-lg">
          <CardHeader>
            {/* Clerk profile preview */}
            <div className="flex items-center gap-4 p-4 rounded-xl bg-muted/50 border border-border mb-2">
              {!isLoaded ? (
                <>
                  <Skeleton className="size-12 rounded-full" />
                  <div className="flex flex-col gap-2">
                    <Skeleton className="h-3 w-20" />
                    <Skeleton className="h-4 w-40" />
                  </div>
                </>
              ) : (
                <>
                  <Avatar className="size-12 ring-2 ring-primary/20">
                    <AvatarImage src={clerkUser?.imageUrl} alt={displayName || 'Profile'} />
                    <AvatarFallback className="bg-primary text-primary-foreground font-serif text-lg font-bold">
                      {displayName.charAt(0).toUpperCase() || 'A'}
                    </AvatarFallback>
                  </Avatar>
                  <div className="flex-1 min-w-0">
                    <p className="text-xs font-medium text-muted-foreground uppercase tracking-wider">Account Email</p>
                    <p className="text-sm font-medium truncate">{defaultEmail || 'Authenticated Clerk User'}</p>
                  </div>
                </>
              )}
            </div>
            {/* Visually hidden — required for accessibility */}
            <CardTitle className="sr-only">Profile Details</CardTitle>
            <CardDescription className="sr-only">
              Fill in your boutique details to complete onboarding.
            </CardDescription>
          </CardHeader>

          <CardContent>
            {formError && (
              <Alert variant="destructive" className="mb-6">
                <AlertCircle className="size-4" />
                <AlertDescription>{formError}</AlertDescription>
              </Alert>
            )}

            <form onSubmit={handleSubmit} className="flex flex-col gap-6">
              {/* Display Name */}
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="displayName" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Full Display Name <span className="text-destructive" aria-hidden>*</span>
                </Label>
                <Input
                  id="displayName"
                  type="text"
                  required
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  placeholder="e.g. Kasun Delpachithra"
                />
              </div>

              {/* Phone Number */}
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="phoneNumber" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Contact Phone / WhatsApp <span className="text-destructive" aria-hidden>*</span>
                </Label>
                <Input
                  id="phoneNumber"
                  type="tel"
                  required
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                  placeholder="+94 77 123 4567"
                />
                <p className="text-xs text-muted-foreground">
                  Include Sri Lankan country code (+94) for WhatsApp concierge notifications.
                </p>
              </div>

              {/* Address */}
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="address" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Boutique / Delivery Address <span className="text-destructive" aria-hidden>*</span>
                </Label>
                <Textarea
                  id="address"
                  rows={2}
                  required
                  value={address}
                  onChange={(e) => setAddress(e.target.value)}
                  placeholder="e.g. 15 Alfred House Gardens, Colombo 03"
                  className="resize-none"
                />
              </div>

              {/* Preferred Communication Channel */}
              <div className="flex flex-col gap-2">
                <Label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Preferred Concierge Channel
                </Label>
                <ToggleGroup
                  type="single"
                  value={contactPreference}
                  onValueChange={(val) => {
                    if (val) setContactPreference(val as ContactPreferences)
                  }}
                  className="flex flex-wrap gap-2 justify-start"
                >
                  {(['WhatsApp', 'SMS', 'Email', 'Phone'] as ContactPreferences[]).map((pref) => (
                    <ToggleGroupItem key={pref} value={pref} variant="outline" size="sm">
                      {pref}
                    </ToggleGroupItem>
                  ))}
                </ToggleGroup>
              </div>

              {/* Push Notifications Toggle */}
              <div className="flex items-center justify-between gap-4 py-2">
                <div className="flex flex-col gap-1">
                  <Label htmlFor="pushNotifications" className="text-sm font-medium">
                    Enable Order &amp; Sourcing Alerts
                  </Label>
                  <p className="text-xs text-muted-foreground">
                    Receive instant concierge updates when client orders or sourcing requests update.
                  </p>
                </div>
                <Switch
                  id="pushNotifications"
                  checked={pushNotificationsEnabled}
                  onCheckedChange={setPushNotificationsEnabled}
                />
              </div>

              {/* Submit CTA */}
              <Button type="submit" disabled={isSubmitting} className="w-full" size="lg">
                {isSubmitting ? (
                  <>
                    <svg
                      className="animate-spin size-4"
                      data-icon="inline-start"
                      fill="none"
                      viewBox="0 0 24 24"
                      aria-hidden
                    >
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                    </svg>
                    Saving Profile…
                  </>
                ) : (
                  'Complete Setup & Enter Dashboard'
                )}
              </Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
