import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { DataTable, type Column } from './DataTable'

interface Row {
  id: string
  name: string
}

const columns: Column<Row>[] = [
  { key: 'name', header: 'Name', render: (row) => row.name, sortable: true },
]

function renderTable(props: Partial<Parameters<typeof DataTable<Row>>[0]> = {}) {
  return render(
    <DataTable<Row>
      columns={columns}
      rows={[]}
      getRowKey={(row) => row.id}
      state="ready"
      emptyMessage="Nothing here."
      {...props}
    />,
  )
}

/**
 * One table for every list. The delivered console repeated the same loading/empty/error markup
 * five times and got the differences wrong each time.
 */
describe('DataTable', () => {
  it('renders the loading state without rendering rows', () => {
    renderTable({ state: 'loading', rows: [{ id: '1', name: 'Ada' }] })
    expect(screen.getAllByTestId('table-skeleton-row').length).toBeGreaterThan(0)
    expect(screen.queryByText('Ada')).toBeNull()
  })

  it('renders the empty state', () => {
    renderTable({ state: 'ready', rows: [] })
    expect(screen.getByText('Nothing here.')).toBeInTheDocument()
  })

  it('renders an error with a working retry', async () => {
    const onRetry = vi.fn()
    renderTable({ state: 'error', errorMessage: 'boom', onRetry })
    expect(screen.getByText('boom')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /retry/i }))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('renders rows when ready', () => {
    renderTable({ rows: [{ id: '1', name: 'Ada' }] })
    expect(screen.getByText('Ada')).toBeInTheDocument()
    expect(screen.queryByTestId('table-skeleton-row')).toBeNull()
  })

  it('announces the sort direction with aria-sort on the header cell', async () => {
    const onSortChange = vi.fn()
    renderTable({
      rows: [{ id: '1', name: 'Ada' }],
      sort: { key: 'name', direction: 'desc' },
      onSortChange,
    })
    expect(screen.getByRole('columnheader', { name: /name/i })).toHaveAttribute(
      'aria-sort',
      'descending',
    )
    await userEvent.click(screen.getByRole('button', { name: /name/i }))
    expect(onSortChange).toHaveBeenCalledWith('name')
  })
})
