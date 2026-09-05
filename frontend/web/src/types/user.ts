export type ContactPreferences = 'Email' | 'Phone' | 'SMS' | 'WhatsApp' | 'None'

export type AccountState = 'OnboardingPending' | 'Active' | 'Suspended'

export interface UserDto {
  id: string
  clerkId: string
  email: string
  firstName: string
  lastName: string
  displayName?: string | null
  username: string
  phoneNumber?: string | null
  address?: string | null
  profileImageUrl?: string | null
  userRole: string
  organizationRole: string
  organizationId: string
  hasCompletedOnboarding: boolean
  accountState: AccountState
  contactPreference: ContactPreferences
  pushNotificationsEnabled: boolean
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CompleteOnboardingRequest {
  displayName: string
  phoneNumber: string
  address: string
  profileImageUrl?: string | null
  contactPreference?: ContactPreferences
  pushNotificationsEnabled?: boolean
}
