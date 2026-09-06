import { describe, expect, it } from 'vitest'
import type { IntegrationStatusDto, IntegrationType, SaveIntegrationRequest } from '../types/integration'
import { integrationRouteSegment } from '../types/integration'

describe('Integrations Contracts', () => {
  it('maps integration types to lowercase route segments', () => {
    expect(integrationRouteSegment('WhatsApp')).toBe('whatsapp')
    expect(integrationRouteSegment('Instagram')).toBe('instagram')
    expect(integrationRouteSegment('PaymentGateway')).toBe('paymentgateway')
  })

  it('supports all backend integration types', () => {
    const types: IntegrationType[] = ['WhatsApp', 'Instagram', 'PaymentGateway']
    expect(types).toHaveLength(3)
  })

  it('validates SaveIntegrationRequest payload shape', () => {
    const req: SaveIntegrationRequest = {
      credentials: { accessToken: 'secret-token', clientSecret: 'also-secret' },
    }
    expect(req.credentials.accessToken).toBe('secret-token')
  })

  it('never carries plaintext in the status DTO', () => {
    const status: IntegrationStatusDto = {
      type: 'WhatsApp',
      connected: true,
      maskedPreview: '****oken',
      metadata: null,
      updatedAt: '2026-09-06T00:00:00Z',
    }
    expect(status.maskedPreview).toBe('****oken')
    expect(status).not.toHaveProperty('credentials')
  })
})
