import { apiClient } from '@/lib/api'
import type { IntegrationStatusDto, IntegrationType, SaveIntegrationRequest } from '@/types/integration'

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
 * Saves (upserts) credentials for a single integration type.
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
 * Deletes the stored credentials for a single integration type.
 */
export async function deleteIntegration(
  organizationId: string,
  type: IntegrationType,
): Promise<void> {
  await apiClient.delete(`${integrationBase(organizationId)}/${type.toLowerCase()}`)
}
