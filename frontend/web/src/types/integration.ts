/** Third-party integration types a boutique can connect (mirrors the backend enum). */
export type IntegrationType = 'WhatsApp' | 'Instagram' | 'PaymentGateway'

/** Safe, non-secret status of a configured integration. */
export interface IntegrationStatusDto {
  type: IntegrationType
  connected: boolean
  maskedPreview: string | null
  metadata: string | null
  updatedAt: string
}

/** Secrets to store for an integration (never returned in status responses). */
export interface SaveIntegrationRequest {
  credentials: Record<string, string>
  metadata?: string
}

/** Lowercase route segment for an integration type (used in the API path). */
export function integrationRouteSegment(type: IntegrationType): string {
  return type.toLowerCase()
}
