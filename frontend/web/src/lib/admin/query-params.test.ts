import { describe, expect, it } from 'vitest'

import {
  ORG_PAGE_SIZES,
  USER_PAGE_SIZES,
  readListParams,
  writeListParams,
} from './query-params'

const USER_OPTIONS = { pageSizes: USER_PAGE_SIZES, defaultPageSize: 50 }

describe('readListParams', () => {
  it('defaults page and pageSize when the URL carries neither', () => {
    const params = readListParams(new URLSearchParams(), USER_OPTIONS)
    expect(params.page).toBe(1)
    expect(params.pageSize).toBe(50)
  })

  it('reads a page and a page size the surface allows', () => {
    const params = readListParams(new URLSearchParams('page=3&pageSize=200'), USER_OPTIONS)
    expect(params.page).toBe(3)
    expect(params.pageSize).toBe(200)
  })

  it('falls back to the default rather than sending a page size the server rejects', () => {
    const params = readListParams(new URLSearchParams('pageSize=9999'), USER_OPTIONS)
    expect(params.pageSize).toBe(50)
  })

  it('never reads a page below 1', () => {
    expect(readListParams(new URLSearchParams('page=0'), USER_OPTIONS).page).toBe(1)
    expect(readListParams(new URLSearchParams('page=-4'), USER_OPTIONS).page).toBe(1)
    expect(readListParams(new URLSearchParams('page=abc'), USER_OPTIONS).page).toBe(1)
  })

  it('keeps only the filters the surface declares', () => {
    const params = readListParams(
      new URLSearchParams('q=ada&accountState=Active&page=2'),
      { ...USER_OPTIONS, filters: ['q', 'accountState'] as const },
    )
    expect(params.filters).toEqual({ q: 'ada', accountState: 'Active' })
  })

  it('drops an empty filter rather than sending an empty string', () => {
    const params = readListParams(new URLSearchParams('q=&accountState=Active'), {
      ...USER_OPTIONS,
      filters: ['q', 'accountState'] as const,
    })
    expect(params.filters).toEqual({ accountState: 'Active' })
  })

  it('exposes the page sizes each surface allows', () => {
    expect([...USER_PAGE_SIZES]).toEqual([25, 50, 100, 200])
    expect([...ORG_PAGE_SIZES]).toEqual([25, 50, 100, 200])
  })
})

describe('writeListParams', () => {
  it('omits the defaults so a clean list keeps a clean URL', () => {
    const search = writeListParams({ page: 1, pageSize: 50, filters: {} }, USER_OPTIONS)
    expect(search.toString()).toBe('')
  })

  it('writes the state a caller would want to bookmark or share', () => {
    const search = writeListParams(
      { page: 4, pageSize: 100, filters: { q: 'ada lovelace', accountState: 'Active' } },
      { ...USER_OPTIONS, filters: ['q', 'accountState'] as const },
    )
    expect(search.get('page')).toBe('4')
    expect(search.get('pageSize')).toBe('100')
    expect(search.get('q')).toBe('ada lovelace')
    expect(search.get('accountState')).toBe('Active')
  })

  it('round-trips through readListParams', () => {
    const options = { ...USER_OPTIONS, filters: ['q'] as const }
    const original = { page: 7, pageSize: 25, filters: { q: 'bloom' } }
    expect(readListParams(writeListParams(original, options), options)).toEqual(original)
  })
})
