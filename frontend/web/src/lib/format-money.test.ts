import { describe, expect, it } from 'vitest'

import { formatCount, formatMoney, formatPercent, NOT_MEASURED } from './format-money'

/**
 * The rule this file exists to enforce: **`null` is "not measured"; `0` is a measurement.**
 * `UsagePanel` used to compute `usage?.blossomUsed ?? 0` and render "0 used" for a period the API
 * had not measured, which reads as a fact about the shop.
 */
describe('formatMoney', () => {
  it('renders a measured value with the currency', () => {
    // `en-LK` formats LKR as "LKR" with two decimals; asserting on the digits keeps the test
    // portable across ICU versions.
    const formatted = formatMoney(42000, 'LKR', 'en-LK')
    expect(formatted).toContain('42,000.00')
  })

  it('renders null as "not measured" rather than zero', () => {
    expect(formatMoney(null)).toBe(NOT_MEASURED)
    expect(formatMoney(undefined)).toBe(NOT_MEASURED)
    expect(formatMoney(Number.NaN)).toBe(NOT_MEASURED)
  })

  it('renders a measured zero as zero, not as "not measured"', () => {
    // This is the whole distinction: zero revenue is a real number.
    expect(formatMoney(0, 'LKR', 'en-LK')).toContain('0.00')
    expect(formatMoney(0, 'LKR', 'en-LK')).not.toBe(NOT_MEASURED)
  })

  it('uses the currency it is given, so two panels cannot disagree', () => {
    expect(formatMoney(10, 'USD', 'en-US')).toContain('$')
    expect(formatMoney(10, 'LKR', 'en-LK')).not.toContain('$')
  })
})

describe('formatCount', () => {
  it('renders a measured zero as "0"', () => {
    expect(formatCount(0)).toBe('0')
  })

  it('renders null as "not measured"', () => {
    expect(formatCount(null)).toBe(NOT_MEASURED)
  })

  it('groups thousands', () => {
    expect(formatCount(1200, 'en-US')).toBe('1,200')
  })
})

describe('formatPercent', () => {
  it('renders a measured zero as 0%, not as "not measured"', () => {
    expect(formatPercent(0, 'en-US')).toBe('0%')
  })

  it('renders null as "not measured"', () => {
    expect(formatPercent(null)).toBe(NOT_MEASURED)
  })

  it('takes a percentage, not a fraction', () => {
    expect(formatPercent(50, 'en-US')).toBe('50%')
  })
})
