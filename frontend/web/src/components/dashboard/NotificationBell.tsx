import { Bell, CheckCheck, X } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'

import { useNotifications } from '@/contexts/NotificationsContext'
import {
  dismissNotification,
  fetchNotifications,
  fetchUnreadCount,
  markAllNotificationsRead,
  markNotificationRead,
} from '@/lib/notifications-api'
import { cn } from '@/lib/utils'
import type { UserNotificationDto } from '@/types/notification'

import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

/** Formats an ISO timestamp as a short relative label (e.g. "5m ago"). */
function timeAgo(iso: string): string {
  const seconds = Math.floor((Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  return new Date(iso).toLocaleDateString()
}

/**
 * Notification bell for the dashboard top bar. Shows an unread badge, lists the caller's
 * notifications, and supports opening (marking read), marking all read, and dismissing.
 * Refreshes when a realtime notification arrives via the SignalR context.
 */
export function NotificationBell() {
  const { lastNotification } = useNotifications()
  const [items, setItems] = useState<UserNotificationDto[]>([])
  const [unread, setUnread] = useState(0)
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const [page, count] = await Promise.all([fetchNotifications({ pageSize: 20 }), fetchUnreadCount()])
      setItems(page.items)
      setUnread(count)
    } catch {
      /* best-effort; keep previous state */
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  // When a realtime notification arrives, refresh the list + badge.
  useEffect(() => {
    if (lastNotification) {
      void refresh()
    }
  }, [lastNotification, refresh])

  const handleOpen = useCallback(
    async (item: UserNotificationDto) => {
      if (!item.isRead) {
        setUnread((prev) => Math.max(0, prev - 1))
        setItems((prev) => prev.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)))
        try {
          await markNotificationRead(item.id)
        } catch {
          /* best-effort */
        }
      }
    },
    [],
  )

  const handleMarkAllRead = useCallback(async () => {
    setUnread(0)
    setItems((prev) => prev.map((n) => ({ ...n, isRead: true })))
    try {
      await markAllNotificationsRead()
    } catch {
      /* best-effort */
    }
  }, [])

  const handleDismiss = useCallback(
    async (id: string) => {
      setItems((prev) => prev.filter((n) => n.id !== id))
      try {
        await dismissNotification(id)
      } catch {
        /* best-effort */
      }
    },
    [],
  )

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Notifications" className="relative">
          <Bell className="size-5" aria-hidden />
          {unread > 0 && (
            <span className="absolute right-1 top-1 flex size-4 items-center justify-center rounded-full bg-primary text-[10px] font-semibold text-primary-foreground">
              {unread > 9 ? '9+' : unread}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-96">
        <div className="flex items-center justify-between px-2 py-1.5">
          <DropdownMenuLabel className="px-2 py-0">Notifications</DropdownMenuLabel>
          {unread > 0 && (
            <Button variant="ghost" size="sm" className="gap-1 text-xs" onClick={handleMarkAllRead}>
              <CheckCheck className="size-3.5" aria-hidden />
              Mark all read
            </Button>
          )}
        </div>
        <DropdownMenuSeparator />
        <div className="max-h-96 overflow-y-auto">
          {loading && items.length === 0 ? (
            <p className="px-3 py-8 text-center text-sm text-muted-foreground">Loading…</p>
          ) : items.length === 0 ? (
            <div className="px-3 py-8 text-center text-sm text-muted-foreground">
              <p>You're all caught up.</p>
            </div>
          ) : (
            items.map((item) => (
              <div
                key={item.id}
                role="button"
                tabIndex={0}
                onClick={() => void handleOpen(item)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') void handleOpen(item)
                }}
                className={cn(
                  'group flex cursor-pointer gap-3 border-b px-3 py-3 text-left transition-colors last:border-b-0 hover:bg-muted',
                  !item.isRead && 'bg-primary/5',
                )}
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    {!item.isRead && <span className="size-2 shrink-0 rounded-full bg-primary" aria-hidden />}
                    <p className="truncate text-sm font-medium">{item.title}</p>
                  </div>
                  <p className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">{item.body}</p>
                  <p className="mt-1 text-[11px] text-muted-foreground/70">{timeAgo(item.createdAt)}</p>
                </div>
                <button
                  type="button"
                  aria-label="Dismiss notification"
                  onClick={(e) => {
                    e.stopPropagation()
                    void handleDismiss(item.id)
                  }}
                  className="self-start rounded p-1 text-muted-foreground opacity-0 transition-opacity hover:bg-muted-foreground/10 group-hover:opacity-100"
                >
                  <X className="size-3.5" aria-hidden />
                </button>
              </div>
            ))
          )}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
