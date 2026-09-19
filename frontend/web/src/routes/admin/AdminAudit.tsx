import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

import { DataTable, type Column } from '@/components/admin/data/DataTable'
import { Pagination } from '@/components/admin/data/Pagination'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

import { queryAuditEntries } from '@/lib/admin/api'
import { deriveLevel, deriveSource, levelLabel, levelTone } from '@/lib/admin/log-level'
import { AUDIT_PAGE_SIZES, readListParams, writeListParams } from '@/lib/admin/query-params'
import type { AuditLogEntry } from '@/types/admin'

const FILTER_KEYS = ['action', 'entityType', 'entityId', 'actorUserId', 'organizationId'] as const
type FilterKey = (typeof FILTER_KEYS)[number]

const LEVEL_TONE_CLASS: Record<string, string> = {
  destructive: 'text-destructive',
  warning: 'text-warning',
  primary: 'text-primary',
  muted: 'text-muted-foreground',
}

type LoadState = 'loading' | 'ready' | 'error'

const LIST_OPTIONS = {
  pageSizes: AUDIT_PAGE_SIZES,
  defaultPageSize: 25,
  filters: FILTER_KEYS,
} as const

/**
 * The audit ledger explorer.
 *
 * The **full server-side filter set** the endpoint accepts is exposed here, and the page, page
 * size and filters live in the **query string** (`readListParams`/`writeListParams`), so the
 * back button restores the operator's context and a filtered view is shareable.
 *
 * Severity is the console's own derivation and is labelled as such (Q4); the audit wire contract
 * carries no level.
 */
