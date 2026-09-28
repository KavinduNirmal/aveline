import { describe, expect, it } from 'vitest'

import { DIALOG_CONTENT_WIDE } from './dialog'
import { cn } from '@/lib/utils'

/**
 * The width contract for `DialogContent`.
 *
 * The defect this pins was silent: `DialogContent` sets `sm:max-w-lg`, callers passed `max-w-2xl`,
 * and tailwind-merge kept **both** because they are different utilities — after which the `sm:` one
 * won from 640px up. The dialog rendered at 512px however wide the class looked, and nothing failed.
 *
 * These assertions run the real `cn`, so they test the merge rather than a hand-written expectation
 * of it.
 */
const PRIMITIVE =
  'grid w-full min-w-0 max-w-[calc(100%-2rem)] gap-4 rounded-xl border p-6 sm:max-w-lg'

const widthClasses = (value: string) => value.split(/\s+/).filter((c) => /(?:^|:)max-w-/.test(c))

describe('DialogContent width contract', () => {
  it('lets the wide variant replace the primitive default at the same breakpoint', () => {
    const merged = cn(PRIMITIVE, DIALOG_CONTENT_WIDE)

    expect(merged).toContain('sm:max-w-3xl')
    // The default must be *replaced*, not joined: two `sm:max-w-*` classes would resolve by
    // stylesheet order, which is not something a caller can reason about.
    expect(merged).not.toContain('sm:max-w-lg')
    expect(widthClasses(merged)).toEqual(['max-w-[calc(100%-2rem)]', 'sm:max-w-3xl'])
  })

  it('keeps the viewport guard when a caller asks for a wide dialog', () => {
    // `max-w-[calc(100%-2rem)]` is the only thing stopping a dialog from exceeding the screen. It
    // must not be replaced by a fixed width, which is what an unprefixed `max-w-*` would do.
    const merged = cn(PRIMITIVE, DIALOG_CONTENT_WIDE)

    expect(merged).toContain('max-w-[calc(100%-2rem)]')
  })

  it('demonstrates the mistake it exists to prevent', () => {
    // Deliberately asserting the broken form, so the gotcha is recorded as behaviour rather than as
    // a comment. Two things go wrong at once, and the second is the dangerous one:
    //
    //  1. `sm:max-w-lg` is *not* replaced, and it wins from 640px up — so the dialog renders at
    //     512px and the caller's width appears to have been ignored.
    //  2. `max-w-3xl` does replace `max-w-[calc(100%-2rem)]`, because tailwind-merge reads both as
    //     the same utility. That silently removes the viewport guard, so below 640px the dialog is
    //     a fixed 768px wide — on a 390px phone, wider than the screen.
    const naive = cn(PRIMITIVE, 'max-w-3xl')

    expect(widthClasses(naive)).toEqual(['sm:max-w-lg', 'max-w-3xl'])
    expect(naive).not.toContain('max-w-[calc(100%-2rem)]')
  })
})
