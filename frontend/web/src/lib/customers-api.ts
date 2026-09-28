/**
 * The tenant customer surface (docs/api/README.md B.19 / §C.11).
 *
 * Every function builds `/api/v1/orgs/${organizationId}/customers…`, the token the server's
 * organization-scope handler reads. The writes send an `Idempotency-Key` because a retried
 * counter action must not mint a second client or a second visit.
 */

export interface CustomerBookItem {
  customerId: string
  fullName: string | null
  nickname: string | null
  level: string | null
  status: string
  phoneNumber: string
  lastVisitAtUtc: string | null
  visitCount: number
  totalSpent: number
}

export interface CustomerBookPage {
  items: CustomerBookItem[]
  total: number
  page: number
  pageSize: number
}

export interface TenantCustomerDetail {
  customerId: string
  fullName: string | null
  nickname: string | null
  phoneNumber: string | null
  email: string | null
  level: string | null
  status: string
  totalSpent: number
  visitCount: number
  lastVisitAtUtc: string | null
  /** Always 1: the server derives the tier from spend, visits and recency. */
  loyaltyTierIsDerived: number
  createdAtUtc: string
  updatedAtUtc: string | null
  interactionCount: number
  tags: string[]
  /** The boutique's own prose about this client. Null when nobody has written one yet. */
  description: string | null
  /**
   * `pending` | `granted` | `revoked`.
   *
   * Shown, never offered: the customer's own consent is not a staff decision, and the staff
   * surfaces that write it are deliberately separate from this record.
   */
  consentStatus: string
  /**
   * The preferences the boutique holds, excluding the nickname alias.
   *
   * Optional on the type because the book row does not carry it and an older payload would omit
   * it; the readers default to an empty list rather than rendering a broken tile.
   */
  preferences?: TenantCustomerPreference[]
}

/** One stated or inferred preference (`CustomerPreferenceDto`). */
export interface TenantCustomerPreference {
  id: string
  preferenceKey: string
  preferenceValue: string
  isExplicit: boolean
  confidence: number
}

/**
 * One note the boutique holds about a client, as the brief and the memory panel show it.
 */
export interface TenantCustomerMemory {
  id: string
  customerId: string
  content: string
  category: string
  source: string
  isExplicit: boolean
  confidence: number
  createdAtUtc: string
}

/** One occasion on a client's calendar (`CustomerEventDto`). */
export interface TenantCustomerEvent {
  id: string
  eventType: string
  eventDate: string
  description: string | null
  isActive: boolean
}

/**
 * A client's occasions.
 *
 * The list is filtered on `isActive` alone, so it carries occasions that have already happened; the
 * reader decides which are still ahead rather than trusting the collection to be "upcoming".
 */
export async function fetchCustomerEvents(
  organizationId: string,
  customerId: string,
  signal?: AbortSignal,
): Promise<TenantCustomerEvent[]> {
  const response = await apiClient.get<TenantCustomerEvent[]>(
    `${customersBase(organizationId)}/${customerId}/events`,
    { signal },
  )
  return response.data
}

/**
 * A client's live memories, newest first.
 *
 * A read of its own rather than a projection of the brief: the brief collapses the notes into what
 * an associate reads before contact, while the panel below the fold is the full store, including
 * the categories (complaint, sentiment) that would otherwise appear nowhere on the screen.
 */
export async function fetchCustomerMemories(
  organizationId: string,
  customerId: string,
  signal?: AbortSignal,
): Promise<TenantCustomerMemory[]> {
  const response = await apiClient.get<TenantCustomerMemory[]>(
    `${customersBase(organizationId)}/${customerId}/memories`,
    { signal },
  )
  return response.data
}

/** One upcoming occasion on the pre-contact brief. */
export interface TenantCustomerBriefEvent {
  id: string
  eventType: string
  eventDate: string
  description: string | null
}

/**
 * The pre-contact brief: who the client is, what is known, and what is coming up.
 *
 * `memories` is empty unless consent is granted; `consentStatus` says why, so the caller can
 * label an empty section instead of showing it as "nothing on file".
 */
export interface TenantCustomerBrief {
  customerId: string
  customerName: string
  description: string | null
  status: string
  consentStatus: string
  preferenceSummary: string | null
  tags: string[]
  upcomingEvents: TenantCustomerBriefEvent[]
  memories: TenantCustomerMemory[]
  generatedAtUtc: string
}

export interface CustomerInteractionItem {
  interactionId: string
  occurredAtUtc: string
  channel: string
  direction: string
  note: string | null
  countedAsVisit: boolean
}

export interface CustomerInteractionPage {
  items: CustomerInteractionItem[]
  total: number
  page: number
  pageSize: number
}