export function AdminAuditView() {
  const [searchParams, setSearchParams] = useSearchParams()

  const { page, pageSize, filters } = readListParams(searchParams, LIST_OPTIONS)

  const [entries, setEntries] = useState<AuditLogEntry[]>([])
  const [total, setTotal] = useState(0)
  const [state, setState] = useState<LoadState>('loading')
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const setFilter = (key: FilterKey, value: string) => {
    setSearchParams(
      writeListParams({ page: 1, pageSize, filters: { ...filters, [key]: value } }, LIST_OPTIONS),
    )
  }

  const setPage = (nextPage: number) => {
    setSearchParams(writeListParams({ page: nextPage, pageSize, filters }, LIST_OPTIONS))
  }

  const setPageSize = (nextPageSize: number) => {
    setSearchParams(
      writeListParams({ page: 1, pageSize: nextPageSize, filters }, LIST_OPTIONS),
    )
  }

  const load = useCallback(async () => {
    setState('loading')
    try {
      const result = await queryAuditEntries({
        action: filters.action,
        entityType: filters.entityType,
        entityId: filters.entityId,
        actorUserId: filters.actorUserId,
        organizationId: filters.organizationId,
        page,
        pageSize,
      })
      setEntries(result.items)
      setTotal(result.total)
      setState('ready')
    } catch {
      setState('error')
    }
  }, [
    filters.action,
    filters.entityType,
    filters.entityId,
    filters.actorUserId,
    filters.organizationId,
    page,
    pageSize,
  ])

  useEffect(() => {
    void load()
  }, [load])

  const columns: Column<AuditLogEntry>[] = [
    {
      key: 'occurredAt',
      header: 'When',
      render: (entry) => (
        <span className="font-mono text-[11px] text-muted-foreground">
          {new Date(entry.occurredAt).toLocaleString()}
        </span>
      ),
    },
    {
      key: 'level',
      header: 'Level (derived)',
      render: (entry) => {
        const level = deriveLevel(entry.action)
        return (
          <Badge
            variant="outline"
            title="Derived by the console from the action name; the server sends no level."
            className={`text-[10px] uppercase tracking-wider ${LEVEL_TONE_CLASS[levelTone(level)]}`}
          >
            {levelLabel(level)}
          </Badge>
        )
      },
    },
    {
      key: 'action',
      header: 'Action',
      render: (entry) => (
        <span className="font-mono text-xs text-foreground">{entry.action}</span>
      ),
    },
    {
      key: 'entity',
      header: 'Entity',
      render: (entry) => (
        <span className="font-mono text-[11px] text-muted-foreground">
          {entry.entityType}:{entry.entityId}
        </span>
      ),
    },
    {
      key: 'actor',
      header: 'Actor',
      render: (entry) => {
        const source = deriveSource(entry)
        return (
          <span className={`text-xs ${LEVEL_TONE_CLASS[source.tone]}`}>
            {source.label}
            {source.detail !== null && (
              <span className="ml-1 font-mono text-[10px] text-muted-foreground">
                {source.detail}
              </span>
            )}
          </span>
        )
      },
    },
    {
      key: 'reason',
      header: 'Reason',
      render: (entry) => (
        <span className="text-xs text-muted-foreground">{entry.reason ?? '—'}</span>
      ),
    },
    {
      key: 'detail',
      header: '',
      render: (entry) => (
        <Button
          variant="ghost"
          size="sm"
          className="h-7 text-[11px]"
          aria-expanded={expandedId === entry.id}
          onClick={() => setExpandedId(expandedId === entry.id ? null : entry.id)}
        >
          {expandedId === entry.id ? 'Hide state' : 'State'}
        </Button>
      ),
    },
  ]

  const expanded = entries.find((candidate) => candidate.id === expandedId) ?? null

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Audit explorer
        </h2>
        <p className="text-sm text-muted-foreground">
          The full historical ledger, filtered server-side and paged in the URL so the back button
          restores your context. Level is derived by the console and labelled as derived.
        </p>
      </div>

      <Card className="border-border shadow-xs">
        <CardContent className="grid grid-cols-1 gap-3 p-4 sm:grid-cols-2 xl:grid-cols-5">
          <div className="space-y-1">
            <Label htmlFor="audit-action" className="text-[11px] text-muted-foreground">
              Action
            </Label>
            <Input
              id="audit-action"
              aria-label="Action"
              placeholder="users.state.updated"
              value={filters.action ?? ''}
              onChange={(event) => setFilter('action', event.target.value)}
              className="h-8 font-mono text-xs"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="audit-entity-type" className="text-[11px] text-muted-foreground">
              Entity type
            </Label>
            <Input
              id="audit-entity-type"
              aria-label="Entity type"
              placeholder="User"
              value={filters.entityType ?? ''}
              onChange={(event) => setFilter('entityType', event.target.value)}
              className="h-8 font-mono text-xs"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="audit-entity-id" className="text-[11px] text-muted-foreground">
              Entity id
            </Label>
            <Input
              id="audit-entity-id"
              aria-label="Entity id"
              placeholder="GUID"
              value={filters.entityId ?? ''}
              onChange={(event) => setFilter('entityId', event.target.value)}
              className="h-8 font-mono text-xs"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="audit-actor" className="text-[11px] text-muted-foreground">
              Actor user id
            </Label>
            <Input
              id="audit-actor"
              aria-label="Actor user id"
              placeholder="GUID"
              value={filters.actorUserId ?? ''}
              onChange={(event) => setFilter('actorUserId', event.target.value)}
              className="h-8 font-mono text-xs"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="audit-org" className="text-[11px] text-muted-foreground">
              Organization id
            </Label>
            <Input
              id="audit-org"
              aria-label="Organization id"
              placeholder="GUID"
              value={filters.organizationId ?? ''}
              onChange={(event) => setFilter('organizationId', event.target.value)}
              className="h-8 font-mono text-xs"
            />
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs">
        <CardContent className="p-0">
          <DataTable
            columns={columns}
            rows={entries}
            getRowKey={(entry) => entry.id}
            state={state}
            emptyMessage="No audit entries match these filters."
            errorMessage="The audit ledger could not be loaded."
            onRetry={() => void load()}
            caption="Audit ledger entries"
          />
        </CardContent>
      </Card>

      {state === 'ready' && (
        <Pagination
          page={page}
          pageSize={pageSize}
          total={total}
          pageSizes={AUDIT_PAGE_SIZES}
          onPageChange={setPage}
          onPageSizeChange={setPageSize}
        />
      )}

      {expanded !== null && (
        <Card className="border-border shadow-xs">
          <CardContent className="grid grid-cols-1 gap-3 p-3 text-[11px] md:grid-cols-2">
            <div className="rounded border border-border bg-muted/20 p-2">
              <div className="mb-1 font-medium text-muted-foreground">BEFORE</div>
              <pre className="overflow-x-auto whitespace-pre-wrap text-muted-foreground">
                {expanded.before === null
                  ? '(none / redacted)'
                  : JSON.stringify(expanded.before, null, 2)}
              </pre>
            </div>
            <div className="rounded border border-border bg-muted/20 p-2">
              <div className="mb-1 font-medium text-muted-foreground">AFTER</div>
              <pre className="overflow-x-auto whitespace-pre-wrap text-foreground">
                {expanded.after === null
                  ? '(none / redacted)'
                  : JSON.stringify(expanded.after, null, 2)}
              </pre>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
