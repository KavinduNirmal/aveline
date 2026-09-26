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

// jsdom implements no `IntersectionObserver`, and `motion`'s `whileInView` reaches for it on mount.
// The landing page's `Reveal` wrapper uses exactly that, so without this stub a render test dies on
// a missing browser API rather than on the thing it is testing. The observer never fires, which is
// fine: the element and its children are still committed to the DOM.
if (typeof window !== 'undefined' && typeof window.IntersectionObserver !== 'function') {
  class MockIntersectionObserver {
    readonly root = null
    readonly rootMargin = ''
    readonly thresholds: ReadonlyArray<number> = []
    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords(): IntersectionObserverEntry[] {
      return []
    }
  }

  Object.defineProperty(window, 'IntersectionObserver', {
    writable: true,
    value: MockIntersectionObserver,
  })
}

// Testing Library does not auto-clean in Vitest; without this a second render in the same
// file sees the first file's DOM.
afterEach(() => {
  cleanup()
})

// jsdom implements neither `hasPointerCapture` nor `setPointerCapture`, and Radix's `Select` calls
// the first one while opening. Without this stub, *clicking* a Select throws an uncaught error that
// Vitest reports at the end of the whole run rather than failing the test that caused it — so the
// symptom is a mysterious global error and a green suite. Stubbing it is the fix; avoiding the click
// would leave the console's only filter control untested.
if (typeof window !== 'undefined') {
  // `scrollIntoView` is the third one the same primitive reaches for.
  if (typeof Element.prototype.scrollIntoView !== 'function') {
    Object.defineProperty(Element.prototype, 'scrollIntoView', {
      writable: true,
      value: () => undefined,
    })
  }

  for (const method of ['hasPointerCapture', 'setPointerCapture', 'releasePointerCapture'] as const) {
    if (typeof (Element.prototype as unknown as Record<string, unknown>)[method] !== 'function') {
      Object.defineProperty(Element.prototype, method, {
        writable: true,
        value: () => false,
      })
    }
  }
}
