import { afterEach, describe, expect, it, vi } from 'vitest'

import { writeClipboard } from './clipboard'

const originalClipboard = navigator.clipboard

function stubClipboard(value: unknown): void {
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value })
}

function stubExecCommand(value: unknown): void {
  Object.defineProperty(document, 'execCommand', { configurable: true, value })
}

afterEach(() => {
  stubClipboard(originalClipboard)
  // Drop the own property so jsdom's prototype is consulted again, rather than leaking a stub.
  Reflect.deleteProperty(document, 'execCommand')
  vi.restoreAllMocks()
})

describe('writeClipboard', () => {
  it('uses the async clipboard when the origin offers one', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    stubClipboard({ writeText })

    await writeClipboard('hello')

    expect(writeText).toHaveBeenCalledWith('hello')
  })

  it('falls back to a selection when the origin has no clipboard API', async () => {
    // A boutique reaching the dashboard on a plain-HTTP LAN host has no `navigator.clipboard`,
    // so the copy has to go through a selection the browser will honour there.
    stubClipboard(undefined)
    const execCommand = vi.fn().mockReturnValue(true)
    stubExecCommand(execCommand)

    await writeClipboard('hello')

    expect(execCommand).toHaveBeenCalledWith('copy')
    // The scratch textarea must not be left behind in the document.
    expect(document.querySelectorAll('textarea')).toHaveLength(0)
  })

  it('reports a refused fallback copy rather than claiming success', async () => {
    stubClipboard(undefined)
    stubExecCommand(vi.fn().mockReturnValue(false))

    await expect(writeClipboard('hello')).rejects.toThrow('copy refused')
    expect(document.querySelectorAll('textarea')).toHaveLength(0)
  })
})
