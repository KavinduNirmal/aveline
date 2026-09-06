import { ArrowRight, CheckCircle2, Copy, Mail, RefreshCw, Trash2, UserPlus } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { toast } from 'sonner'

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
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

import { createInvitation, listPendingInvitations, revokeInvitation } from '@/lib/invitations'
import type { BoutiqueStaffRole, PendingInvitationDto } from '@/types/invitation'
import { INVITABLE_ROLES } from '@/types/invitation'
import { useOwnerOnboardingWizard } from '../wizard-context'

const ROLE_LABEL: Record<string, string> = Object.fromEntries(
  INVITABLE_ROLES.map((r) => [r.value, r.label]),
)

export function InviteStaffStep() {
  const { draft, goTo } = useOwnerOnboardingWizard()
  const organizationId = draft.organizationId
  const [role, setRole] = useState<BoutiqueStaffRole>('org:boutique_staff')
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [createdLink, setCreatedLink] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [pending, setPending] = useState<PendingInvitationDto[]>([])

  const loadPending = useCallback(async () => {
    if (!organizationId) return
    try {
      setPending(await listPendingInvitations(organizationId))
    } catch {
      setPending([])
    }
  }, [organizationId])

  useEffect(() => {
    void loadPending()
  }, [loadPending])

  const handleInvite = async () => {
    if (!organizationId) return
    setBusy(true)
    try {
      const result = await createInvitation(organizationId, {
        boutiqueRole: role,
        recipientEmail: email.trim() || undefined,
      })
      setCreatedLink(result.link)
      setCopied(false)
      setEmail('')
      toast.success(
        result.recipientEmail
          ? `Invitation sent to ${result.recipientEmail}.`
          : 'Invitation created — share the code or link below.',
      )
      await loadPending()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Unable to create the invitation.')
    } finally {
      setBusy(false)
    }
  }

  const handleCopy = async () => {
    if (!createdLink) return
    try {
      await navigator.clipboard.writeText(createdLink)
      setCopied(true)
      toast.success('Invitation link copied.')
      setTimeout(() => setCopied(false), 1500)
    } catch {
      toast.error('Copy failed — please copy the link manually.')
    }
  }

  const handleRevoke = async (invitationId: string) => {
    if (!organizationId) return
    try {
      await revokeInvitation(organizationId, invitationId)
      setPending((prev) => prev.filter((i) => i.invitationId !== invitationId))
      toast.info('Invitation revoked.')
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Unable to revoke the invitation.')
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-serif text-xl font-medium">Invite Your Team</CardTitle>
        <CardDescription>
          Invite boutique staff so they can join your workspace once onboarding completes. This is
          optional — you can invite more team members later from Settings.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex flex-col gap-2">
          <Label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Role</Label>
          <ToggleGroup
            type="single"
            value={role}
            onValueChange={(val) => {
              if (val) setRole(val as BoutiqueStaffRole)
            }}
            variant="outline"
            className="flex flex-wrap gap-2 justify-start"
          >
            {INVITABLE_ROLES.map((r) => (
              <ToggleGroupItem key={r.value} value={r.value}>
                {r.label}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="inviteEmail" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
            Staff Email (optional)
          </Label>
          <div className="flex flex-col sm:flex-row gap-2">
            <Input
              id="inviteEmail"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="colleague@boutique.lk"
            />
            <Button onClick={() => void handleInvite()} disabled={!organizationId || busy} className="sm:w-auto">
              {busy ? <RefreshCw className="size-4 animate-spin" data-icon="inline-start" /> : <UserPlus className="size-4" data-icon="inline-start" />}
              Invite
            </Button>
          </div>
          <p className="text-xs text-muted-foreground flex items-center gap-1.5">
            <Mail className="size-3.5" aria-hidden /> If no email is provided, share the generated code or link manually.
          </p>
        </div>

        {createdLink && (
          <div className="flex flex-col gap-2 p-3 rounded-xl border border-border bg-muted/40">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Invitation Link</span>
            <div className="flex items-center gap-2">
              <code className="flex-1 text-xs truncate bg-background border border-border rounded-md px-2 py-1.5">
                {createdLink}
              </code>
              <Button size="sm" variant="outline" onClick={() => void handleCopy()}>
                {copied ? <CheckCircle2 className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" data-icon="inline-start" />}
                {copied ? 'Copied' : 'Copy'}
              </Button>
            </div>
          </div>
        )}

        {pending.length > 0 && (
          <div className="flex flex-col gap-2">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Pending Invitations ({pending.length})
            </span>
            <div className="flex flex-col gap-2">
              {pending.map((inv) => (
                <div key={inv.invitationId} className="flex items-center justify-between gap-3 rounded-lg border border-border p-3">
                  <div className="flex flex-col min-w-0">
                    <span className="text-sm font-medium truncate">
                      {inv.recipientEmail ?? 'Manual (code shared)'}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {ROLE_LABEL[inv.boutiqueRole] ?? inv.boutiqueRole} · Expires{' '}
                      {new Date(inv.expiresAt).toLocaleDateString()}
                    </span>
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    <Badge variant="secondary" className="text-[10px]">Pending</Badge>
                    <Button size="icon" variant="ghost" onClick={() => void handleRevoke(inv.invitationId)} aria-label="Revoke invitation">
                      <Trash2 className="size-4" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </CardContent>
      <CardFooter className="flex justify-between gap-3">
        <Button variant="outline" onClick={() => goTo(6)}>
          Back
        </Button>
        <Button onClick={() => goTo(8)}>
          Continue to Review <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      </CardFooter>
    </Card>
  )
}
