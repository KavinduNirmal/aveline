import { apiClient } from '@/lib/api'
import type {
  MarkAllReadResponse,
  NotificationPage,
  UnreadCountResponse,
  UserNotificationDto,
} from '@/types/notification'

export interface FetchNotificationsParams {
  page?: number
  pageSize?: number
  unreadOnly?: boolean
}

/**
 * Lists the current user's notifications. See GET /api/v1/notifications.
 */
export async function fetchNotifications(
  params: FetchNotificationsParams = {},
): Promise<NotificationPage> {
  const response = await apiClient.get<NotificationPage>('/api/v1/notifications', {
    params: {
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 50,
      unreadOnly: params.unreadOnly ?? false,
    },
  })
  return response.data
}

/**
 * Returns the current user's unread notification count. See GET /api/v1/notifications/unread-count.
 */
export async function fetchUnreadCount(): Promise<number> {
  const response = await apiClient.get<UnreadCountResponse>('/api/v1/notifications/unread-count')
  return response.data.count
}

/**
 * Fetches a single notification. See GET /api/v1/notifications/{id}.
 */
export async function fetchNotification(id: string): Promise<UserNotificationDto> {
  const response = await apiClient.get<UserNotificationDto>(`/api/v1/notifications/${id}`)
  return response.data
}

/**
 * Marks a single notification as read. See PATCH /api/v1/notifications/{id}/read.
 */
export async function markNotificationRead(id: string): Promise<void> {
  await apiClient.patch(`/api/v1/notifications/${id}/read`)
}

/**
 * Marks all of the current user's notifications as read. See POST /api/v1/notifications/read-all.
 */
export async function markAllNotificationsRead(): Promise<number> {
  const response = await apiClient.post<MarkAllReadResponse>('/api/v1/notifications/read-all')
  return response.data.updated
}

/**
 * Dismisses (soft-deletes) a single notification. See DELETE /api/v1/notifications/{id}.
 */
export async function dismissNotification(id: string): Promise<void> {
  await apiClient.delete(`/api/v1/notifications/${id}`)
}
