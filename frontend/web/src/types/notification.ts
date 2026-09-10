/** A single notification inbox item returned by the notifications API. */
export interface UserNotificationDto {
  id: string
  notificationId: string
  type: string
  title: string
  body: string
  data: Record<string, string | null>
  isRead: boolean
  readAt: string | null
  deliveredAt: string | null
  createdAt: string
}

/** A paginated page of the current user's notifications. */
export interface NotificationPage {
  items: UserNotificationDto[]
  total: number
  page: number
  pageSize: number
}

/** Response of the unread-count endpoint. */
export interface UnreadCountResponse {
  count: number
}

/** Response of the mark-all-read endpoint. */
export interface MarkAllReadResponse {
  updated: number
}
