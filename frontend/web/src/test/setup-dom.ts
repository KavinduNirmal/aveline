import '@testing-library/jest-dom/vitest'

import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// jsdom implements no `matchMedia`, and the shadcn `sidebar` primitive reads it through
// `use-mobile`. Without this stub every shell test fails on `window.matchMedia is not a
// function` rather than on the thing it is testing.
if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => undefined,
      removeListener: () => undefined,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      dispatchEvent: () => false,
    }),
  })
}

// Testing Library does not auto-clean in Vitest; without this a second render in the same
// file sees the first file's DOM.
afterEach(() => {
  cleanup()
})
