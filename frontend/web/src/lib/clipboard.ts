/**
 * Clipboard write with a fallback.
 *
 * `navigator.clipboard` is unavailable on a non-secure origin, which is exactly how the dashboard
 * is reached on a boutique's own LAN host, so the copy falls back to a selection the browser will
 * honour there rather than reporting a failure that is not the associate's fault.
 */
export async function writeClipboard(text: string): Promise<void> {
  if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text)
    return
  }
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.top = '-1000px'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  try {
    if (!document.execCommand?.('copy')) throw new Error('copy refused')
  } finally {
    document.body.removeChild(area)
  }
}
