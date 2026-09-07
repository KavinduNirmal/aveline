import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()
const patchMock = vi.fn()
const deleteMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
    patch: (...args: unknown[]) => patchMock(...args),
    delete: (...args: unknown[]) => deleteMock(...args),
  },
}))

import {
  dismissNotification,
  fetchNotification,
  fetchNotifications,
  fetchUnreadCount,
  markAllNotificationsRead,
  markNotificationRead,
} from './notifications-api'

describe('notifications API client', () => {
  beforeEach(() => {
    getMock.mockReset()
    postMock.mockReset()
    patchMock.mockReset()
    deleteMock.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('fetchNotifications requests the list endpoint with default paging', async () => {
    getMock.mockResolvedValue({ data: { items: [], total: 0, page: 1, pageSize: 50 } })

    await fetchNotifications()

    expect(getMock).toHaveBeenCalledWith('/api/v1/notifications', {
      params: { page: 1, pageSize: 50, unreadOnly: false },
    })
  })

  it('fetchNotifications forwards unreadOnly and paging params', async () => {
    getMock.mockResolvedValue({ data: { items: [], total: 0, page: 2, pageSize: 10 } })

    await fetchNotifications({ page: 2, pageSize: 10, unreadOnly: true })

    expect(getMock).toHaveBeenCalledWith('/api/v1/notifications', {
      params: { page: 2, pageSize: 10, unreadOnly: true },
    })
  })

  it('fetchUnreadCount returns the count', async () => {
    getMock.mockResolvedValue({ data: { count: 3 } })

    await expect(fetchUnreadCount()).resolves.toBe(3)
    expect(getMock).toHaveBeenCalledWith('/api/v1/notifications/unread-count')
  })

  it('fetchNotification requests a single notification', async () => {
    const dto = { id: 'n1', title: 'Hi' }
    getMock.mockResolvedValue({ data: dto })

    await expect(fetchNotification('n1')).resolves.toEqual(dto)
    expect(getMock).toHaveBeenCalledWith('/api/v1/notifications/n1')
  })

  it('markNotificationRead patches the read endpoint', async () => {
    patchMock.mockResolvedValue({})

    await markNotificationRead('n1')

    expect(patchMock).toHaveBeenCalledWith('/api/v1/notifications/n1/read')
  })

  it('markAllNotificationsRead posts and returns updated count', async () => {
    postMock.mockResolvedValue({ data: { updated: 5 } })

    await expect(markAllNotificationsRead()).resolves.toBe(5)
    expect(postMock).toHaveBeenCalledWith('/api/v1/notifications/read-all')
  })

  it('dismissNotification deletes the notification', async () => {
    deleteMock.mockResolvedValue({})

    await dismissNotification('n1')

    expect(deleteMock).toHaveBeenCalledWith('/api/v1/notifications/n1')
  })
})
