import { apiClient } from '@/lib/api'
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
