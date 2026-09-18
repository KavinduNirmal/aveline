import { describe, expect, it } from 'vitest'
import { renderToString } from 'react-dom/server'

import { AvelineAvatar } from './AvelineAvatar'

describe('AvelineAvatar', () => {
  it('renders the blossom without a background circle', () => {
    const html = renderToString(<AvelineAvatar />)
    expect(html).toContain('aveline-waiting')
    expect(html).not.toContain('bg-primary')
  })

  it('applies the colour-cycle class in the idle state', () => {
    const html = renderToString(<AvelineAvatar state="idle" />)
    expect(html).toContain('aveline-waiting')
    expect(html).toContain('aveline-breathing')
  })

  it('thinking applies the pulse animation', () => {
    const html = renderToString(<AvelineAvatar state="thinking" />)
    expect(html).toContain('aveline-pulse')
  })

  it('searching renders ripple rings', () => {
    const html = renderToString(<AvelineAvatar state="searching" />)
    expect(html).toContain('aveline-ripple-ring')
  })

  it('processing spins', () => {
    const html = renderToString(<AvelineAvatar state="processing" />)
    expect(html).toContain('aveline-spin')
  })

  it('tool_call uses stepped rotation', () => {
    const html = renderToString(<AvelineAvatar state="tool_call" />)
    expect(html).toContain('aveline-spin-steps')
  })

  it('error applies the shake animation', () => {
    const html = renderToString(<AvelineAvatar state="error" />)
    expect(html).toContain('aveline-shake')
  })

  it('success applies the bloom animation', () => {
    const html = renderToString(<AvelineAvatar state="success" />)
    expect(html).toContain('aveline-bloom')
  })
})
