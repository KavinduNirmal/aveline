function required(name: string, value: string | undefined): string {
  if (!value) {
    throw new Error(
      `${name} is not set. Copy .env.example to .env.local and fill it in.`,
    )
  }
  return value
}

/**
 * Clerk publishable key (`pk_...`).
 *
 * A function (not a module-level const) so importing this module — e.g. in unit
 * tests — does not throw when the key is only inlined at build/runtime.
 */
export function clerkPublishableKey(): string {
  return required(
    'VITE_CLERK_PUBLISHABLE_KEY',
    import.meta.env.VITE_CLERK_PUBLISHABLE_KEY,
  )
}

/** Aveline API base URL. */
export const apiBaseUrl =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5091'
