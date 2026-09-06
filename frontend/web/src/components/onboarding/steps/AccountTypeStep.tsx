import { ArrowRight, Store, Users } from 'lucide-react'
import type { FormEvent } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

import { useOwnerOnboardingWizard } from '../wizard-context'

export function AccountTypeStep() {
  const { draft, patch, goTo, isSubmitting, handleStaffJoin } = useOwnerOnboardingWizard()

  const submitStaffJoin = (e: FormEvent) => {
    e.preventDefault()
    void handleStaffJoin()
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <Card
          className={`cursor-pointer transition-all border-2 ${
            draft.accountType === 'owner'
              ? 'border-primary shadow-md bg-primary/5'
              : 'border-border hover:border-primary/50'
          }`}
          onClick={() => patch({ accountType: 'owner' })}
        >
          <CardHeader>
            <div className="size-10 rounded-xl bg-primary/15 text-primary flex items-center justify-center mb-2">
              <Store className="size-5" aria-hidden />
            </div>
            <CardTitle className="font-serif text-xl">I&apos;m a Boutique Owner</CardTitle>
            <CardDescription>
              Create a new organization, define your AI concierge, select your plan, and invite staff.
            </CardDescription>
          </CardHeader>
          <CardFooter>
            <Badge variant={draft.accountType === 'owner' ? 'default' : 'outline'}>
              {draft.accountType === 'owner' ? 'Selected' : 'Select'}
            </Badge>
          </CardFooter>
        </Card>

        <Card
          className={`cursor-pointer transition-all border-2 ${
            draft.accountType === 'staff'
              ? 'border-primary shadow-md bg-primary/5'
              : 'border-border hover:border-primary/50'
          }`}
          onClick={() => patch({ accountType: 'staff' })}
        >
          <CardHeader>
            <div className="size-10 rounded-xl bg-secondary text-secondary-foreground flex items-center justify-center mb-2">
              <Users className="size-5" aria-hidden />
            </div>
            <CardTitle className="font-serif text-xl">I&apos;m Staff</CardTitle>
            <CardDescription>
              Join an existing boutique using an invitation code shared by your owner.
            </CardDescription>
          </CardHeader>
          <CardFooter>
            <Badge variant={draft.accountType === 'staff' ? 'default' : 'outline'}>
              {draft.accountType === 'staff' ? 'Selected' : 'Select'}
            </Badge>
          </CardFooter>
        </Card>
      </div>

      {draft.accountType === 'owner' ? (
        <Button size="lg" className="w-full" onClick={() => goTo(3)}>
          Proceed to Boutique Setup <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      ) : (
        <Card>
          <CardHeader>
            <CardTitle className="font-serif text-lg">Enter Invitation Code</CardTitle>
            <CardDescription>
              Enter the code provided by your boutique owner to activate your staff profile.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={submitStaffJoin} className="flex flex-col gap-4">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="staffCode" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Invitation Code <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="staffCode"
                  value={draft.inviteCode}
                  onChange={(e) => patch({ inviteCode: e.target.value })}
                  placeholder="e.g. AB-9F7K2"
                  className="font-mono uppercase tracking-widest text-center text-lg"
                  required
                />
              </div>
              <Button type="submit" disabled={isSubmitting} size="lg" className="w-full">
                {isSubmitting ? 'Joining Atelier…' : 'Join Boutique & Enter Dashboard'}
              </Button>
            </form>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
