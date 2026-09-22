import { describe, expect, it } from 'vitest'

import { DASHBOARD_WINDOWS, resolveWindowRange } from './useDashboardWindow'

/**
 * The window is lifted to the shell so every KPI panel reads one value. These tests pin the two
 * things that would make two panels disagree: the boundary arithmetic, and the preset list.
 */
describe('resolveWindowRange', () => {
  const now = new Date('2026-09-20T15:30:00Z')

  it('resolves a rolling window by subtracting days', () => {
    expect(resolveWindowRange('7d', now).from).toBe('2026-09-13T15:30:00.000Z')
    expect(resolveWindowRange('30d', now).from).toBe('2026-08-21T15:30:00.000Z')
    expect(resolveWindowRange('90d', now).from).toBe('2026-06-22T15:30:00.000Z')
  })

  it('resolves month-to-date to the first of the month in UTC', () => {
    // The server's `mtd` starts at 00:00 UTC on the first, so a client range that used local
    // midnight would describe a different period by up to a day.
    expect(resolveWindowRange('mtd', now).from).toBe('2026-09-01T00:00:00.000Z')
  })

  it('resolves year-to-date to the first of January in UTC', () => {
    expect(resolveWindowRange('ytd', now).from).toBe('2026-01-01T00:00:00.000Z')
  })

  it('never produces a range whose start is after its end', () => {
    for (const preset of DASHBOARD_WINDOWS) {
      const range = resolveWindowRange(preset.value, now)
      expect(new Date(range.from).getTime(), preset.value).toBeLessThan(new Date(range.to).getTime())
    }
  })

  it('offers every window the server accepts, and no others', () => {
    // A preset the server rejects would render a 400 as a broken panel.
    expect(DASHBOARD_WINDOWS.map((w) => w.value)).toEqual(['7d', '30d', '90d', 'mtd', 'ytd'])
  })

  it('labels the windows in the owner\'s words rather than the wire tokens', () => {
    expect(DASHBOARD_WINDOWS.find((w) => w.value === 'mtd')?.label).toBe('This month')
    expect(DASHBOARD_WINDOWS.every((w) => w.label.length > 0)).toBe(true)
  })
})
