import {
  AlertCircle,
  ArrowDownLeft,
  BookOpen,
  CheckCircle2,
  Clock,
  Info,
  Link2,
  Loader2,
  MessageCircle,
  Plug,
  RefreshCw,
  ShieldCheck,
  Unplug,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

import { InstagramIcon, PaymentIcon, WhatsAppIcon } from '@/components/dashboard/BrandIcons'
import {
  deleteIntegration,
  listIntegrationMessages,
  listIntegrations,
  saveIntegration,
  testIntegration,
} from '@/lib/integrations'
import type {
  InboundMessageLogDto,
  IntegrationStatus,
  IntegrationStatusDto,
  IntegrationType,
} from '@/types/integration'
import type { OrganizationProfileDto } from '@/types/organization'

interface IntegrationsPanelProps {
  organization: OrganizationProfileDto
}

interface IntegrationField {
  key: string
  label: string
  placeholder: string
}

interface IntegrationMeta {
  type: IntegrationType
  name: string
  description: string
  help: string
  icon: (props: { className?: string }) => React.ReactNode
  fields: IntegrationField[]
}

const INTEGRATIONS: IntegrationMeta[] = [
  {
    type: 'WhatsApp',
    name: 'WhatsApp Business',
    description: 'Message customers and receive inbound messages on your WhatsApp Business number.',
    help: 'Uses your own Meta WhatsApp Business credentials. After connecting, point Meta at Aveline\u2019s webhook so inbound messages flow in. Meta may charge per message.',
    icon: WhatsAppIcon,
    fields: [
      { key: 'accessToken', label: 'Access Token', placeholder: 'EAAG… permanent token from Meta' },
      { key: 'phoneNumberId', label: 'Phone Number ID', placeholder: 'e.g. 123456789012345' },
      { key: 'appSecret', label: 'App Secret', placeholder: 'Meta app secret (webhook signing)' },
      { key: 'webhookVerifyToken', label: 'Webhook Verify Token', placeholder: 'A token you set in Meta' },
    ],
  },
  {
    type: 'Instagram',
    name: 'Instagram',
    description: 'Connect your Meta/Instagram business account for social commerce.',
    help: 'Uses your own Meta/Instagram business credentials. Instagram messaging is wired in a later release.',
    icon: InstagramIcon,
    fields: [
      { key: 'clientId', label: 'App ID / Client ID', placeholder: 'Instagram App ID' },
      { key: 'clientSecret', label: 'Client Secret', placeholder: 'Meta app secret' },
      { key: 'accessToken', label: 'Access Token', placeholder: 'Long-lived Instagram token' },
    ],
  },
  {
    type: 'PaymentGateway',
    name: 'Payment Gateway',
    description: 'Accept customer payments securely through your boutique gateway.',
    help: 'Uses your own payment gateway credentials. Live payment processing is wired in a later release. Your gateway may charge transaction fees.',
    icon: PaymentIcon,
    fields: [
      { key: 'secretKey', label: 'Secret Key', placeholder: 'sk_live_…' },
      { key: 'publishableKey', label: 'Publishable Key (optional)', placeholder: 'pk_live_…' },
    ],
  },
]

const STATUS_META: Record<
  IntegrationStatus,
  { label: string; variant: 'default' | 'outline' | 'secondary' | 'destructive' }
> = {
  Connected: { label: 'Connected', variant: 'default' },
  Pending: { label: 'Pending', variant: 'secondary' },
  Error: { label: 'Error', variant: 'destructive' },
  Expired: { label: 'Expired', variant: 'destructive' },
  Disconnected: { label: 'Not connected', variant: 'outline' },
}

function formatDate(value: string | null): string {
  if (!value) return 'Never'
  return new Date(value).toLocaleString()
}

function formatTime(value: string): string {
  return new Date(value).toLocaleString()
}

function maskPhone(value: string | null): string {
  if (!value) return 'Unknown'
  return value.length > 8 ? `${value.slice(0, 3)}••••${value.slice(-4)}` : value
}

function IntegrationCard({
  meta,
  status,
  onConnect,
  onTest,
  onDisconnect,
  busy,
}: {
  meta: IntegrationMeta
  status: IntegrationStatusDto | null
  onConnect: () => void
  onTest: () => void
  onDisconnect: () => void
  busy: boolean
}) {
  const Icon = meta.icon
  const connected = status?.status === 'Connected'
  const statusMeta = status ? STATUS_META[status.status] : STATUS_META.Disconnected

  return (
    <Card className="flex h-full flex-col overflow-hidden">
      <CardHeader className="pb-3 flex-row items-start justify-between gap-3 space-y-0">
        <div className="flex items-start gap-3">
          <div className="size-10 rounded-full bg-primary/10 text-primary flex items-center justify-center shrink-0">
            <Icon className="size-5" aria-hidden />
          </div>
          <div>
            <CardTitle className="font-serif text-base font-medium">{meta.name}</CardTitle>
            <CardDescription className="text-xs">{meta.description}</CardDescription>
          </div>
        </div>
        <div className="flex items-center gap-1.5 shrink-0">
          <Tooltip>
            <TooltipTrigger asChild>
              <button
                type="button"
                aria-label={`About ${meta.name}`}
                className="inline-flex size-6 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <Info className="size-3.5" aria-hidden />
              </button>
            </TooltipTrigger>
            <TooltipContent side="top" align="end" className="max-w-xs">
              {meta.help}
            </TooltipContent>
          </Tooltip>
          <Badge variant={statusMeta.variant}>{statusMeta.label}</Badge>
        </div>
      </CardHeader>

      <CardContent className="flex flex-1 flex-col gap-3">
        {connected ? (
          <div className="flex flex-col gap-1.5 rounded-lg border border-border bg-muted/40 p-3 text-xs">
            <div className="flex items-center gap-2 text-muted-foreground">
              <ShieldCheck className="size-3.5 text-emerald-600" aria-hidden />
              <span>Credentials encrypted with AES-256</span>
            </div>
            <div className="flex items-center gap-2 text-muted-foreground">
              <Clock className="size-3.5" aria-hidden />
              <span>Last connected: {formatDate(status?.lastConnectedAt ?? null)}</span>
            </div>
            {status?.maskedPreview && (
              <div className="flex items-center gap-2 text-muted-foreground">
                <CheckCircle2 className="size-3.5 text-emerald-600" aria-hidden />
                <span>Token: {status.maskedPreview}</span>
              </div>
            )}
          </div>
        ) : (
          <div className="flex items-start gap-2 rounded-lg border border-border bg-muted/40 p-3 text-xs text-muted-foreground">
            <Plug className="size-3.5 mt-0.5 shrink-0 text-muted-foreground" aria-hidden />
            <span>
              {status?.status === 'Error' || status?.status === 'Expired'
                ? status.lastError ?? 'This connection needs attention.'
                : 'Not connected yet. Connect to enable messaging.'}
            </span>
          </div>
        )}
      </CardContent>

      <div className="mt-auto flex items-center justify-end gap-2 border-t px-6 py-3">
        {connected ? (
          <>
            <Button variant="outline" size="sm" onClick={onTest} disabled={busy}>
              {busy ? <Loader2 className="size-3.5 animate-spin" /> : <RefreshCw className="size-3.5" />}
              Test
            </Button>
            <Button variant="ghost" size="sm" onClick={onDisconnect} disabled={busy}>
              {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Unplug className="size-3.5" />}
              Disconnect
            </Button>
          </>
        ) : (
          <Button size="sm" onClick={onConnect} disabled={busy}>
            {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Link2 className="size-3.5" />}
            Connect
          </Button>
        )}
      </div>
    </Card>
  )
}

function ConnectDialog({
  meta,
  organization,
  open,
  onOpenChange,
  onConnected,
}: {
  meta: IntegrationMeta
  organization: OrganizationProfileDto
  open: boolean
  onOpenChange: (open: boolean) => void
  onConnected: (status: IntegrationStatusDto) => void
}) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open) setValues({})
  }, [open])

  const canSave = meta.fields.some((f) => (values[f.key] ?? '').trim().length > 0)

  const handleConnect = async () => {
    setBusy(true)
    try {
      const updated = await saveIntegration(organization.id, meta.type, { credentials: values })
      onConnected(updated)
      onOpenChange(false)
      if (updated.status === 'Connected') {
        toast.success(`${meta.name} connected successfully.`)
      } else if (updated.status === 'Error') {
        toast.error(updated.lastError ?? `Unable to connect ${meta.name}.`)
      } else {
        toast.success(`${meta.name} credentials saved.`)
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : `Unable to connect ${meta.name}.`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="font-serif text-lg font-medium">Connect {meta.name}</DialogTitle>
          <DialogDescription>
            Enter your credentials. They are encrypted and scoped to {organization.name} only.
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-3">
          {meta.fields.map((field) => (
            <div key={field.key} className="flex flex-col gap-1.5">
              <Label htmlFor={`${meta.type}-${field.key}`} className="text-xs text-muted-foreground">
                {field.label}
              </Label>
              <Input
                id={`${meta.type}-${field.key}`}
                type="password"
                value={values[field.key] ?? ''}
                onChange={(e) => setValues((prev) => ({ ...prev, [field.key]: e.target.value }))}
                placeholder={field.placeholder}
                autoComplete="off"
              />
            </div>
          ))}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={() => void handleConnect()} disabled={!canSave || busy}>
            {busy ? <Loader2 className="size-4 animate-spin" /> : <Link2 className="size-4" />}
            Test &amp; Connect
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DisconnectDialog({
  meta,
  open,
  onOpenChange,
  onDisconnected,
}: {
  meta: IntegrationMeta
  open: boolean
  onOpenChange: (open: boolean) => void
  onDisconnected: () => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="font-serif text-lg font-medium">Disconnect {meta.name}?</DialogTitle>
          <DialogDescription>
            This removes the stored credentials. Inbound messages will stop flowing until you
            reconnect.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={onDisconnected}>
            <Unplug className="size-4" />
            Disconnect
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ActivityLog({ logs, loading }: { logs: InboundMessageLogDto[]; loading: boolean }) {
  if (loading) {
    return (
      <div className="flex flex-col gap-3">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-14 w-full" />
        ))}
      </div>
    )
  }

  if (logs.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 py-8 text-center text-sm text-muted-foreground">
        <MessageCircle className="size-6 text-muted-foreground/50" aria-hidden />
        <p>No inbound messages yet.</p>
        <p className="text-xs">Once a customer messages you on WhatsApp, activity appears here.</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      {logs.map((log) => (
        <div
          key={log.id}
          className="flex items-start gap-3 rounded-lg border border-border bg-card p-3"
        >
          <div className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
            <ArrowDownLeft className="size-4" aria-hidden />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm font-medium text-foreground">
                {maskPhone(log.from)}
              </span>
              <span className="shrink-0 text-xs text-muted-foreground">
                {formatTime(log.receivedAt)}
              </span>
            </div>
            <p className="mt-0.5 truncate text-sm text-muted-foreground">{log.content}</p>
          </div>
        </div>
      ))}
    </div>
  )
}

export function IntegrationsPanel({ organization }: IntegrationsPanelProps) {
  const [statuses, setStatuses] = useState<IntegrationStatusDto[]>([])
  const [logs, setLogs] = useState<InboundMessageLogDto[]>([])
  const [loading, setLoading] = useState(true)
  const [busyType, setBusyType] = useState<IntegrationType | null>(null)
  const [connectFor, setConnectFor] = useState<IntegrationMeta | null>(null)
  const [disconnectFor, setDisconnectFor] = useState<IntegrationMeta | null>(null)

  const load = useCallback(async () => {
    try {
      const [statusData, logData] = await Promise.all([
        listIntegrations(organization.id),
        listIntegrationMessages(organization.id),
      ])
      setStatuses(statusData)
      setLogs(logData)
    } catch {
      setStatuses([])
      setLogs([])
    } finally {
      setLoading(false)
    }
  }, [organization.id])

  useEffect(() => {
    void load()
  }, [load])

  const statusFor = (type: IntegrationType) =>
    statuses.find((s) => s.type === type) ?? null

  const stats = useMemo(() => {
    const connected = statuses.filter((s) => s.status === 'Connected').length
    const attention = statuses.filter(
      (s) => s.status === 'Error' || s.status === 'Expired',
    ).length
    const today = logs.filter(
      (l) => new Date(l.receivedAt).toDateString() === new Date().toDateString(),
    ).length
    return { connected, attention, today }
  }, [statuses, logs])

  const handleConnected = (updated: IntegrationStatusDto) => {
    setStatuses((prev) => {
      const rest = prev.filter((s) => s.type !== updated.type)
      return [...rest, updated]
    })
  }

  const handleTest = async (meta: IntegrationMeta) => {
    setBusyType(meta.type)
    try {
      const updated = await testIntegration(organization.id, meta.type)
      handleConnected(updated)
      if (updated.status === 'Connected') {
        toast.success(`${meta.name} connection is healthy.`)
      } else {
        toast.error(updated.lastError ?? `${meta.name} connection test failed.`)
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : `Unable to test ${meta.name}.`)
    } finally {
      setBusyType(null)
    }
  }

  const handleDisconnect = async (meta: IntegrationMeta) => {
    setBusyType(meta.type)
    try {
      await deleteIntegration(organization.id, meta.type)
      setStatuses((prev) => prev.filter((s) => s.type !== meta.type))
      setDisconnectFor(null)
      toast.info(`${meta.name} disconnected.`)
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : `Unable to disconnect ${meta.name}.`)
    } finally {
      setBusyType(null)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="font-serif text-2xl font-medium tracking-tight">Integrations</h1>
          <p className="text-sm text-muted-foreground">
            Connect your boutique&apos;s WhatsApp, Instagram, and payment gateway. Credentials are
            encrypted and scoped to <span className="font-medium text-foreground">{organization.name}</span> only.
          </p>
        </div>
        <Button variant="outline" size="sm" asChild className="shrink-0 self-start">
          <Link to="/docs/integrations">
            <BookOpen className="size-4" aria-hidden />
            Integration guide
          </Link>
        </Button>
      </div>

      {/* Third-party cost notice */}
      <div className="flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-muted-foreground">
        <AlertCircle className="size-4 shrink-0 text-amber-600" aria-hidden />
        <p>
          Connecting an integration may incur fees charged by the third-party provider (e.g. Meta
          WhatsApp messaging rates or payment-gateway transaction fees).{' '}
          <span className="font-medium text-foreground">
            Aveline is not responsible for any third-party API or messaging costs — these are
            solely your responsibility.
          </span>
        </p>
      </div>

      {/* Stats row */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Card className="p-4 flex items-center gap-4">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-emerald-500/10 text-emerald-600">
            <CheckCircle2 className="size-5" aria-hidden />
          </div>
          <div>
            <p className="text-xs text-muted-foreground font-medium">Connected</p>
            <p className="text-lg font-semibold">{loading ? '—' : `${stats.connected} of ${INTEGRATIONS.length}`}</p>
          </div>
        </Card>

        <Card className="p-4 flex items-center gap-4">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-rose-500/10 text-rose-600">
            <AlertCircle className="size-5" aria-hidden />
          </div>
          <div>
            <p className="text-xs text-muted-foreground font-medium">Needs attention</p>
            <p className="text-lg font-semibold">{loading ? '—' : stats.attention}</p>
          </div>
        </Card>

        <Card className="p-4 flex items-center gap-4">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <MessageCircle className="size-5" aria-hidden />
          </div>
          <div>
            <p className="text-xs text-muted-foreground font-medium">Messages today</p>
            <p className="text-lg font-semibold">{loading ? '—' : stats.today}</p>
          </div>
        </Card>
      </div>

      {/* Integration cards (no inline forms) */}
      <div className="grid grid-cols-1 items-stretch gap-4 md:grid-cols-3">
        {INTEGRATIONS.map((meta) => (
          <IntegrationCard
            key={meta.type}
            meta={meta}
            status={statusFor(meta.type)}
            busy={busyType === meta.type}
            onConnect={() => setConnectFor(meta)}
            onTest={() => void handleTest(meta)}
            onDisconnect={() => setDisconnectFor(meta)}
          />
        ))}
      </div>

      {/* Activity log */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="font-serif text-lg font-medium flex items-center gap-2">
            <MessageCircle className="size-5 text-primary" aria-hidden /> Recent activity
          </CardTitle>
          <CardDescription>Latest inbound messages received from customers.</CardDescription>
        </CardHeader>
        <CardContent>
          <ActivityLog logs={logs} loading={loading} />
        </CardContent>
      </Card>

      {/* Connect dialog */}
      {connectFor && (
        <ConnectDialog
          meta={connectFor}
          organization={organization}
          open
          onOpenChange={(open) => {
            if (!open) setConnectFor(null)
          }}
          onConnected={handleConnected}
        />
      )}

      {/* Disconnect confirm dialog */}
      {disconnectFor && (
        <DisconnectDialog
          meta={disconnectFor}
          open
          onOpenChange={(open) => {
            if (!open) setDisconnectFor(null)
          }}
          onDisconnected={() => void handleDisconnect(disconnectFor)}
        />
      )}
    </div>
  )
}
