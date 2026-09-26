import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

/**
 * The date-range control: 7 d / 30 d / 90 d / 12 m, over the `from`/`to` query contract.
 *
 * A preset `ToggleGroup` rather than a calendar primitive (DR-3): it matches the delivered
 * precedent, needs no new shadcn install, and stays trivially testable. The URL contract is still
 * `from`/`to`, so a custom picker later is purely additive.
 *
 * The non-empty guard is load-bearing: Radix's single `ToggleGroup` emits `""` on **deselect**,
 * and clearing the range would leave the page with no window at all.
 */
export type RangePreset = '7d' | '30d' | '90d' | '12m'

const PRESETS: ReadonlyArray<{ value: RangePreset; label: string; days: number }> = [
  { value: '7d', label: '7 d', days: 7 },
  { value: '30d', label: '30 d', days: 30 },
  { value: '90d', label: '90 d', days: 90 },
  { value: '12m', label: '12 m', days: 365 },
]

/** The window a preset means, as the `from`/`to` pair the endpoints take. */
export function presetWindow(
  preset: RangePreset,
  now: Date = new Date(),
): { from: string; to: string } {
  const days = PRESETS.find((candidate) => candidate.value === preset)?.days ?? 30
  const from = new Date(now.getTime() - days * 86_400_000)
  return { from: from.toISOString(), to: now.toISOString() }
}

/** The granularity that keeps a preset's bucket count readable. */
export function presetGranularity(preset: RangePreset): 'day' | 'week' | 'month' {
  if (preset === '12m') return 'month'
  if (preset === '90d') return 'week'
  return 'day'
}

export function RangePresets({
  value,
  onChange,
}: {
  value: RangePreset
  onChange: (preset: RangePreset) => void
}) {
  return (
    <ToggleGroup
      type="single"
      value={value}
      onValueChange={(next) => {
        // Radix emits "" on deselect; a range must always be set.
        if (next !== '') onChange(next as RangePreset)
      }}
    >
      {PRESETS.map((preset) => (
        <ToggleGroupItem key={preset.value} value={preset.value} className="text-xs">
          {preset.label}
        </ToggleGroupItem>
      ))}
    </ToggleGroup>
  )
}
