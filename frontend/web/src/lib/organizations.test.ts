import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type {
  OrganizationProfileWithMembershipDto,
  OrganizationUsageSummary,
} from '../types/organization'

const getMock = vi.fn()
const postMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
  },
}))

import {
  createOrganization,
  fetchMyOrganizations,
  fetchOrganizationBySlug,
  fetchOrganizationUsage,
} from './organizations'

describe('organizations client', () => {
  beforeEach(() => {
    getMock.mockReset()
    postMock.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('fetchOrganizationBySlug requests the encoded slug path', async () => {
    const payload: OrganizationProfileWithMembershipDto = {
      organization: {
        id: 'org-1',
        name: 'House of Fashions',
        slug: 'house-of-fashions',
        clerkOrgId: null,
        ownerUserId: 'u-1',
        address: null,
        phoneNumber: null,
        description: null,
        logoUrl: null,
        planTier: 'Bloom',
        hasCompletedOnboarding: true,
        createdAt: '2026-09-07T00:00:00Z',
      },
      membership: {
        organizationId: 'org-1',
        organizationName: 'House of Fashions',
        slug: 'house-of-fashions',
        boutiqueRole: 'org:boutique_owner',
        status: 'Active',
      },
    }
    getMock.mockResolvedValue({ data: payload })

    const result = await fetchOrganizationBySlug('house-of-fashions')

    expect(getMock).toHaveBeenCalledWith('/api/v1/orgs/by-slug/house-of-fashions')
    expect(result.organization.slug).toBe('house-of-fashions')
    expect(result.membership?.status).toBe('Active')
  })

  it('fetchOrganizationBySlug URL-encodes slugs', async () => {
    getMock.mockResolvedValue({ data: {} })
    await fetchOrganizationBySlug('with space/and&slash').catch(() => undefined)
    expect(getMock).toHaveBeenCalledWith('/api/v1/orgs/by-slug/with%20space%2Fand%26slash')
  })

  it('fetchOrganizationUsage requests the org usage path', async () => {
    const usage: OrganizationUsageSummary = {
      organizationId: 'org-1',
      periodStart: '2026-09-01T00:00:00Z',
      periodEnd: '2026-10-01T00:00:00Z',
      monthlyBlossomLimit: 750,
      blossomUsed: 120.5,
      blossomRemaining: 629.5,
      status: 'Active',
    }
    getMock.mockResolvedValue({ data: usage })

    const result = await fetchOrganizationUsage('org-1')

    expect(getMock).toHaveBeenCalledWith('/api/v1/orgs/org-1/usage')
    expect(result.blossomRemaining).toBe(629.5)
    expect(result.status).toBe('Active')
  })

  it('fetchMyOrganizations reads the memberships endpoint', async () => {
    getMock.mockResolvedValue({ data: [] })
    await fetchMyOrganizations()
    expect(getMock).toHaveBeenCalledWith('/api/v1/orgs/my')
  })

  it('createOrganization posts to the orgs endpoint', async () => {
    postMock.mockResolvedValue({ data: { organization: {}, accountState: 'Active' } })
    await createOrganization({ name: 'Boutique', slug: 'boutique' })
    expect(postMock).toHaveBeenCalledWith('/api/v1/orgs', {
      name: 'Boutique',
      slug: 'boutique',
    })
  })
})
