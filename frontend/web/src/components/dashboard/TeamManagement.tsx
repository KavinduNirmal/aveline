import {
  AlertTriangle,
  CheckCircle2,
  Clock,
  Copy,
  Layers,
  Plus,
  QrCode,
  RefreshCw,
  Sparkles,
  Trash2,
  UserCheck,
  UserPlus,
} from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
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
import { QrCodeSvg } from '@/components/ui/QrCodeSvg'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

import {
  createBulkInvitations,
  createInvitation,
  listPendingInvitations,
  revokeInvitation,
} from '@/lib/invitations'
import type {
  BoutiqueStaffRole,
  CreateInvitationResponse,
  ExpirationOption,
  PendingInvitationDto,
} from '@/types/invitation'
import { EXPIRATION_OPTIONS, INVITABLE_ROLES } from '@/types/invitation'
import type { OrganizationProfileDto } from '@/types/organization'


interface TeamManagementProps {
  organization: OrganizationProfileDto
  role: string
}

const ROLE_LABEL: Record<string, string> = Object.fromEntries(
  INVITABLE_ROLES.map((r) => [r.value, r.label]),
)

export function TeamManagement({ organization }: TeamManagementProps) {
  const [activeTab, setActiveTab] = useState<'pending' | 'members'>('pending')
  const [mode, setMode] = useState<'single' | 'bulk'>('single')
  const [selectedRole, setSelectedRole] = useState<BoutiqueStaffRole>('org:boutique_staff')
  const [validityHours, setValidityHours] = useState<number>(24)
  const [email, setEmail] = useState('')
  const [bulkCount, setBulkCount] = useState<number>(3)
  const [sendSummaryToOwner, setSendSummaryToOwner] = useState(false)
  const [busy, setBusy] = useState(false)

  const [singleResult, setSingleResult] = useState<CreateInvitationResponse | null>(null)
  const [bulkResults, setBulkResults] = useState<CreateInvitationResponse[]>([])
  const [copiedIndex, setCopiedIndex] = useState<string | null>(null)

  const [activeQrCode, setActiveQrCode] = useState<{ code: string; link: string } | null>(null)
  const [pendingInvites, setPendingInvites] = useState<PendingInvitationDto[]>([])
  const [isLoading, setIsLoading] = useState(true)

  const loadPending = useCallback(async () => {
    try {
      setIsLoading(true)
      const data = await listPendingInvitations(organization.id)
      setPendingInvites(data)
    } catch {
      setPendingInvites([])
    } finally {
      setIsLoading(false)
    }
  }, [organization.id])

  useEffect(() => {
    void loadPending()
  }, [loadPending])

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
          : 'Staff invitation code generated!',
      )
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
      setBulkResults(res)
      toast.success(`Successfully generated ${res.length} staff invitation codes!`)
      await loadPending()
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Failed to generate bulk invitation codes.')
    } finally {
      setBusy(false)
    }
  }

  const handleCopy = async (text: string, key: string, label = 'Copied') => {
    try {
      await navigator.clipboard.writeText(text)
      setCopiedIndex(key)
      toast.success(`${label} to clipboard.`)
      setTimeout(() => setCopiedIndex(null), 1500)
    } catch {
      toast.error('Failed to copy to clipboard.')
    }
  }

  const handleRevoke = async (invitationId: string) => {
    try {
      await revokeInvitation(organization.id, invitationId)
      setPendingInvites((prev) => prev.filter((i) => i.invitationId !== invitationId))
      toast.info('Invitation code revoked.')
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Unable to revoke invitation.')
    }
  }

  // Calculate metrics
  const expiringSoonCount = pendingInvites.filter((i) => {
    const expires = new Date(i.expiresAt).getTime()
    const now = Date.now()
    return expires - now > 0 && expires - now < 3600 * 6 * 1000 // less than 6 hours
  }).length

  return (
    <div className="flex flex-col gap-6">
      {/* Top Header */}
      <div>
        <h1 className="font-serif text-2xl font-medium tracking-tight">Team &amp; Code Generation</h1>
        <p className="text-sm text-muted-foreground">
          Generate, share, and manage staff onboarding codes for <span className="font-medium text-foreground">{organization.name}</span>.
        </p>
      </div>

      {/* Metrics Row */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Card className="p-4 flex items-center gap-4">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <UserCheck className="size-5" />
          </div>
          <div>
            <p className="text-xs text-muted-foreground font-medium">Boutique Owner</p>
            <p className="text-lg font-semibold">Active Workspace</p>
          </div>
        </Card>

        <Card className="p-4 flex items-center gap-4">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-amber-500/10 text-amber-600">
            <Clock className="size-5" />
          </div>
          <div>
            <p className="text-xs text-muted-foreground font-medium">Pending Codes</p>
            <p className="text-lg font-semibold">{pendingInvites.length} Active Codes</p>
          </div>
        </Card>

        <Card className="p-4 flex items-center gap-4">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-rose-500/10 text-rose-600">
            <AlertTriangle className="size-5" />
          </div>
          <div>
            <p className="text-xs text-muted-foreground font-medium">Expiring Soon</p>
            <p className="text-lg font-semibold">{expiringSoonCount} Codes (&lt;6h)</p>
          </div>
        </Card>
      </div>

      {/* Main Code Generator Studio Card */}
      <Card>
        <CardHeader>
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <CardTitle className="font-serif text-lg font-medium flex items-center gap-2">
                <Sparkles className="size-5 text-primary" aria-hidden /> Code Generator Studio
              </CardTitle>
              <CardDescription>
                Create 12-character invitation codes to onboard boutique managers, supervisors, or staff members.
              </CardDescription>
            </div>
            <ToggleGroup
              type="single"
              value={mode}
              onValueChange={(val) => {
                if (val) setMode(val as 'single' | 'bulk')
              }}
              variant="outline"
              className="self-start sm:self-auto"
            >
              <ToggleGroupItem value="single" className="gap-1.5 text-xs">
                <UserPlus className="size-3.5" /> Single Code
              </ToggleGroupItem>
              <ToggleGroupItem value="bulk" className="gap-1.5 text-xs">
                <Layers className="size-3.5" /> Batch / Bulk
              </ToggleGroupItem>
            </ToggleGroup>
          </div>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* Role selection */}
            <div className="flex flex-col gap-2">
              <Label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Staff Role
              </Label>
              <ToggleGroup
                type="single"
                value={selectedRole}
                onValueChange={(val) => {
                  if (val) setSelectedRole(val as BoutiqueStaffRole)
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

            {/* Expiration selection */}
            <div className="flex flex-col gap-2">
              <Label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Expiration Duration
              </Label>
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
          </div>

          {/* Mode-specific input controls */}
          {mode === 'single' ? (
            <div className="flex flex-col gap-2">
              <Label htmlFor="staffEmail" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Staff Email (Optional)
              </Label>
              <div className="flex flex-col sm:flex-row gap-2">
                <Input
                  id="staffEmail"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="colleague@boutique.lk"
                />
                <Button onClick={() => void handleGenerateSingle()} disabled={busy} className="sm:w-auto">
                  {busy ? <RefreshCw className="size-4 animate-spin" /> : <Plus className="size-4" />}
                  Generate Code
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              <Label htmlFor="bulkCount" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Number of Codes to Generate (Batch Mode)
              </Label>
              <div className="flex flex-col sm:flex-row gap-2 items-center">
                <Input
                  id="bulkCount"
                  type="number"
                  min={1}
                  max={10}
                  value={bulkCount}
                  onChange={(e) => setBulkCount(Math.max(1, Math.min(10, Number(e.target.value))))}
                  className="sm:w-32"
                />
                <span className="text-xs text-muted-foreground flex-1">
                  Generates {bulkCount} unique 12-char codes bound to {ROLE_LABEL[selectedRole]}.
                </span>
                <Button onClick={() => void handleGenerateBulk()} disabled={busy} className="sm:w-auto">
                  {busy ? <RefreshCw className="size-4 animate-spin" /> : <Layers className="size-4" />}
                  Generate {bulkCount} Codes
                </Button>
              </div>
            </div>
          )}

          <div className="flex items-center gap-2">
            <label className="flex items-center gap-2 text-xs text-muted-foreground cursor-pointer">
              <input
                type="checkbox"
                checked={sendSummaryToOwner}
                onChange={(e) => setSendSummaryToOwner(e.target.checked)}
                className="rounded border-border text-primary focus:ring-primary"
              />
              Send summary email notification to my owner address
            </label>
          </div>

          {/* Generated Code Output Display */}
          {singleResult && (
            <div className="flex flex-col gap-3 p-4 rounded-xl border border-primary/20 bg-primary/5">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold uppercase tracking-wider text-primary flex items-center gap-1.5">
                  <CheckCircle2 className="size-4 text-emerald-600" /> New Code Generated
                </span>
                <Badge variant="outline" className="text-[11px] font-mono">
                  {ROLE_LABEL[singleResult.boutiqueRole]}
                </Badge>
              </div>
              <div className="flex flex-col sm:flex-row items-center gap-3 bg-background border border-border p-3 rounded-lg">
                <div className="flex-1 text-center sm:text-start">
                  <p className="text-xs text-muted-foreground font-medium">12-Character Invitation Code</p>
                  <code className="text-xl font-mono font-bold tracking-widest text-foreground">
                    {singleResult.code}
                  </code>
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <Button size="sm" variant="secondary" onClick={() => void handleCopy(singleResult.code, 'single-code', 'Code copied')}>
                    {copiedIndex === 'single-code' ? <CheckCircle2 className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" />}
                    {copiedIndex === 'single-code' ? 'Copied' : 'Copy Code'}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => void handleCopy(singleResult.link, 'single-link', 'Link copied')}>
                    {copiedIndex === 'single-link' ? <CheckCircle2 className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" />}
                    {copiedIndex === 'single-link' ? 'Copied' : 'Copy Link'}
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setActiveQrCode({ code: singleResult.code, link: singleResult.link })}>
                    <QrCode className="size-4" />
                  </Button>
                </div>
              </div>
            </div>
          )}

          {/* Bulk Results Table */}
          {bulkResults.length > 0 && (
            <div className="flex flex-col gap-3 p-4 rounded-xl border border-primary/20 bg-primary/5">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold uppercase tracking-wider text-primary flex items-center gap-1.5">
                  <CheckCircle2 className="size-4 text-emerald-600" /> Batch Generated {bulkResults.length} Codes
                </span>
              </div>
              <div className="flex flex-col gap-2">
                {bulkResults.map((item, idx) => (
                  <div key={item.invitationId} className="flex items-center justify-between bg-background border border-border p-2.5 rounded-lg text-sm">
                    <div className="flex items-center gap-3">
                      <span className="text-xs font-mono text-muted-foreground w-6">#{idx + 1}</span>
                      <code className="font-mono font-bold text-base tracking-wider">{item.code}</code>
                    </div>
                    <div className="flex items-center gap-2">
                      <Button size="sm" variant="ghost" onClick={() => void handleCopy(item.code, `bulk-${idx}`, 'Code copied')}>
                        {copiedIndex === `bulk-${idx}` ? <CheckCircle2 className="size-3.5 text-emerald-600" /> : <Copy className="size-3.5" />}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setActiveQrCode({ code: item.code, link: item.link })}>
                        <QrCode className="size-3.5" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Pending & Active Members Tabs */}
      <Card>
        <CardHeader className="border-b pb-4">
          <div className="flex items-center gap-4">
            <button
              type="button"
              onClick={() => setActiveTab('pending')}
              className={`text-sm font-medium border-b-2 pb-2 transition-colors ${
                activeTab === 'pending'
                  ? 'border-primary text-primary font-semibold'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Pending Invitation Codes ({pendingInvites.length})
            </button>
          </div>
        </CardHeader>
        <CardContent className="pt-4">
          {isLoading ? (
            <div className="flex items-center justify-center p-8 text-muted-foreground">
              <RefreshCw className="size-5 animate-spin mr-2" /> Loading invitations...
            </div>
          ) : pendingInvites.length === 0 ? (
            <div className="text-center p-8 text-muted-foreground text-sm">
              No active pending invitation codes found. Use the generator above to create one.
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              {pendingInvites.map((inv) => (
                <div
                  key={inv.invitationId}
                  className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 p-3.5 rounded-lg border border-border bg-card"
                >
                  <div className="flex flex-col gap-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium text-foreground truncate">
                        {inv.recipientEmail ?? 'Standalone Invitation Code'}
                      </span>
                      <Badge variant="secondary" className="text-[10px]">
                        {ROLE_LABEL[inv.boutiqueRole] ?? inv.boutiqueRole}
                      </Badge>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      Created {new Date(inv.createdAt).toLocaleDateString()} · Expires{' '}
                      {new Date(inv.expiresAt).toLocaleString()}
                    </span>
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => void handleRevoke(inv.invitationId)}
                    >
                      <Trash2 className="size-3.5 text-destructive mr-1" /> Revoke
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* QR Code Viewer Modal */}
      {activeQrCode && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <div className="bg-background border border-border rounded-xl p-6 max-w-sm w-full flex flex-col items-center gap-4 text-center shadow-xl">
            <h3 className="font-serif text-lg font-medium">Invitation QR Code</h3>
            <p className="text-xs text-muted-foreground">
              Scan with mobile device camera to open invitation link directly.
            </p>
            <div className="p-3 bg-white rounded-lg border border-border">
              <QrCodeSvg value={activeQrCode.link} size={180} />
            </div>
            <code className="text-sm font-mono font-bold tracking-widest px-3 py-1 bg-muted rounded">
              {activeQrCode.code}
            </code>
            <Button className="w-full mt-2" variant="outline" onClick={() => setActiveQrCode(null)}>
              Close
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
