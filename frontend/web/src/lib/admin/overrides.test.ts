import { describe, expect, it } from 'vitest'

import {
  classifyOverrideError,
  coerceOverrideValue,
  inferOverrideValueType,
} from './overrides'

describe('classifyOverrideError', () => {
  it('routes a 400 validation problem to a field-level error', () => {
    const result = classifyOverrideError({
      status: 400,
      errors: { 'overrides[0].value': ['Value must be an integer for this key.'] },
    })
    expect(result.kind).toBe('field')
    expect(result.fields).toEqual({
      'overrides[0].value': ['Value must be an integer for this key.'],
    })
  })

  it('routes a 409 overlap to "the world changed, reload and retry"', () => {
    const result = classifyOverrideError({ status: 409, code: 'override-overlap' })
    expect(result.kind).toBe('overlap')
    expect(result.message).toMatch(/reload and retry/i)
  })

  it('does not conflate the two: a 400 is never an overlap and a 409 is never a field error', () => {
    expect(classifyOverrideError({ status: 400 }).kind).not.toBe('overlap')
    expect(classifyOverrideError({ status: 409 }).kind).not.toBe('field')
  })

  it('falls back to a generic error', () => {
    expect(classifyOverrideError(new Error('network down')).kind).toBe('other')
    expect(classifyOverrideError({ status: 500 }).kind).toBe('other')
  })

  it('keeps the server message when it has one', () => {
    expect(classifyOverrideError({ status: 409, message: 'overlaps rule 7' }).message).toBe(
      'overlaps rule 7',
    )
  })
})

describe('override value typing', () => {
  it('infers the valueType from the key, matching the server catalog shapes', () => {
    expect(inferOverrideValueType('feature:custom_styling')).toBe('Boolean')
    expect(inferOverrideValueType('max_products')).toBe('Integer')
    expect(inferOverrideValueType('blossoms.monthly')).toBe('Decimal')
    expect(inferOverrideValueType('support_tier')).toBe('String')
  })

  it('coerces the form string into the JSON shape the key expects', () => {
    expect(coerceOverrideValue('true', 'Boolean')).toBe(true)
    expect(coerceOverrideValue('false', 'Boolean')).toBe(false)
    expect(coerceOverrideValue('25', 'Integer')).toBe(25)
    expect(coerceOverrideValue('750.5', 'Decimal')).toBe(750.5)
    expect(coerceOverrideValue('priority', 'String')).toBe('priority')
  })

  it('refuses a value the key cannot take rather than sending a 400', () => {
    expect(() => coerceOverrideValue('abc', 'Integer')).toThrow(/integer/i)
    expect(() => coerceOverrideValue('abc', 'Decimal')).toThrow(/number/i)
  })
})
