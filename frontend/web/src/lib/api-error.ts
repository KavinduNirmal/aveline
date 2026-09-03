/** Error surfaced by the API client, carrying the HTTP status and a friendly message. */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly details?: unknown

  constructor(
    status: number,
    message: string,
    code?: string,
    details?: unknown,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.details = details
  }
}

function extractMessage(status: number, data: unknown): string {
  if (typeof data === 'object' && data !== null) {
    const record = data as Record<string, unknown>
    const candidate = record.message ?? record.detail ?? record.title
    if (typeof candidate === 'string' && candidate.trim() !== '') {
      return candidate
    }
  }

  switch (status) {
    case 401:
      return 'Your session has expired. Please sign in again.'
    case 403:
      return "You don't have permission to perform this action."
    case 404:
      return 'The requested resource was not found.'
    case 0:
      return 'Unable to reach the server. Please check your connection.'
    default:
      return status >= 500
        ? 'Something went wrong on our side. Please try again.'
        : 'The request failed.'
  }
}

/** Normalizes any thrown value into an [ApiError] with a friendly message. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error
  }
  if (typeof error === 'object' && error !== null && 'isAxiosError' in error) {
    const axiosError = error as {
      response?: { status?: number; data?: unknown }
      code?: string
      message?: string
    }
    const status = axiosError.response?.status ?? 0
    return new ApiError(
      status,
      extractMessage(status, axiosError.response?.data),
      axiosError.code,
      axiosError.response?.data,
    )
  }
  return new ApiError(
    0,
    error instanceof Error ? error.message : 'The request failed.',
  )
}
