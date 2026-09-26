import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

import { LOG_LEVELS, levelLabel, type LogLevel } from '@/lib/admin/log-level'

export interface LogFilterState {
  level: LogLevel | 'all'
  action: string
  actorKind: string
  entityType: string
}

export const EMPTY_LOG_FILTERS: LogFilterState = {
  level: 'all',
  action: '',
  actorKind: '',
  entityType: '',
}

export const ACTOR_KIND_OPTIONS = [
  'User',
  'Admin',
  'System',
  'Scheduler',
  'Service',
  'Alert',
] as const

/**
 * The feed's client-side filters.
 *
 * The severity filter is explicit about what it filters: the console's own derived level, not a
 * server field, because the audit wire contract has no level at all (Q4).
 */
export function LogFilters({
  filters,
  onChange,
}: {
  filters: LogFilterState
  onChange: (filters: LogFilterState) => void
}) {
  const set = <K extends keyof LogFilterState>(key: K, value: LogFilterState[K]) => {
    onChange({ ...filters, [key]: value })
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Input
        aria-label="Filter by action"
        placeholder="Filter by action…"
        value={filters.action}
        onChange={(event) => set('action', event.target.value)}
        className="h-8 max-w-[240px] text-xs"
      />
      <Input
        aria-label="Filter by entity type"
        placeholder="Entity type…"
        value={filters.entityType}
        onChange={(event) => set('entityType', event.target.value)}
        className="h-8 max-w-[180px] text-xs"
      />
      <Select
        value={filters.actorKind === '' ? 'all' : filters.actorKind}
        onValueChange={(value) => set('actorKind', value === 'all' ? '' : value)}
      >
        <SelectTrigger className="h-8 w-[150px] text-xs" aria-label="Filter by actor">
          <SelectValue placeholder="All actors" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All actors</SelectItem>
          {ACTOR_KIND_OPTIONS.map((kind) => (
            <SelectItem key={kind} value={kind}>
              {kind}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Select
        value={filters.level}
        onValueChange={(value) => set('level', value as LogLevel | 'all')}
      >
        <SelectTrigger className="h-8 w-[170px] text-xs" aria-label="Filter by derived level">
          <SelectValue placeholder="All derived levels" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All derived levels</SelectItem>
          {LOG_LEVELS.map((level) => (
            <SelectItem key={level} value={level}>
              {levelLabel(level)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {filters.level !== 'all' || filters.action !== '' || filters.actorKind !== '' || filters.entityType !== '' ? (
        <Button
          variant="ghost"
          size="sm"
          className="h-8 text-xs"
          onClick={() => onChange(EMPTY_LOG_FILTERS)}
        >
          Clear
        </Button>
      ) : null}
    </div>
  )
}
