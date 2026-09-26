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

/** The client's interaction history, newest first. */
export async function fetchCustomerInteractions(
  organizationId: string,
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
