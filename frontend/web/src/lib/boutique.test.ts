import { describe, expect, it } from 'vitest'

import {
  BOUTIQUE_DESCRIPTION_MAX,
  formatLkPhone,
  isValidLkPhone,
  LK_LOCAL_DIGITS,
  LK_PHONE_REGEX,
} from './boutique'

describe('formatLkPhone', () => {
  it('formats a bare 9-digit local number with the +94 prefix and 2-2-2-3 grouping', () => {
    expect(formatLkPhone('771212123')).toBe('+94 77 12 12 123')
  })

  it('formats a pasted E.164 number (drops the country code)', () => {
    expect(formatLkPhone('94771212123')).toBe('+94 77 12 12 123')
  })

  it('drops a leading 0 used for national dialling', () => {
    expect(formatLkPhone('0771212123')).toBe('+94 77 12 12 123')
  })

  it('ignores non-numeric characters and never exceeds 9 local digits', () => {
    expect(formatLkPhone('+94 77 1a2b1c2d1e2f3g9x')).toBe('+94 77 12 12 123')
    expect(formatLkPhone('0771212123456789')).toBe('+94 77 12 12 123')
  })

  it('returns only the prefix when there are no digits', () => {
    expect(formatLkPhone('')).toBe('+94 ')
    expect(formatLkPhone('abc')).toBe('+94 ')
  })
})

describe('isValidLkPhone', () => {
  it('accepts exactly 9 local digits in the formatted shape', () => {
    expect(isValidLkPhone('+94 77 12 12 123')).toBe(true)
    expect(LK_PHONE_REGEX.test('+94 77 12 12 123')).toBe(true)
  })

  it('rejects partial or invalid numbers', () => {
    expect(isValidLkPhone('+94 ')).toBe(false)
    expect(isValidLkPhone('+94 77 12 12')).toBe(false)
    expect(isValidLkPhone('0771212123')).toBe(false)
    expect(isValidLkPhone('+94 771 234 567')).toBe(false)
    expect(isValidLkPhone('+94 77 12 12 1234')).toBe(false)
  })

  it('rejects any non-numeric character in the local part', () => {
    expect(LK_PHONE_REGEX.test('+94 77 12 a2 123')).toBe(false)
    expect(isValidLkPhone('+94 77 12 a2 123')).toBe(false)
  })
})

describe('boutique limits', () => {
  it('exposes a bounded description limit', () => {
    expect(BOUTIQUE_DESCRIPTION_MAX).toBeGreaterThan(0)
    expect(typeof BOUTIQUE_DESCRIPTION_MAX).toBe('number')
  })

  it('documents 9 local digits excluding the country code', () => {
    expect(LK_LOCAL_DIGITS).toBe(9)
  })
})
