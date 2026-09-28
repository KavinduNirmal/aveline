import {
  ArrowRight,
  Check,
  CheckCircle2,
  Clock,
  Copy,
  Layers,
  Plus,
  QrCode,
  RefreshCw,
  Trash2,
  UserPlus,
} from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { QrCodeSvg } from '@/components/ui/QrCodeSvg'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { Switch } from '@/components/ui/switch'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'
import {
  createBulkInvitations,
  createInvitation,
  listPendingInvitations,
  revokeInvitation,
} from '@/lib/invitations'
import { expiryLabel, isExpiringSoon } from './invitationExpiry'
import type {
  BoutiqueStaffRole,
  CreateInvitationResponse,
  PendingInvitationDto,
} from '@/types/invitation'
import { EXPIRATION_OPTIONS, INVITABLE_ROLES } from '@/types/invitation'
import type { OrganizationProfileDto } from '@/types/organization'

const ROLE_LABEL: Record<string, string> = Object.fromEntries(
  INVITABLE_ROLES.map((r) => [r.value, r.label]),
)

/** The minimum a share needs to be useful, and possibly all the clipboard this browser has. */
const INVITE_FALLBACK_ORIGIN = 'https://aveline.lk/invite'

/** Which half of the drawer opens first. Both are one click apart inside it. */
export type InvitationDrawerView = 'generate' | 'pending'

interface InvitationDrawerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  organization: OrganizationProfileDto
  view: InvitationDrawerView
  /** Asks the opener to show the other half; the opener owns `view`. */
  onViewChange: (view: InvitationDrawerView) => void
  /** The page shows the pending count on its own button, so the drawer reports what it read. */
  onPendingCountChange: (count: number) => void
}

/**
 * The summary notice's three-state outcome as a sentence. `NotSent` is reported with the server's
 * reason rather than swallowed, because a requested notification that never went out is exactly the
 * failure the old boolean could not express.
 */
function summaryNote(status: string, note: string | null): string {
  if (status === 'Dispatched') {
    return note ?? 'Summary email dispatched.'
  }
  if (status === 'NotSent') {
    return note ?? 'No summary email was sent.'
  }
  return 'No summary email was requested.'
}

function inviteLink(link: string | null | undefined, code: string): string {
  return link && link.length > 0
    ? link
    : `${INVITE_FALLBACK_ORIGIN}/${code}`
}

/** Section label: the drawer's one recurring typographic device. */
function FieldLabel({ children, htmlFor }: { children: React.ReactNode; htmlFor?: string }) {
  return (
    <Label
      htmlFor={htmlFor}
      className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground"
    >
      {children}
    </Label>
  )
}

/**
 * Code generation, moved off the Team page and into a side drawer.
 *
 * The Team section is about **who is on the team**: the member list is the page, and minting an
 * onboarding code is an occasional task rather than a standing view. Keeping the generator and its
 * metrics on the page pushed the roster below the fold, so they live here.
 *
 * The drawer is two views behind one switch - generate a code, or work the codes already out - and
 * they share the header and the count badge. The generate view is a single narrowed column so the
 * hand-off (select role, select lifetime, mint, copy) reads top to bottom, rather than the row of
 * competing pill groups that made the previous layout jump as options wrapped.
 */
