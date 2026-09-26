import { describe, expect, it } from 'vitest'
import type {
  InboundMessageLogDto,
  IntegrationStatusDto,
  IntegrationType,
  SaveIntegrationRequest,
} from '../types/integration'
import { integrationRouteSegment } from '../types/integration'
import { apiBaseUrl } from './env'
import { suggestWebhookVerifyToken, whatsappWebhookUrl } from './integrations'

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
      status: 'Connected',
      connected: true,
      maskedPreview: '****oken',
      metadata: null,
      lastConnectedAt: '2026-09-06T00:00:00Z',
      lastError: null,
      updatedAt: '2026-09-06T00:00:00Z',
    }
    expect(status.maskedPreview).toBe('****oken')
    expect(status).not.toHaveProperty('credentials')
  })

  it('exposes the lifecycle status fields', () => {
    const status: IntegrationStatusDto = {
      type: 'WhatsApp',
      status: 'Expired',
      connected: false,
      maskedPreview: '****oken',
      metadata: null,
      lastConnectedAt: null,
      lastError: 'Token expired',
      updatedAt: '2026-09-06T00:00:00Z',
    }
    expect(status.status).toBe('Expired')
    expect(status.lastError).toBe('Token expired')
    expect(status.connected).toBe(false)
  })

  it('shapes an inbound message log entry without secrets', () => {
    const log: InboundMessageLogDto = {
      id: 'log-1',
      channel: 'whatsapp',
      direction: 'inbound',
      externalId: 'wamid.ABC',
      from: '+94771234567',
      to: null,
      content: 'Hi, do you have this in red?',
      receivedAt: '2026-09-08T00:00:00Z',
    }
    expect(log.channel).toBe('whatsapp')
    expect(log.direction).toBe('inbound')
    expect(log).not.toHaveProperty('credentials')
  })
})

describe('WhatsApp webhook setup', () => {
  it('points Meta at the org route on the API root group', () => {
    expect(whatsappWebhookUrl('org-1', 'https://api.aveline.lk')).toBe(
      'https://api.aveline.lk/api/v1/webhooks/whatsapp/org-1',
    )
  })

  it('tolerates a trailing slash on the configured base URL', () => {
    expect(whatsappWebhookUrl('org-1', 'https://api.aveline.lk/')).toBe(
      'https://api.aveline.lk/api/v1/webhooks/whatsapp/org-1',
    )
  })

  it('defaults to the configured API base URL', () => {
    expect(whatsappWebhookUrl('org-1')).toBe(
      `${apiBaseUrl}/api/v1/webhooks/whatsapp/org-1`,
    )
  })

  it('suggests an unpadded base64url token Meta can echo back', () => {
    // 24 bytes of randomness is 32 base64 characters, with no `=` to escape in a query string.
    expect(suggestWebhookVerifyToken()).toMatch(/^[A-Za-z0-9_-]{32}$/)
  })

  it('suggests a fresh token every time', () => {
    expect(suggestWebhookVerifyToken()).not.toBe(suggestWebhookVerifyToken())
  })

  it('honours a shorter requested length', () => {
    expect(suggestWebhookVerifyToken(12)).toHaveLength(16)
  })
})
