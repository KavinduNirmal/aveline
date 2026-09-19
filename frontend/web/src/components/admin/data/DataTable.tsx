import type { ReactNode } from "react"

import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { ArrowDown, ArrowUp, ChevronsUpDown } from "lucide-react"

export interface Column<T> {
  key: string
  header: string
  render: (row: T) => ReactNode
  sortable?: boolean
  className?: string
}

export type DataTableState = 'loading' | 'ready' | 'error'

export interface SortState {
  key: string
  direction: 'asc' | 'desc'
}

/**
 * One table for every operator list.
 *
 * It owns the five states so a page cannot invent its own: a skeleton while loading, an empty
 * message that names what is empty, an error with a working retry, and rows when ready. The
 * `aria-sort` announcement is the accessibility half — a sort arrow that only exists visually is
 * invisible to a screen reader.
 */
export function DataTable<T>({
  columns,
  rows,
  getRowKey,
  state,
  emptyMessage,
  errorMessage,
  onRetry,
  sort,
  onSortChange,
  caption,
}: {
  columns: Column<T>[]
  rows: T[]
  getRowKey: (row: T) => string
  state: DataTableState
  emptyMessage: string
  errorMessage?: string
  onRetry?: () => void
  sort?: SortState
  onSortChange?: (key: string) => void
  caption?: string
}) {
  const ariaSort = (key: string): 'ascending' | 'descending' | 'none' => {
    if (sort?.key !== key) return 'none'
    return sort.direction === 'asc' ? 'ascending' : 'descending'
  }

  return (
    <Table>
      {caption !== undefined && <caption className="sr-only">{caption}</caption>}
      <TableHeader>
        <TableRow>
          {columns.map((column) => (
            <TableHead
              key={column.key}
              className={column.className}
              aria-sort={column.sortable === true ? ariaSort(column.key) : undefined}
            >
              {column.sortable === true ? (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => onSortChange?.(column.key)}
                  className="h-auto gap-1 p-0 text-xs font-medium hover:bg-transparent hover:text-foreground"
                >
                  {column.header}
                  {sort?.key !== column.key ? (
                    <ChevronsUpDown className="size-3" />
                  ) : sort.direction === 'asc' ? (
                    <ArrowUp className="size-3" />
                  ) : (
                    <ArrowDown className="size-3" />
                  )}
                </Button>
              ) : (
                column.header
              )}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody>
        {state === 'loading' &&
          Array.from({ length: 4 }, (_, index) => (
            <TableRow key={`skeleton-${index}`} data-testid="table-skeleton-row">
              {columns.map((column) => (
                <TableCell key={column.key}>
                  <Skeleton className="h-4 w-full" />
                </TableCell>
              ))}
            </TableRow>
          ))}

        {state === 'error' && (
          <TableRow>
            <TableCell colSpan={columns.length} className="py-8 text-center">
              <div className="space-y-2 text-xs text-muted-foreground">
                <div>{errorMessage ?? 'This list could not be loaded.'}</div>
                {onRetry !== undefined && (
                  <Button variant="outline" size="sm" className="text-xs" onClick={onRetry}>
                    Retry
                  </Button>
                )}
              </div>
            </TableCell>
          </TableRow>
        )}

        {state === 'ready' && rows.length === 0 && (
          <TableRow>
            <TableCell
              colSpan={columns.length}
              className="py-8 text-center text-xs text-muted-foreground"
            >
              {emptyMessage}
            </TableCell>
          </TableRow>
        )}

        {state === 'ready' &&
          rows.map((row) => (
            <TableRow key={getRowKey(row)}>
              {columns.map((column) => (
                <TableCell key={column.key} className={column.className}>
                  {column.render(row)}
                </TableCell>
              ))}
            </TableRow>
          ))}
      </TableBody>
    </Table>
  )
}
