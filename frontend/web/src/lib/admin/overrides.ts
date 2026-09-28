/**
 * The entitlement-override PATCH has two failures that look alike and need different affordances:
 * a `400` is a **field-level validation error** (fix the field), and a `409 override-overlap` is
 * **"the world changed, reload and retry"** (a different screen state, not a toast).
 *
 * The delivered console rendered both as one toast, which left the operator editing a form
 * against a world that had already moved.
 */

export type OverrideErrorKind = 'field' | 'overlap' | 'other'

export interface ClassifiedOverrideError {
  kind: OverrideErrorKind
  message: string
  fields?: Record<string, string[]>
}

interface OverrideErrorShape {
  status?: number
  code?: string
  message?: string
  errors?: Record<string, string[]>
}

export function classifyOverrideError(error: unknown): ClassifiedOverrideError {
  const shape = (error ?? {}) as OverrideErrorShape

  if (shape.status === 400) {
    return {
      kind: 'field',
      message: shape.message ?? 'The server rejected one or more values.',
      ...(shape.errors !== undefined ? { fields: shape.errors } : {}),
    }
  }

  if (shape.status === 409) {
    return {
      kind: 'overlap',
      message: shape.message ?? 'The world changed, reload and retry.',
    }
  }

  return {
    kind: 'other',
    message:
      shape.message ?? (error instanceof Error ? error.message : 'Unexpected error'),
  }
}

export type OverrideValueType = 'Boolean' | 'Integer' | 'Decimal' | 'String'

/**
 * The server validates `value` against the catalog type for `key`, so the client must send the
 * JSON shape the key expects. The delivered form hard-coded `valueType: "boolean"` and sent the
 * raw form string, which the server rejects with a `400` for every non-boolean key.
 */
export function inferOverrideValueType(key: string): OverrideValueType {
  const normalized = key.trim().toLowerCase()
  if (normalized.startsWith('feature:')) return 'Boolean'
  if (
    normalized.startsWith('max_') ||
    normalized.includes('count') ||
    normalized.endsWith(':limit')
  ) {
    return 'Integer'
  }
  if (
    normalized.startsWith('blossoms.') ||
    normalized.includes('amount') ||
    normalized.includes('price')
  ) {
    return 'Decimal'
  }
  return 'String'
}

export function coerceOverrideValue(
  value: string,
  valueType: OverrideValueType,
): number | boolean | string {
  switch (valueType) {
    case 'Boolean':
      return value.trim().toLowerCase() === 'true'
    case 'Integer': {
      const parsed = Number.parseInt(value.trim(), 10)
      if (Number.isNaN(parsed)) throw new Error('Value must be an integer for this key.')
      return parsed
    }
    case 'Decimal': {
      const parsed = Number(value.trim())
      if (Number.isNaN(parsed)) throw new Error('Value must be a number for this key.')
      return parsed
    }
    default:
      return value
  }
}