/**
 * The writable subset. `status` is deliberately absent: the server derives it and ignores a
 * client-supplied value, so offering it here would be a control that cannot work.
 */
export interface UpdateCustomerRequest {
  fullName?: string
  nickname?: string
  phoneNumber?: string
  email?: string
  level?: string
  description?: string
}

export interface CreateWalkInCustomerRequest {
  fullName: string
  phoneNumber?: string
  nickname?: string
  source?: string
}

export interface WalkInCreated {
  customerId: string
  fullName: string | null
  level: string | null
  status: string
  consentStatus: string
  createdAtUtc: string
  /** Set when the walk-in matched an existing client: the response is then a report, not a create. */
  duplicateOfCustomerId: string | null
}

export interface RecordInteractionRequest {
  occurredAtUtc: string
  channel: string
  direction?: string
  note?: string
  purchaseTotal?: number
}

const customersBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/customers`

/** The client book: search, level filter and paging. */
export async function fetchCustomerBook(
  organizationId: string,
  options: { search?: string; level?: string; page?: number; pageSize?: number } = {},
  signal?: AbortSignal,
): Promise<CustomerBookPage> {
  const response = await apiClient.get<CustomerBookPage>(customersBase(organizationId), {
    params: {
      search: options.search || undefined,
      level: options.level || undefined,
      page: options.page ?? 1,
      pageSize: options.pageSize ?? 50,
    },
    signal,
  })
  return response.data
}

/** Read one client. 404 means "not in this boutique", indistinguishable from "does not exist". */
export async function fetchCustomer(
  organizationId: string,
  customerId: string,
  signal?: AbortSignal,
): Promise<TenantCustomerDetail> {
  const response = await apiClient.get<TenantCustomerDetail>(
    `${customersBase(organizationId)}/${customerId}`,
    { signal },
  )
  return response.data
}

/**
 * The pre-contact brief for one client.
 *
 * A read of its own rather than a projection of the detail: it is what an associate opens
 * *before* making contact, and its memories section is consent-gated on the server.
 */
export async function fetchCustomerBrief(
  organizationId: string,
  customerId: string,
  signal?: AbortSignal,
): Promise<TenantCustomerBrief> {
  const response = await apiClient.get<TenantCustomerBrief>(
    `${customersBase(organizationId)}/${customerId}/brief`,
    { signal },
  )
  return response.data
}

/** The client's interaction history, newest first. */
export async function fetchCustomerInteractions(  organizationId: string,
  customerId: string,
  options: { page?: number; pageSize?: number } = {},
  signal?: AbortSignal,
): Promise<CustomerInteractionPage> {
  const response = await apiClient.get<CustomerInteractionPage>(
    `${customersBase(organizationId)}/${customerId}/interactions`,
    {
      params: { page: options.page ?? 1, pageSize: options.pageSize ?? 50 },
      signal,
    },
  )
  return response.data
}

/**
 * Creates a counter walk-in. A duplicate name returns the existing client with
 * `duplicateOfCustomerId` set, so the caller must say "already on file" rather than "created".
 */
export async function createWalkInCustomer(
  organizationId: string,
  payload: CreateWalkInCustomerRequest,
  idempotencyKey: string,
): Promise<WalkInCreated> {
  const response = await apiClient.post<WalkInCreated>(
    customersBase(organizationId),
    payload,
    { headers: { 'Idempotency-Key': idempotencyKey } },
  )
  return response.data
}

/** Partial update of the writable fields. Needs `customers:manage`. */
export async function updateCustomer(
  organizationId: string,
  customerId: string,
  payload: UpdateCustomerRequest,
): Promise<TenantCustomerDetail> {
  const response = await apiClient.patch<TenantCustomerDetail>(
    `${customersBase(organizationId)}/${customerId}`,
    payload,
  )
  return response.data
}

/**
 * Soft-deletes a client. Idempotent: a second delete is 204, not 404.
 * A client with live orders is refused with `customer-has-open-orders`.
 */
export async function deleteCustomer(
  organizationId: string,
  customerId: string,
): Promise<void> {
  await apiClient.delete(`${customersBase(organizationId)}/${customerId}`)
}

/** Records a counter interaction, optionally with the amount taken. Needs no manage permission. */
export async function recordCustomerInteraction(
  organizationId: string,
  customerId: string,
  payload: RecordInteractionRequest,
  idempotencyKey: string,
): Promise<void> {
  await apiClient.post(
    `${customersBase(organizationId)}/${customerId}/interactions`,
    payload,
    { headers: { 'Idempotency-Key': idempotencyKey } },
  )
}
import { apiClient } from '@/lib/api'
