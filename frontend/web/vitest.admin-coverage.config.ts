/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

/**
 * The admin-subtree coverage run (strategy C3).
 *
 * The tenant surface is measured by `vitest run --coverage` (`bun run test:coverage`) and keeps
 * its existing floor untouched. This config measures **only** the admin subtree — including
 * files no test has loaded yet, which is the point: a gate added after the work is a gate that
 * measures nothing.
 *
 * The threshold below is the **ratchet**. It starts at 0 at A0 and each slice raises it to the
 * value that slice achieved (A8 writes the final number). It is deliberately its own run so a
 * partially-built admin UI can never block the tenant dashboard, and a tenant regression can
 * never be blamed on the console.
 */
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary'],
      reportsDirectory: './coverage/admin',
      include: [
        'src/routes/admin/**/*.{ts,tsx}',
        'src/components/admin/**/*.{ts,tsx}',
        'src/contexts/AdminSessionContext.tsx',
      ],
      exclude: ['**/*.test.*', '**/*.d.ts'],
      thresholds: {
        // The ratchet. A0 set the floor to 0; each slice raises it to the value that slice
        // achieved, and the floor never exceeds the achieved value — a ratchet that blocks is a
        // ratchet that gets removed. A8 records the final numbers.
        //
        // Achieved at A7/A8: routes/admin 68.75% lines / 55.91% branches; the whole admin subtree
        // 68.3% lines. The `components/admin` floor stays low because `components/admin/shell` is
        // at 10% lines and dominates that aggregate — the shell is exercised structurally (one
        // geometry container, a skip link, the panel) rather than coveraged to death, which is
        // what the Playwright walk is for.
        'src/routes/admin/**': { lines: 67, functions: 58, branches: 53, statements: 67 },
        'src/components/admin/**': { lines: 9, functions: 8, branches: 0, statements: 9 },
        'src/contexts/AdminSessionContext.tsx': {
          lines: 66,
          functions: 71,
          branches: 53,
          statements: 64,
        },
      },
    },
    projects: [
      {
        extends: true,
        test: {
          name: 'node',
          environment: 'node',
          include: ['src/**/*.test.{ts,tsx}'],
          exclude: ['**/node_modules/**', '**/*.dom.test.{ts,tsx}'],
        },
      },
      {
        extends: true,
        test: {
          name: 'dom',
          environment: 'jsdom',
          setupFiles: ['./src/test/setup-dom.ts'],
          include: ['src/**/*.dom.test.{ts,tsx}'],
        },
      },
    ],
  },
})
