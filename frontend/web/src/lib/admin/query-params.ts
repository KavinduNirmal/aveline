/**
 * URL-backed list state. Filters and paging live in the query string, so the back button works,
 * a filtered view is shareable, and a refresh does not silently reset the operator's context.
 */

export const USER_PAGE_SIZES = [25, 50, 100, 200] as const
export const ORG_PAGE_SIZES = [25, 50, 100, 200] as const
export const AUDIT_PAGE_SIZES = [25, 50, 100, 200] as const

export interface ListParams<F extends string = string> {
  page: number
  pageSize: number
  filters: Partial<Record<F, string>>
}

export interface ListParamOptions<F extends string> {
  pageSizes: readonly number[]
  defaultPageSize: number
  /** The query-string keys this surface owns; anything else in the URL is ignored. */
  filters?: readonly F[]
}

function positiveInt(value: string | null, fallback: number): number {
  if (value === null) return fallback
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed >= 1 ? parsed : fallback
}

export function readListParams<F extends string>(
  search: URLSearchParams,
  options: ListParamOptions<F>,
): ListParams<F> {
  const page = positiveInt(search.get('page'), 1)
  const requestedSize = positiveInt(search.get('pageSize'), options.defaultPageSize)
  // A page size the surface does not offer is not forwarded: the server has its own clamp and a
  // silently-clamped page size makes the pager lie.
  const pageSize = options.pageSizes.includes(requestedSize)
    ? requestedSize
    : options.defaultPageSize

  const filters: Partial<Record<F, string>> = {}
  for (const key of options.filters ?? []) {
    const value = search.get(key)
    if (value !== null && value.length > 0) filters[key] = value
  }

  return { page, pageSize, filters }
}

export function writeListParams<F extends string>(
  params: ListParams<F>,
  options: ListParamOptions<F>,
): URLSearchParams {
  const search = new URLSearchParams()
  if (params.page !== 1) search.set('page', String(params.page))
  if (params.pageSize !== options.defaultPageSize) {
    search.set('pageSize', String(params.pageSize))
  }
  for (const [key, value] of Object.entries(params.filters)) {
    if (typeof value === 'string' && value.length > 0) search.set(key, value)
  }
  return search
}

/** Converts a filter record into the `undefined`-based shape the API client expects. */
export function toQueryFilters<F extends string>(
  filters: Partial<Record<F, string>>,
): Record<string, string | undefined> {
  const result: Record<string, string | undefined> = {}
  for (const [key, value] of Object.entries(filters)) {
    result[key] = typeof value === 'string' && value.length > 0 ? value : undefined
  }
  return result
}
