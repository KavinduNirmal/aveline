import { useCallback, useEffect, useState } from "react"
import { queryAuditEntries } from "@/lib/admin/api"
import type { AuditLogEntry } from "@/types/admin"

interface UseAuditTrailOptions {
  actorUserId?: string
  organizationId?: string
  action?: string
  entityType?: string
  entityId?: string
  pageSize?: number
  enabled?: boolean
}

export function useAuditTrail(options: UseAuditTrailOptions = {}) {
  const {
    actorUserId,
    organizationId,
    action,
    entityType,
    entityId,
    pageSize = 20,
    enabled = true,
  } = options

  const [entries, setEntries] = useState<AuditLogEntry[]>([])
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(
    async (targetPage: number, append = false) => {
      if (!enabled) return
      setIsLoading(true)
      setError(null)
      try {
        const res = await queryAuditEntries({
          actorUserId,
          organizationId,
          action,
          entityType,
          entityId,
          page: targetPage,
          pageSize,
        })
        setEntries((prev) => (append ? [...prev, ...res.items] : res.items))
        setPage(res.page)
        setTotal(res.total)
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : "Failed to load audit trail")
      } finally {
        setIsLoading(false)
      }
    },
    [action, actorUserId, enabled, entityId, entityType, organizationId, pageSize],
  )

  useEffect(() => {
    void load(1, false)
  }, [load])

  const loadMore = useCallback(async () => {
    if (entries.length < total && !isLoading) {
      await load(page + 1, true)
    }
  }, [entries.length, isLoading, load, page, total])

  const refresh = useCallback(async () => {
    await load(1, false)
  }, [load])

  return {
    entries,
    page,
    total,
    isLoading,
    error,
    hasMore: entries.length < total,
    loadMore,
    refresh,
  }
}