export function InvitationDrawer({
  open,
  onOpenChange,
  organization,
  view,
  onViewChange,
  onPendingCountChange,
}: InvitationDrawerProps) {
  // The opener owns which half is shown: it is the prop, not a mirror of it. Reopening on the other
  // half remounts the drawer through `key` (see TeamManagement), so no state sync is needed.
  const activeView = view
  const [mode, setMode] = useState<'single' | 'bulk'>('single')
  const [selectedRole, setSelectedRole] = useState<BoutiqueStaffRole>('org:boutique_staff')
  const [validityHours, setValidityHours] = useState<number>(24)
  const [email, setEmail] = useState('')
  const [bulkCount, setBulkCount] = useState<number>(3)
  const [sendSummaryToOwner, setSendSummaryToOwner] = useState(false)
  const [busy, setBusy] = useState(false)

  const [singleResult, setSingleResult] = useState<CreateInvitationResponse | null>(null)
  const [bulkResults, setBulkResults] = useState<CreateInvitationResponse[]>([])
  const [copied, setCopied] = useState<string | null>(null)
  const [activeQrCode, setActiveQrCode] = useState<{
    code: string
    link: string
  } | null>(null)

  const [pendingInvites, setPendingInvites] = useState<PendingInvitationDto[]>([])
  const [isLoading, setIsLoading] = useState(false)

  const loadPending = useCallback(async () => {
    try {
      setIsLoading(true)
      const data = await listPendingInvitations(organization.id)
      setPendingInvites(data)
      onPendingCountChange(data.length)
    } catch {
      setPendingInvites([])
      onPendingCountChange(0)
    } finally {
      setIsLoading(false)
    }
  }, [organization.id, onPendingCountChange])

  // Closed drawers issue no request: the roster is the page, and the badge is not worth a call per
  // render. Opening one is what asks for the codes.
  useEffect(() => {
    if (open) void loadPending()
  }, [open, loadPending])

  const handleGenerateSingle = async () => {
    setBusy(true)
    setBulkResults([])
    try {
      const res = await createInvitation(organization.id, {
        boutiqueRole: selectedRole,
        recipientEmail: email.trim() || undefined,
        validityHours,
        sendSummaryToOwner,
      })
      setSingleResult(res)
      setEmail('')
      toast.success(
        res.recipientEmail
          ? `Invitation sent to ${res.recipientEmail}.`
          : 'Invitation code generated.',
      )
      if (res.summaryEmailRequested) {
        toast.info(summaryNote(res.summaryEmailStatus, res.summaryEmailNote))
      }
      await loadPending()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Failed to generate invitation code.')
    } finally {
      setBusy(false)
    }
  }

  const handleGenerateBulk = async () => {
    setBusy(true)
    setSingleResult(null)
    try {
      const res = await createBulkInvitations(organization.id, {
        boutiqueRole: selectedRole,
        count: bulkCount,
        validityHours,
        sendSummaryToOwner,
      })
      setBulkResults(res.invitations)
      const clamped =
        res.createdCount !== res.requestedCount
          ? ` (asked for ${res.requestedCount}; the server mints at most ${res.createdCount})`
          : ''
      toast.success(`Generated ${res.createdCount} invitation codes${clamped}.`)
      if (res.summaryEmailRequested) {
        toast.info(summaryNote(res.summaryEmailStatus, res.summaryEmailNote))
      }
      await loadPending()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Failed to generate bulk invitation codes.')
    } finally {
      setBusy(false)
    }
  }

  const handleCopy = async (text: string, key: string, label: string) => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(key)
      toast.success(`${label} copied.`)
      setTimeout(() => setCopied(null), 1600)
    } catch {
      toast.error('Failed to copy to clipboard.')
    }
  }

  const handleRevoke = async (invitationId: string) => {
    try {
      await revokeInvitation(organization.id, invitationId)
      setPendingInvites((prev) => {
        const next = prev.filter((i) => i.invitationId !== invitationId)
        onPendingCountChange(next.length)
        return next
      })
      toast.info('Invitation code revoked.')
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Unable to revoke invitation.')
    }
  }

  const expiringSoonCount = pendingInvites.filter((i) => isExpiringSoon(i.expiresAt)).length
  const roleLabel = ROLE_LABEL[selectedRole] ?? selectedRole

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="flex w-full flex-col gap-0 overflow-y-auto p-0 sm:max-w-[30rem]">
        <SheetHeader className="gap-1.5 border-b px-6 pb-5 pt-6">
          <SheetTitle className="font-serif text-xl">Invite staff</SheetTitle>
          <SheetDescription className="text-[13px] leading-relaxed">
            Onboarding codes for {organization.name}. A code can be revoked until it is used.
          </SheetDescription>
        </SheetHeader>

        {/* Two views, one control. The count lives here so the pending view is worth opening. */}
        <div className="border-b px-6 py-3">
          <ToggleGroup
            type="single"
            value={activeView}
            onValueChange={(value) => {
              if (value && value !== activeView) onViewChange(value as InvitationDrawerView)
            }}
            spacing={0}
            className="grid w-full grid-cols-2 gap-0.5 rounded-full bg-muted/60 p-1"
          >
            {(
              [
                { id: 'generate', label: 'New code', icon: UserPlus },
                {
                  id: 'pending',
                  label: `Pending codes${pendingInvites.length > 0 ? ` (${pendingInvites.length})` : ''}`,
                  icon: Clock,
                },
              ] as const
            ).map((tab) => (
              <ToggleGroupItem
                key={tab.id}
                value={tab.id}
                size="sm"
                aria-label={tab.id === 'generate' ? 'New code' : 'Pending codes'}
                className={cn(
                  'h-8 w-full rounded-full text-[13px] font-medium text-muted-foreground',
                  'hover:bg-transparent hover:text-foreground',
                  'data-[state=on]:bg-background data-[state=on]:text-foreground',
                  'data-[state=on]:shadow-sm hover:data-[state=on]:bg-background',
                  'hover:data-[state=on]:text-foreground',
                )}
              >
                <tab.icon className="size-3.5" aria-hidden />
                {tab.label}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        </div>

        {activeView === 'generate' ? (
          <div className="flex flex-col gap-6 px-6 py-6">
            <section className="flex flex-col gap-2.5">
              <FieldLabel>Staff role</FieldLabel>
              <ToggleGroup
                type="single"
                value={selectedRole}
                onValueChange={(value) => {
                  if (value) setSelectedRole(value as BoutiqueStaffRole)
                }}
                spacing={0}
                className="grid w-full grid-cols-3 gap-0.5 rounded-xl bg-muted/60 p-1"
              >
                {INVITABLE_ROLES.map((r) => (
                  <ToggleGroupItem
                    key={r.value}
                    value={r.value}
                    className={cn(
                      'h-9 w-full rounded-lg text-[13px] font-medium capitalize text-muted-foreground',
                      'hover:bg-transparent hover:text-foreground',
                      'data-[state=on]:bg-background data-[state=on]:text-foreground',
                      'data-[state=on]:shadow-sm hover:data-[state=on]:bg-background',
                      'hover:data-[state=on]:text-foreground',
                    )}
                  >
                    {r.label}
                  </ToggleGroupItem>
                ))}
              </ToggleGroup>
              {/* Says what the choice grants, so the pills are not a label-only decision. */}
              <p className="text-xs text-muted-foreground">
                {ROLE_DESCRIPTION[selectedRole] ?? 'Boutique access.'}
              </p>
            </section>

            <section className="flex flex-col gap-2.5">
              <FieldLabel>Code lifetime</FieldLabel>
              <ToggleGroup
                type="single"
                value={String(validityHours)}
                onValueChange={(value) => {
                  if (value) setValidityHours(Number(value))
                }}
                variant="outline"
                size="sm"
                className="flex-wrap justify-start gap-1.5"
              >
                {EXPIRATION_OPTIONS.map((opt) => (
                  <ToggleGroupItem
                    key={opt.hours}
                    value={String(opt.hours)}
                    className={cn(
                      'rounded-full px-3 text-[12px] text-muted-foreground',
                      'data-[state=on]:border-primary data-[state=on]:bg-primary/10',
                      'data-[state=on]:text-primary',
                    )}
                  >
                    {opt.label}
                  </ToggleGroupItem>
                ))}
              </ToggleGroup>
              <p className="text-xs text-muted-foreground">
                After this the code stops working; the server clamps it to between 1 hour and 30
                days.
              </p>
            </section>

            <section className="flex flex-col gap-2.5">
              <div className="flex items-center justify-between">
                <FieldLabel>How many</FieldLabel>
                <ToggleGroup
                  type="single"
                  value={mode}
                  onValueChange={(value) => {
                    if (value) setMode(value as 'single' | 'bulk')
                  }}
                  size="sm"
                  className="gap-0.5 rounded-full bg-muted/60 p-0.5"
                >
                  {(
                    [
                      { id: 'single', label: 'One' },
                      { id: 'bulk', label: 'Batch' },
                    ] as const
                  ).map((option) => (
                    <ToggleGroupItem
                      key={option.id}
                      value={option.id}
                      className={cn(
                        'rounded-full px-2.5 text-[12px] text-muted-foreground',
                        'hover:bg-transparent hover:text-foreground',
                        'data-[state=on]:bg-background data-[state=on]:text-foreground',
                        'data-[state=on]:shadow-sm hover:data-[state=on]:bg-background',
                        'hover:data-[state=on]:text-foreground',
                      )}
                    >
                      {option.label}
                    </ToggleGroupItem>
                  ))}
                </ToggleGroup>
              </div>

              {mode === 'single' ? (
                <div className="flex flex-col gap-2">
                  <FieldLabel htmlFor="staffEmail">Send to (optional)</FieldLabel>
                  <Input
                    id="staffEmail"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="colleague@boutique.lk"
                  />
                  <p className="text-xs text-muted-foreground">
                    Leave it empty to copy the code and share it yourself.
                  </p>
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  <FieldLabel htmlFor="bulkCount">Codes to mint</FieldLabel>
                  <Input
                    id="bulkCount"
                    type="number"
                    min={1}
                    max={10}
                    value={bulkCount}
                    onChange={(e) =>
                      setBulkCount(Math.max(1, Math.min(10, Number(e.target.value) || 1)))
                    }
                    className="w-24"
                  />
                  <p className="text-xs text-muted-foreground">
                    Up to ten at a time, each a unique 12-character code for {roleLabel.toLowerCase()}.
                  </p>
                </div>
              )}
            </section>

            <label
              htmlFor="send-summary-to-owner"
              className="flex cursor-pointer items-start gap-3 rounded-xl border bg-muted/30 p-3.5"
            >
              <Switch
                id="send-summary-to-owner"
                checked={sendSummaryToOwner}
                onCheckedChange={setSendSummaryToOwner}
                className="mt-0.5"
              />
              <span className="flex flex-col gap-0.5">
                <span className="text-[13px] font-medium">Email me a summary</span>
                <span className="text-xs text-muted-foreground">
                  Sent to your owner address. The code is never in the email.
                </span>
              </span>
            </label>

            {/* The one action, at the end of the flow it completes. */}
            <div className="flex flex-col gap-3 border-t pt-5">
              <Button
                type="button"
                onClick={() => void (mode === 'single' ? handleGenerateSingle() : handleGenerateBulk())}
                disabled={busy}
                className="h-11 w-full gap-2 text-[15px]"
              >
                {busy ? (
                  <RefreshCw className="size-4 animate-spin" aria-hidden />
                ) : mode === 'single' ? (
                  <Plus className="size-4" aria-hidden />
                ) : (
                  <Layers className="size-4" aria-hidden />
                )}
                {busy
                  ? 'Generating...'
                  : mode === 'single'
                    ? 'Generate code'
                    : `Generate ${bulkCount} code${bulkCount === 1 ? '' : 's'}`}
              </Button>
              <p className="text-center text-xs text-muted-foreground">
                {mode === 'single'
                  ? `One ${roleLabel.toLowerCase()} code, valid ${validityLabel(validityHours)}.`
                  : `${bulkCount} ${roleLabel.toLowerCase()} codes, each valid ${validityLabel(validityHours)}.`}
              </p>
            </div>

            {singleResult && (
              <section className="flex flex-col gap-3 rounded-2xl border border-primary/25 bg-primary/[0.04] p-4">
                <div className="flex items-center justify-between">
                  <span className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-[0.12em] text-primary">
                    <CheckCircle2 className="size-4" aria-hidden /> Code ready
                  </span>
                  <Badge variant="outline">{ROLE_LABEL[singleResult.boutiqueRole] ?? singleResult.boutiqueRole}</Badge>
                </div>

                <code className="block rounded-xl border bg-background px-3 py-3 text-center font-mono text-lg font-semibold tracking-[0.18em]">
                  {singleResult.code}
                </code>

                <div className="grid grid-cols-2 gap-2">
                  <Button
                    type="button"
                    size="sm"
                    onClick={() =>
                      void handleCopy(singleResult.code, 'single-code', 'Code')
                    }
                  >
                    {copied === 'single-code' ? (
                      <Check className="size-3.5" aria-hidden />
                    ) : (
                      <Copy className="size-3.5" aria-hidden />
                    )}
                    {copied === 'single-code' ? 'Copied' : 'Copy code'}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      void handleCopy(inviteLink(singleResult.link, singleResult.code), 'single-link', 'Link')
                    }
                  >
                    {copied === 'single-link' ? (
                      <Check className="size-3.5" aria-hidden />
                    ) : (
                      <Copy className="size-3.5" aria-hidden />
                    )}
                    {copied === 'single-link' ? 'Copied' : 'Copy link'}
                  </Button>
                </div>

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="gap-1.5 text-muted-foreground"
                  onClick={() =>
                    setActiveQrCode({
                      code: singleResult.code,
                      link: inviteLink(singleResult.link, singleResult.code),
                    })
                  }
                >
                  <QrCode className="size-3.5" aria-hidden /> Show QR code
                </Button>

                <p className="text-xs text-muted-foreground">
                  This is the only time the code is shown here. It is stored hashed, so it cannot be
                  looked up again - revoke and mint a new one if it is lost.
                </p>
              </section>
            )}

            {bulkResults.length > 0 && (
              <section className="flex flex-col gap-3 rounded-2xl border border-primary/25 bg-primary/[0.04] p-4">
                <span className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-[0.12em] text-primary">
                  <CheckCircle2 className="size-4" aria-hidden /> {bulkResults.length} codes ready
                </span>
                <div className="flex max-h-64 flex-col gap-2 overflow-y-auto pr-1">
                  {bulkResults.map((item, idx) => (
                    <div
                      key={item.invitationId}
                      className="flex items-center justify-between gap-3 rounded-xl border bg-background px-3 py-2"
                    >
                      <code className="font-mono text-sm font-semibold tracking-[0.14em]">
                        {item.code}
                      </code>
                      <div className="flex items-center gap-1">
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          aria-label={`Copy code ${idx + 1}`}
                          onClick={() => void handleCopy(item.code, `bulk-${idx}`, 'Code')}
                        >
                          {copied === `bulk-${idx}` ? (
                            <Check className="size-3.5" aria-hidden />
                          ) : (
                            <Copy className="size-3.5" aria-hidden />
                          )}
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          aria-label={`Show QR code ${idx + 1}`}
                          onClick={() =>
                            setActiveQrCode({
                              code: item.code,
                              link: inviteLink(item.link, item.code),
                            })
                          }
                        >
                          <QrCode className="size-3.5" aria-hidden />
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
                <p className="text-xs text-muted-foreground">
                  Copy or scan each one now; they cannot be looked up again.
                </p>
              </section>
            )}
          </div>
        ) : (
          <div className="flex flex-col gap-5 px-6 py-6">
            <div className="grid grid-cols-2 gap-3">
              <div className="rounded-xl border bg-card p-3.5">
                <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                  Outstanding
                </p>
                <p className="mt-1 font-serif text-2xl font-medium">{pendingInvites.length}</p>
              </div>
              <div
                className={cn(
                  'rounded-xl border p-3.5',
                  expiringSoonCount > 0 ? 'border-warning/40 bg-warning/[0.06]' : 'bg-card',
                )}
              >
                <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                  Expiring soon
                </p>
                <p
                  className={cn(
                    'mt-1 font-serif text-2xl font-medium',
                    expiringSoonCount > 0 && 'text-warning',
                  )}
                >
                  {expiringSoonCount}
                </p>
              </div>
            </div>

            {isLoading ? (
              <p className="flex items-center justify-center gap-2 py-10 text-sm text-muted-foreground">
                <RefreshCw className="size-4 animate-spin" aria-hidden /> Loading codes...
              </p>
            ) : pendingInvites.length === 0 ? (
              <div className="flex flex-col items-center gap-2 rounded-2xl border border-dashed py-10 text-center">
                <Clock className="size-5 text-muted-foreground" aria-hidden />
                <p className="text-sm font-medium">No active codes</p>
                <Button
                  type="button"
                  variant="link"
                  size="sm"
                  className="h-auto gap-1 p-0 text-xs"
                  onClick={() => onViewChange('generate')}
                >
                  Generate one <ArrowRight className="size-3" aria-hidden />
                </Button>
              </div>
            ) : (
              <ul className="flex flex-col gap-2.5">
                {pendingInvites.map((inv) => {
                  const soon = isExpiringSoon(inv.expiresAt)
                  return (
                    <li
                      key={inv.invitationId}
                      className="flex flex-col gap-3 rounded-2xl border bg-card p-3.5"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div className="flex min-w-0 flex-col gap-1">
                          <span className="truncate text-sm font-medium">
                            {inv.recipientEmail ?? 'Not addressed to anyone'}
                          </span>
                          <span
                            className={cn(
                              'text-xs',
                              soon ? 'font-medium text-warning' : 'text-muted-foreground',
                            )}
                          >
                            {expiryLabel(inv.expiresAt)}
                            {soon ? ' - expiring soon' : ''}
                          </span>
                        </div>
                        <Badge variant="secondary" className="shrink-0">
                          {ROLE_LABEL[inv.boutiqueRole] ?? inv.boutiqueRole}
                        </Badge>
                      </div>

                      <div className="flex items-center justify-between gap-2 border-t pt-3">
                        {/* The code itself is not returned by the list (`PendingInvitationDto` is
                            the non-secret view), so there is nothing here to copy or show. */}
                        <span className="text-xs text-muted-foreground">
                          Minted {new Date(inv.createdAt).toLocaleDateString()}
                        </span>
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          aria-label="Revoke code"
                          className="shrink-0 text-destructive hover:text-destructive"
                          onClick={() => void handleRevoke(inv.invitationId)}
                        >
                          <Trash2 className="size-3.5" aria-hidden />
                          Revoke
                        </Button>
                      </div>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        )}

        {activeQrCode && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs">
            <div className="flex w-full max-w-sm flex-col items-center gap-4 rounded-2xl border bg-background p-6 text-center shadow-xl">
              <h3 className="font-serif text-lg font-medium">Invitation QR code</h3>
              <p className="text-xs text-muted-foreground">
                Scan with a mobile camera to open the invitation link directly.
              </p>
              <div className="rounded-xl border bg-white p-3">
                <QrCodeSvg value={activeQrCode.link} size={180} />
              </div>
              <code className="rounded-lg bg-muted px-3 py-1.5 font-mono text-sm font-semibold tracking-[0.18em]">
                {activeQrCode.code}
              </code>
              <Button
                type="button"
                className="mt-1 w-full"
                variant="outline"
                onClick={() => setActiveQrCode(null)}
              >
                Close
              </Button>
            </div>
          </div>
        )}
      </SheetContent>
    </Sheet>
  )
}

/** What each invitable role may do, in one line, so the role pills are an informed choice. */
const ROLE_DESCRIPTION: Record<string, string> = {
  'org:boutique_manager':
    'Runs the shop day to day: clients, catalog, income and staff, without the integrations credentials.',
  'org:boutique_supervisor':
    'Approves and oversees the counter: clients, catalog and the income register.',
  'org:boutique_staff': 'The counter: clients, visits and orders, with no register or settings.',
}

function validityLabel(hours: number): string {
  const option = EXPIRATION_OPTIONS.find((o) => o.hours === hours)
  if (option) return option.label.toLowerCase()
  if (hours < 24) return `for ${hours} hour${hours === 1 ? '' : 's'}`
  const days = Math.round(hours / 24)
  return `for ${days} day${days === 1 ? '' : 's'}`
}
