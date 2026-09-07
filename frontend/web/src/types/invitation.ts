/** Canonical boutique staff roles an owner may invite. */
export type BoutiqueStaffRole = 'org:boutique_supervisor' | 'org:boutique_manager' | 'org:boutique_staff'

export interface CreateInvitationRequest {
  boutiqueRole: BoutiqueStaffRole
  recipientEmail?: string
}

export interface CreateInvitationResponse {
  invitationId: string
  code: string
  link: string
  boutiqueRole: BoutiqueStaffRole
  recipientEmail: string | null
  expiresAt: string
}

export interface PendingInvitationDto {
  invitationId: string
  boutiqueRole: BoutiqueStaffRole
  recipientEmail: string | null
  createdAt: string
  expiresAt: string
}

export const INVITABLE_ROLES: { value: BoutiqueStaffRole; label: string }[] = [
  { value: 'org:boutique_supervisor', label: 'Supervisor' },
  { value: 'org:boutique_manager', label: 'Manager' },
  { value: 'org:boutique_staff', label: 'Staff' },
]
