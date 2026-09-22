import { useCallback, useEffect, useState } from 'react'
import { RefreshCw, Save } from 'lucide-react'
import { toast } from 'sonner'

import { EntitlementsTable } from '@/components/dashboard/billing/EntitlementsTable'
import { ApiKeysPanel } from '@/components/dashboard/settings/ApiKeysPanel'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { toApiError } from '@/lib/api-error'
import { hasPermission } from '@/lib/permissions'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import {
  fetchSettings,
  updateSettings,
  type SettingsResponse,
  type UpdateSettingsPayload,
} from '@/lib/settings-api'
import type { OrganizationProfileDto } from '@/types/organization'

interface SettingsPanelProps {
  organization: OrganizationProfileDto
  role: string
}

/** The editable profile fields, in the order they appear. */
const PROFILE_FIELDS: ReadonlyArray<{
  key: keyof UpdateSettingsPayload
  label: string
  placeholder?: string
}> = [
  { key: 'name', label: 'Boutique name' },
  { key: 'contactEmail', label: 'Contact email', placeholder: 'hello@boutique.lk' },
  { key: 'billingEmail', label: 'Billing email', placeholder: 'accounts@boutique.lk' },
  { key: 'phoneNumber', label: 'Phone', placeholder: '+94 77 000 0000' },
  { key: 'address', label: 'Address' },
  { key: 'currency', label: 'Currency', placeholder: 'LKR' },
  { key: 'timeZone', label: 'Time zone', placeholder: 'Asia/Colombo' },
]

/**
 * The Settings section: the boutique profile, its resolved entitlements, and API keys.
 *
 * **Integrations is not here, on purpose.** The WhatsApp and payment-gateway credentials stay on
 * `settings:manage` behind their own section; TD5.5 chose `team:manage` for staff management
 * precisely so a manager never reaches them, and folding Integrations into Settings would undo that.
 */
export function SettingsPanel({ organization, role }: SettingsPanelProps) {
  const canViewKeys = hasPermission(role, 'apikeys:view')
  const canManageKeys = hasPermission(role, 'apikeys:manage')

  const [data, setData] = useState<SettingsResponse | null>(null)
  const [draft, setDraft] = useState<UpdateSettingsPayload>({})
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  const load = usePanelLoad(
    (signal?: AbortSignal) => fetchSettings(organization.id, signal),
    (response) => {
      setData(response)
      setDraft({})
    },
    () => {
      setData(null)
      setError('Could not load the boutique settings.')
    },
    [organization.id],
  )

  const runLoad = useCallback(
    async (signal?: AbortSignal) => {
      setIsLoading(true)
      setError(null)
      await load(signal)
      setIsLoading(false)
    },
    [load],
  )

  useEffect(() => {
    const controller = new AbortController()
    void runLoad(controller.signal)
    return () => controller.abort()
  }, [runLoad])

  const save = async () => {
    setIsSaving(true)
    setSaveError(null)
    try {
      // Only the fields the operator actually touched are sent: the endpoint is a partial update,
      // and sending the whole form back would overwrite a concurrent edit with stale values.
      await updateSettings(organization.id, draft)
      toast.success('Settings saved.')
      await runLoad()
    } catch (caught) {
      setSaveError(toApiError(caught).message)
    } finally {
      setIsSaving(false)
    }
  }

  const valueOf = (key: keyof UpdateSettingsPayload): string => {
    const draftValue = draft[key]
    if (draftValue !== undefined) return String(draftValue)
    const source = data?.settings[key]
    return source === null || source === undefined ? '' : String(source)
  }

  if (isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <Skeleton className="h-64 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          {organization.name}
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Settings</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
          The boutique profile clients see, the limits the plan grants, and the machine credentials
          for this shop.
        </p>
      </div>

      {error ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3 pt-6">
            <p className="text-sm text-destructive">{error}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
              Try again
            </Button>
          </CardContent>
        </Card>
      ) : data ? (
        <>
          <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
            <CardHeader>
              <CardTitle className="font-serif text-lg font-medium">Boutique profile</CardTitle>
              <CardDescription>
                Only the fields you change are sent, so a concurrent edit is not overwritten with
                stale values.
              </CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <div className="grid gap-4 sm:grid-cols-2">
                {PROFILE_FIELDS.map((field) => (
                  <div key={field.key} className="flex flex-col gap-1.5">
                    <Label htmlFor={`setting-${field.key}`}>{field.label}</Label>
                    <Input
                      id={`setting-${field.key}`}
                      value={valueOf(field.key)}
                      placeholder={field.placeholder}
                      onChange={(event) =>
                        setDraft((current) => ({ ...current, [field.key]: event.target.value }))
                      }
                    />
                  </div>
                ))}
              </div>

              {saveError ? <p className="text-sm text-destructive">{saveError}</p> : null}

              <div className="flex items-center gap-3">
                <Button
                  type="button"
                  disabled={isSaving || Object.keys(draft).length === 0}
                  onClick={() => void save()}
                >
                  {isSaving ? (
                    <RefreshCw className="size-4 animate-spin" aria-hidden />
                  ) : (
                    <Save className="size-4" aria-hidden />
                  )}
                  Save changes
                </Button>
                {Object.keys(draft).length === 0 ? (
                  <span className="text-xs text-muted-foreground">No changes yet.</span>
                ) : null}
              </div>
            </CardContent>
          </Card>

          <EntitlementsTable items={data.entitlements} />

          {canViewKeys ? (
            <ApiKeysPanel organizationId={organization.id} canManage={canManageKeys} />
          ) : null}
        </>
      ) : null}
    </div>
  )
}
