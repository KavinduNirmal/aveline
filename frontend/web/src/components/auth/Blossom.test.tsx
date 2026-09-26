import { describe, expect, it } from 'vitest'
import { renderToString } from 'react-dom/server'

import { Blossom } from './Blossom'

/**
 * The counter-sway CSS is declared once, globally.
 *
 * It used to be re-declared by a `<style>` element inside every `<Blossom animateCounter>`
 * instance. On the landing page that was ~105 identical `<style>` nodes — 7 `AuroraField` mounts
 * x 14 blossoms, plus the explicit call sites — each asking the style engine to parse and match
 * the same two keyframes again, against a page Lighthouse already measures at 4.4 s of
 * "Style & Layout". These assertions exist so the injection cannot come back: the mechanism is
 * invisible to every other test in the repository.
 */
describe('Blossom', () => {
  it('injects no <style> element, however many instances are mounted', () => {
    const html = renderToString(
      <div>
        {Array.from({ length: 20 }, (_, index) => (
          <Blossom key={index} animateCounter counterDuration={14} />
        ))}
      </div>,
    )
    expect(html).not.toContain('<style')
    expect(html).not.toContain('@keyframes')
  })

  it('carries the per-instance duration as a custom property', () => {
    const html = renderToString(<Blossom animateCounter counterDuration={9} />)
    expect(html).toContain('--aveline-sway-duration:9s')
  })

  it('does not carry a sway duration when the sway is off', () => {
    const html = renderToString(<Blossom counterDuration={9} />)
    expect(html).not.toContain('--aveline-sway-duration')
  })

  it('applies the sway classes only when animateCounter is set', () => {
    expect(renderToString(<Blossom animateCounter />)).toContain('aveline-petal-layer-base')
    expect(renderToString(<Blossom animateCounter />)).toContain('aveline-petal-layer-top')
    expect(renderToString(<Blossom />)).not.toContain('aveline-petal-layer-base')
  })

  it('still renders its petals, so the sway change is not a visual removal', () => {
    const html = renderToString(<Blossom animateCounter className="size-4" />)
    expect(html.match(/<ellipse/g)).toHaveLength(8)
    expect(html).toContain('size-4')
  })
})
