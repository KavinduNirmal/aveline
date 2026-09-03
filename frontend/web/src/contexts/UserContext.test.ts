import { describe, expect, it } from 'vitest'
import type { CompleteOnboardingRequest, UserDto } from '@/types/user'

describe('UserContext and Onboarding Data Types', () => {
  it('creates valid complete onboarding request payload', () => {
    const payload: CompleteOnboardingRequest = {
      displayName: 'Kasun Delpachithra',
      phoneNumber: '+94771234567',
      address: '15 Alfred House Gardens, Colombo 03',
      profileImageUrl: 'https://img.clerk.com/avatar.png',
      contactPreference: 'WhatsApp',
      pushNotificationsEnabled: true,
    }

    expect(payload.displayName).toBe('Kasun Delpachithra')
    expect(payload.phoneNumber).toBe('+94771234567')
    expect(payload.contactPreference).toBe('WhatsApp')
    expect(payload.pushNotificationsEnabled).toBe(true)
  })

  it('correctly maps UserDto properties', () => {
    const userDto: UserDto = {
      id: 'usr-123',
      clerkId: 'user_clerk_123',
      email: 'kasun@aveline.lk',
      firstName: 'Kasun',
      lastName: 'Delpachithra',
      displayName: 'Kasun Delpachithra',
      username: 'kasun_d',
      phoneNumber: '+94771234567',
      address: 'Colombo 03',
      profileImageUrl: 'https://img.clerk.com/avatar.png',
      userRole: 'owner',
      organizationRole: 'org:admin',
      organizationId: 'org_123',
      hasCompletedOnboarding: true,
      contactPreference: 'WhatsApp',
      pushNotificationsEnabled: true,
      isActive: true,
      createdAt: '2026-09-03T10:00:00Z',
      updatedAt: '2026-09-03T10:00:00Z',
    }

    expect(userDto.hasCompletedOnboarding).toBe(true)
    expect(userDto.organizationRole).toBe('org:admin')
    expect(userDto.userRole).toBe('owner')
  })
})
