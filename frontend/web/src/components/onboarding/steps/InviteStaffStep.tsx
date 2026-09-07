import { ArrowRight, CheckCircle2, Copy, QrCode, RefreshCw, Sparkles, Trash2, UserPlus } from 'lucide-react'
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
import { QrCodeSvg } from '@/components/ui/QrCodeSvg'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

import { createInvitation, listPendingInvitations, revokeInvitation } from '@/lib/invitations'
import type { BoutiqueStaffRole, ExpirationOption, PendingInvitationDto } from '@/types/invitation'
import { EXPIRATION_OPTIONS, INVITABLE_ROLES } from '@/types/invitation'
import { useOwnerOnboardingWizard } from '../wizard-context'


const ROLE_LABEL: Record<string, string> = Object.fromEntries(
  INVITABLE_ROLES.map((r) => [r.value, r.label]),
)

export function InviteStaffStep() {
  const { draft, goTo } = useOwnerOnboardingWizard()
  const organizationId = draft.organizationId
  const [role, setRole] = useState<BoutiqueStaffRole>('org:boutique_staff')
  const [validityHours, setValidityHours] = useState<number>(24)
  const [email, setEmail] = useState('')
  const [sendSummaryToOwner, setSendSummaryToOwner] = useState(false)
  const [busy, setBusy] = useState(false)
  const [createdResult, setCreatedResult] = useState<{ code: string; link: string } | null>(null)
  const [copiedCode, setCopiedCode] = useState(false)
  const [copiedLink, setCopiedLink] = useState(false)
  const [showQrModal, setShowQrModal] = useState(false)
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
        validityHours,
        sendSummaryToOwner,
      })
      setCreatedResult({ code: result.code, link: result.link })
      setCopiedCode(false)
      setCopiedLink(false)
      setEmail('')
      toast.success(
        result.recipientEmail
          ? `Invitation sent to ${result.recipientEmail}.`
          : 'Invitation code generated successfully!',
      )
      await loadPending()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Unable to create the invitation.')
    } finally {
      setBusy(false)
    }
  }

  const handleCopyCode = async () => {
    if (!createdResult) return
    try {
      await navigator.clipboard.writeText(createdResult.code)
      setCopiedCode(true)
      toast.success('Invitation code copied to clipboard.')
      setTimeout(() => setCopiedCode(false), 1500)
    } catch {
      toast.error('Copy failed — please copy the code manually.')
    }
  }

  const handleCopyLink = async () => {
    if (!createdResult) return
    try {
      await navigator.clipboard.writeText(createdResult.link)
      setCopiedLink(true)
      toast.success('Shareable link copied to clipboard.')
      setTimeout(() => setCopiedLink(false), 1500)
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
        <CardTitle className="font-serif text-xl font-medium flex items-center gap-2">
          <Sparkles className="size-5 text-primary" aria-hidden /> Invite Your Team &amp; Generate Codes
        </CardTitle>
        <CardDescription>
          Generate invitation codes for your boutique staff so they can join your workspace. Staff can enter their 12-character code in the mobile app or web sign-up.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex flex-col gap-2">
          <Label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Staff Role</Label>
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
          <Label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Code Expiration</Label>
          <ToggleGroup
            type="single"
            value={String(validityHours)}
            onValueChange={(val) => {
              if (val) setValidityHours(Number(val))
            }}
            variant="outline"
            className="flex flex-wrap gap-2 justify-start"
          >
            {EXPIRATION_OPTIONS.map((opt: ExpirationOption) => (
              <ToggleGroupItem key={opt.hours} value={String(opt.hours)}>
                {opt.label}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="inviteEmail" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
            Staff Email (Optional)
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
              Generate Code
            </Button>
          </div>
          <div className="flex items-center gap-2 mt-1">
            <label className="flex items-center gap-2 text-xs text-muted-foreground cursor-pointer">
              <input
                type="checkbox"
                checked={sendSummaryToOwner}
                onChange={(e) => setSendSummaryToOwner(e.target.checked)}
                className="rounded border-border text-primary focus:ring-primary"
              />
              Send summary email to my owner address
            </label>
          </div>
        </div>

        {createdResult && (
          <div className="flex flex-col gap-3 p-4 rounded-xl border border-primary/20 bg-primary/5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-primary flex items-center gap-1.5">
                <CheckCircle2 className="size-4 text-emerald-600" /> Invitation Code Generated
              </span>
              <Badge variant="outline" className="text-[11px] font-mono">
                {EXPIRATION_OPTIONS.find((o: ExpirationOption) => o.hours === validityHours)?.label}
              </Badge>
            </div>

            <div className="flex flex-col sm:flex-row items-center gap-3 bg-background border border-border p-3 rounded-lg">
              <div className="flex-1 text-center sm:text-start">
                <p className="text-xs text-muted-foreground font-medium">12-Character Invitation Code</p>
                <code className="text-lg font-mono font-bold tracking-widest text-foreground">
                  {createdResult.code}
                </code>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <Button size="sm" variant="secondary" onClick={() => void handleCopyCode()}>
                  {copiedCode ? <CheckCircle2 className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" data-icon="inline-start" />}
                  {copiedCode ? 'Copied' : 'Copy Code'}
                </Button>
                <Button size="sm" variant="outline" onClick={() => void handleCopyLink()}>
                  {copiedLink ? <CheckCircle2 className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" data-icon="inline-start" />}
                  {copiedLink ? 'Copied' : 'Copy Link'}
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setShowQrModal(true)}>
                  <QrCode className="size-4" />
                </Button>
              </div>
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
                      {inv.recipientEmail ?? 'Standalone Invitation Code'}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {ROLE_LABEL[inv.boutiqueRole] ?? inv.boutiqueRole} · Expires{' '}
                      {new Date(inv.expiresAt).toLocaleString()}
                    </span>
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    <Badge variant="secondary" className="text-[10px]">Pending</Badge>
                    <Button size="icon" variant="ghost" onClick={() => void handleRevoke(inv.invitationId)} aria-label="Revoke invitation">
                      <Trash2 className="size-4 text-destructive" />
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

      {/* QR Code Modal */}
      {showQrModal && createdResult && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <div className="bg-background border border-border rounded-xl p-6 max-w-sm w-full flex flex-col items-center gap-4 text-center shadow-xl">
            <h3 className="font-serif text-lg font-medium">Scan to Join Boutique</h3>
            <p className="text-xs text-muted-foreground">
              Scan this QR code with a phone camera to immediately open the invitation link.
            </p>
            <div className="p-3 bg-white rounded-lg border border-border">
              <QrCodeSvg value={createdResult.link} size={180} />
            </div>
            <code className="text-sm font-mono font-bold tracking-widest px-3 py-1 bg-muted rounded">
              {createdResult.code}
            </code>
            <Button className="w-full mt-2" variant="outline" onClick={() => setShowQrModal(false)}>
              Close
            </Button>
          </div>
        </div>
      )}
    </Card>
  )
}

