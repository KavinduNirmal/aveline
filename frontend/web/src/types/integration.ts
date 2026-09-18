/** Third-party integration types a boutique can connect (mirrors the backend enum). */
export type IntegrationType = 'WhatsApp' | 'Instagram' | 'PaymentGateway'

/** Lifecycle state of an integration (mirrors the backend enum). */
export type IntegrationStatus = 'Pending' | 'Connected' | 'Error' | 'Expired' | 'Disconnected'

/** Safe, non-secret status of a configured integration. */
export interface IntegrationStatusDto {
  type: IntegrationType
  status: IntegrationStatus
  connected: boolean
  maskedPreview: string | null
  metadata: string | null
  lastConnectedAt: string | null
  lastError: string | null
  updatedAt: string
}

/** Secrets to store for an integration (never returned in status responses). */
export interface SaveIntegrationRequest {
  credentials: Record<string, string>
  metadata?: string
}

/** Safe view of a single inbound/outbound message log entry. */
export interface InboundMessageLogDto {
  id: string
  channel: string
  direction: 'inbound' | 'outbound'
  externalId: string | null
  from: string | null
  to: string | null
  content: string | null
  receivedAt: string
}

/** Lowercase route segment for an integration type (used in the API path). */
export function integrationRouteSegment(type: IntegrationType): string {
  return type.toLowerCase()
}
