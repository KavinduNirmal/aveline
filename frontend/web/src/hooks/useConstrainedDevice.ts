import { useState } from 'react'

/**
 * Whether this device should be spared the decorative animation budget.
 *
 * The landing page mounts seven `AuroraField`s — 42 infinitely-animating blurred layers and 98
 * infinitely-animating blossoms — and nothing anywhere in the app consulted
 * `navigator.connection`, `deviceMemory` or `saveData`, so an entry-level Android ran exactly the
 * same GPU and style-recalculation bill as a flagship. This is the signal that lets the
 * decoration step aside.
 *
 * Read once, synchronously, in the state initialiser rather than in an effect: the app is a
 * client-only SPA, so the values are already available on the first render and an effect would
 * render the full animation budget first and then unmount it.
 *
 * Thresholds are deliberately conservative and deliberate:
 *   - `saveData` is an explicit request from the reader to use less data.
 *   - `deviceMemory <= 4` is the entry-level/mid-tier Android band — the plan's primary target.
 *   - 2G-class `effectiveType` cannot stream a decorative animation and the content at once.
 *
 * `hardwareConcurrency` is deliberately NOT consulted: a 4-core desktop is not a constrained
 * device, and gating on it would remove the decoration for most laptops.
 */
export function useConstrainedDevice(): boolean {
  const [constrained] = useState<boolean>(() => isConstrainedDevice())
  return constrained
}

/** Exported for the test, which has to exercise each signal without a real constrained device. */
export function isConstrainedDevice(): boolean {
  if (typeof navigator === 'undefined') return false

  const nav = navigator as Navigator & {
    connection?: { saveData?: boolean; effectiveType?: string }
    deviceMemory?: number
  }

  if (nav.connection?.saveData === true) return true
  if (typeof nav.deviceMemory === 'number' && nav.deviceMemory <= 4) return true

  const effectiveType = nav.connection?.effectiveType
  if (effectiveType === 'slow-2g' || effectiveType === '2g') return true

  return false
}
