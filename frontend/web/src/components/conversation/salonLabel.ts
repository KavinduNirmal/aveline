import type { ConversationDto } from '@/types/conversation'

/**
 * How a Salon is named, classified and ordered.
 *
 * Extracted from `SalonPanel` because two surfaces need the same answer — the Salon list and the
 * global Aveline drawer — and a panel imported by a drawer (or the reverse) would be a cycle.
 */

/**
 * The display name for a Salon.
 *
 * A client Salon is named after the client: the server already sends `customerName` on every list
 * row, so printing the literal word "Customer" discarded it. An inbound thread whose client is not
 * yet identified is named by its channel reference, because that is the only handle anyone can act
 * on. The general, customer-less thread is Aveline's own.
 */
export function salonLabel(conversation: ConversationDto): string {
  if (conversation.customerId) {
    const name = conversation.customerName?.trim()
    return name && name.length > 0 ? name : 'Unnamed client'
  }
  const channel = conversation.externalRef?.trim()
  return channel && channel.length > 0 ? channel : 'Aveline'
}

/**
 * True for the organization-shared concierge thread: no client and no channel. This is the Salon
 * every member has, the one the global Aveline panel pins to, and the one that answers questions
 * about any client.
 */
export function isGeneralSalon(conversation: ConversationDto): boolean {
  return !conversation.customerId && !conversation.externalRef
}

/**
 * The general Salon first, then the rest in the order the server sent them (newest activity first).
 * Returns a new array: the caller's list is context state and must not be reordered in place.
 */
export function sortSalons(conversations: ConversationDto[]): ConversationDto[] {
  return [...conversations].sort((a, b) => Number(isGeneralSalon(b)) - Number(isGeneralSalon(a)))
}
