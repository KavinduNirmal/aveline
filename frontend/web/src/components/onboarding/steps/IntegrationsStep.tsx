import { ArrowRight, CheckCircle2, Link2, MessageCircle, CreditCard, Camera, Loader2, Trash2 } from 'lucide-react'
import { useState } from 'react'
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

import { deleteIntegration, saveIntegration } from '@/lib/integrations'
import type { IntegrationType } from '@/types/integration'
import { useOwnerOnboardingWizard } from '../wizard-context'

interface IntegrationField {
  key: string
  label: string
  placeholder: string
}

interface IntegrationMeta {
  type: IntegrationType
  name: string
  description: string
  icon: typeof Link2
  fields: IntegrationField[]
}

const INTEGRATIONS: IntegrationMeta[] = [
  {
    type: 'WhatsApp',
    name: 'WhatsApp Business',
    description: 'Let Aveline message customers on your WhatsApp Business number.',
    icon: MessageCircle,
    fields: [
      {
        key: 'accessToken',
        label: 'Access Token',
        placeholder: 'EAAG… permanent token from Meta',
      },
    ],
  },
  {
    type: 'Instagram',
    name: 'Instagram',
    description: 'Connect your Meta/Instagram business account for social commerce.',
    icon: Camera,
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
    icon: CreditCard,
    fields: [
      { key: 'secretKey', label: 'Secret Key', placeholder: 'sk_live_…' },
      { key: 'publishableKey', label: 'Publishable Key (optional)', placeholder: 'pk_live_…' },
    ],
  },
]

function IntegrationCard({ meta }: { meta: IntegrationMeta }) {
  const { draft } = useOwnerOnboardingWizard()
  const organizationId = draft.organizationId
  const [values, setValues] = useState<Record<string, string>>({})
  const [connected, setConnected] = useState(false)
  const [busy, setBusy] = useState(false)
  const Icon = meta.icon

  const canSave =
    organizationId !== '' &&
    meta.fields.some((f) => (values[f.key] ?? '').trim().length > 0)

  const handleConnect = async () => {
    if (!organizationId) return
    setBusy(true)
    try {
      await saveIntegration(organizationId, meta.type, {
        credentials: values,
      })
      setConnected(true)
      setValues({})
      toast.success(`${meta.name} connected securely. Your credentials are encrypted.`)
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : `Unable to connect ${meta.name}.`)
    } finally {
      setBusy(false)
    }
  }

  const handleDisconnect = async () => {
    if (!organizationId) return
    setBusy(true)
    try {
      await deleteIntegration(organizationId, meta.type)
      setConnected(false)
      toast.info(`${meta.name} disconnected.`)
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : `Unable to disconnect ${meta.name}.`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card className="overflow-hidden">
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
        <Badge variant={connected ? 'default' : 'outline'} className="shrink-0">
          {connected ? 'Connected' : 'Not connected'}
        </Badge>
      </CardHeader>

      <CardContent className="flex flex-col gap-3">
        {connected ? (
          <div className="flex items-center gap-2 text-xs text-emerald-700 dark:text-emerald-300">
            <CheckCircle2 className="size-4 shrink-0" />
            <span>Stored securely with AES-256 encryption.</span>
          </div>
        ) : (
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
        )}
      </CardContent>

      <CardFooter className="justify-end gap-2 pt-0">
        {connected ? (
          <Button variant="ghost" size="sm" onClick={() => void handleDisconnect()} disabled={busy}>
            {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Trash2 className="size-3.5" data-icon="inline-start" />}
            Disconnect
          </Button>
        ) : (
          <Button size="sm" onClick={() => void handleConnect()} disabled={!canSave || busy}>
            {busy ? <Loader2 className="size-3.5 animate-spin" data-icon="inline-start" /> : <Link2 className="size-3.5" data-icon="inline-start" />}
            Connect
          </Button>
        )}
      </CardFooter>
    </Card>
  )
}

export function IntegrationsStep() {
  const { goTo } = useOwnerOnboardingWizard()

  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-serif text-xl font-medium">Connect Your Channels</CardTitle>
        <CardDescription>
          Optionally connect WhatsApp, Instagram, and your payment gateway. Credentials are
          encrypted and scoped to your boutique only. You can always do this later from Settings.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {INTEGRATIONS.map((meta) => (
          <IntegrationCard key={meta.type} meta={meta} />
        ))}
      </CardContent>
      <CardFooter className="flex justify-between gap-3">
        <Button variant="outline" onClick={() => goTo(5)}>
          Back
        </Button>
        <div className="flex items-center gap-2">
          <Button variant="ghost" onClick={() => goTo(7)}>
            Skip for now
          </Button>
          <Button onClick={() => goTo(7)}>
            Continue to Team <ArrowRight className="size-4" data-icon="inline-end" />
          </Button>
        </div>
      </CardFooter>
    </Card>
  )
}
