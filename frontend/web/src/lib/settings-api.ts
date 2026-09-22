import { apiClient } from '@/lib/api'
import type { EntitlementItem } from '@/lib/billing-api'

/**
 * The tenant Settings surface: the boutique profile, the resolved settings block, and API keys
 * (docs/api/README.md C.5, B.9).
 *
 * **Integrations is deliberately not part of this module.** It stays on `settings:manage` (owner
 * only) while Settings is also reachable by a manager through `team:manage`-adjacent flows; keeping
 * the WhatsApp and payment-gateway credentials out of the manager's reach is the whole reason TD5.5
 * chose a new permission rather than widening `settings:manage`.
 */

export interface OrganizationSettings {
  id: string
  name: string
  slug: string
  address: string | null
  phoneNumber: string | null
  description: string | null
  logoUrl: string | null
  brandVoice: string | null
  businessRules: string | null
  preferredColorsFabrics: string | null
  customerPreferences: string | null
  billingEmail: string | null
  contactEmail: string | null
  currency: string
  timeZone: string
  isActive: boolean
  suspendedAt: string | null
}

export interface SettingsResponse {
  settings: OrganizationSettings
  entitlements: EntitlementItem[]
}

export interface UpdateSettingsPayload {
  name?: string
  slug?: string
  address?: string
  phoneNumber?: string
  description?: string
  logoUrl?: string
  brandVoice?: string
  businessRules?: string
  preferredColorsFabrics?: string
  customerPreferences?: string
  billingEmail?: string
  contactEmail?: string
  currency?: string
  timeZone?: string
}

export interface ApiKey {
  id: string
  name: string
  prefix: string
  scopes: string[]
  environment: string
  status: string
  createdAt: string
  expiresAt: string | null
  lastUsedAt: string | null
  revokedAt: string | null
  revokedReason: string | null
  requestCount: number
}

export interface CreateApiKeyResponse {
  key: ApiKey
  /** Present exactly once; the server never returns it again. */
  secret: string
}

export interface CreateApiKeyPayload {
  name: string
  scopes: string[]
  environment?: string
  expiresAt?: string
}

const orgBase = (organizationId: string) => `/api/v1/orgs/${organizationId}`

export async function fetchSettings(
  organizationId: string,
  signal?: AbortSignal,
): Promise<SettingsResponse> {
  const response = await apiClient.get<SettingsResponse>(`${orgBase(organizationId)}/settings`, {
    signal,
  })
  return response.data
}

export async function updateSettings(
  organizationId: string,
  payload: UpdateSettingsPayload,
  signal?: AbortSignal,
): Promise<OrganizationSettings> {
  const response = await apiClient.patch<OrganizationSettings>(
    orgBase(organizationId),
    payload,
    { signal },
  )
  return response.data
}

export async function fetchApiKeys(
  organizationId: string,
  signal?: AbortSignal,
): Promise<ApiKey[]> {
  const response = await apiClient.get<ApiKey[]>(`${orgBase(organizationId)}/api-keys`, { signal })
  return response.data
}

export async function createApiKey(
  organizationId: string,
  payload: CreateApiKeyPayload,
  signal?: AbortSignal,
): Promise<CreateApiKeyResponse> {
  const response = await apiClient.post<CreateApiKeyResponse>(
    `${orgBase(organizationId)}/api-keys`,
    payload,
    { signal },
  )
  return response.data
}

export async function revokeApiKey(
  organizationId: string,
  keyId: string,
  reason?: string,
  signal?: AbortSignal,
): Promise<ApiKey> {
  const response = await apiClient.post<ApiKey>(
    `${orgBase(organizationId)}/api-keys/${keyId}/revoke`,
    reason ? { reason } : {},
    { signal },
  )
  return response.data
}

export async function deleteApiKey(
  organizationId: string,
  keyId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`${orgBase(organizationId)}/api-keys/${keyId}`, { signal })
}
