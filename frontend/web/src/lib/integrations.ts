import { apiClient } from '@/lib/api'
import { apiBaseUrl } from '@/lib/env'
import type {
  InboundMessageLogDto,
  IntegrationStatusDto,
  IntegrationType,
  SaveIntegrationRequest,
} from '@/types/integration'

const integrationBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/integrations`

/**
 * Returns the masked, non-secret status of all configured integrations for an org.
 */
export async function listIntegrations(
  organizationId: string,
): Promise<IntegrationStatusDto[]> {
  const response = await apiClient.get<IntegrationStatusDto[]>(integrationBase(organizationId))
  return response.data
}

/**
 * Returns recent inbound/outbound message activity for an org (for the activity log).
 */
export async function listIntegrationMessages(
  organizationId: string,
): Promise<InboundMessageLogDto[]> {
  const response = await apiClient.get<InboundMessageLogDto[]>(
    `${integrationBase(organizationId)}/messages`,
  )
  return response.data
}

/**
 * Saves (upserts) credentials for a single integration type. For WhatsApp this also
 * auto-connects by validating the credentials against Meta.
 */
export async function saveIntegration(
  organizationId: string,
  type: IntegrationType,
  payload: SaveIntegrationRequest,
): Promise<IntegrationStatusDto> {
  const response = await apiClient.put<IntegrationStatusDto>(
    `${integrationBase(organizationId)}/${type.toLowerCase()}`,
    payload,
  )
  return response.data
}

/**
 * Re-runs a connection test against the stored credentials for an integration type.
 */
export async function testIntegration(
  organizationId: string,
  type: IntegrationType,
): Promise<IntegrationStatusDto> {
  const response = await apiClient.post<IntegrationStatusDto>(
    `${integrationBase(organizationId)}/${type.toLowerCase()}/test`,
  )
  return response.data
}

/**
 * Deletes the stored credentials for a single integration type.
 */
export async function deleteIntegration(
  organizationId: string,
  type: IntegrationType,
): Promise<void> {
  await apiClient.delete(`${integrationBase(organizationId)}/${type.toLowerCase()}`)
}

/**
 * The URL Meta must call for a boutique's inbound WhatsApp events.
 *
 * The webhook is mapped on the API's `/api/v1` group rather than under the org-scoped integration
 * routes, so this is built from the API origin and not from `integrationBase`. `baseUrl` is
 * injectable for tests.
 */
export function whatsappWebhookUrl(
  organizationId: string,
  baseUrl: string = apiBaseUrl,
): string {
  return `${baseUrl.replace(/\/+$/, '')}/api/v1/webhooks/whatsapp/${organizationId}`
}

/** Bytes of randomness behind a suggested verify token. */
const VERIFY_TOKEN_BYTES = 24

/**
 * A verify token the boutique can hand to Meta.
 *
 * Meta only echoes the token back on the subscription handshake, so a random URL-safe string is
 * both sufficient and far less error-prone than one typed by hand: the backend compares it
 * verbatim against the stored `webhookVerifyToken`. Base64url keeps it safe to paste into a form
 * or a query string.
 */
export function suggestWebhookVerifyToken(byteLength: number = VERIFY_TOKEN_BYTES): string {
  const bytes = new Uint8Array(byteLength)
  crypto.getRandomValues(bytes)
  let binary = ''
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')
}
