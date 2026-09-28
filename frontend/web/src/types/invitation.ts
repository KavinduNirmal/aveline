/** Canonical boutique staff roles an owner may invite. */
export type BoutiqueStaffRole = 'org:boutique_supervisor' | 'org:boutique_manager' | 'org:boutique_staff'

export interface CreateInvitationRequest {
  boutiqueRole: BoutiqueStaffRole
  recipientEmail?: string
  validityHours?: number
  sendSummaryToOwner?: boolean
}

export interface BulkCreateInvitationRequest {
  boutiqueRole: BoutiqueStaffRole
  count: number
  validityHours?: number
  sendSummaryToOwner?: boolean
}

export interface CreateInvitationResponse {
  invitationId: string
  code: string
  link: string
  mobileLink?: string
  boutiqueRole: BoutiqueStaffRole
  recipientEmail: string | null
  expiresAt: string
  summaryEmailRequested: boolean
  /**
   * `NotRequested` | `Dispatched` | `NotSent`. Three states rather than a boolean, because
   * "not asked for" and "asked for but not sent" are different facts.
   */
  summaryEmailStatus: string
  summaryEmailNote: string | null
}

export interface BulkCreateInvitationResponse {
  invitations: CreateInvitationResponse[]
  /** What the caller asked for, so a clamp is visible rather than silent. */
  requestedCount: number
  createdCount: number
  effectiveValidityHours: number
  summaryEmailRequested: boolean
  summaryEmailStatus: string
  summaryEmailNote: string | null
}

export interface PendingInvitationDto {
  invitationId: string
  boutiqueRole: BoutiqueStaffRole
  recipientEmail: string | null
  createdAt: string
  expiresAt: string
}

/** The bounds the server clamps to, mirrored so the UI cannot offer a value it will not honour. */
export const INVITATION_LIMITS = {
  minValidityHours: 1,
  maxValidityHours: 720,
  defaultValidityHours: 24,
  minBulkCount: 1,
  maxBulkCount: 10,
} as const

export const INVITABLE_ROLES: { value: BoutiqueStaffRole; label: string }[] = [
  { value: 'org:boutique_supervisor', label: 'Supervisor' },
  { value: 'org:boutique_manager', label: 'Manager' },
  { value: 'org:boutique_staff', label: 'Staff' },
]

export interface ExpirationOption {
  hours: number
  label: string
}

export const EXPIRATION_OPTIONS: ExpirationOption[] = [
  { hours: 24, label: '24 Hours (Standard)' },
  { hours: 168, label: '7 Days' },
  { hours: 720, label: '30 Days' },
]

