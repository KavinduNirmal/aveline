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
        // The ratchet. A0 set the floor to 0; each slice raised it to the value that slice
        // achieved, and the floor never exceeds the achieved value — a ratchet that blocks is a
        // ratchet that gets removed.
        //
        // Achieved with the business-KPI surface (P1–P6) and the landing-page KPIs: routes/admin
        // 78.24% lines / 64.52% branches; components/admin 29.41% lines / 19.35% branches;
        // AdminSessionContext 64.4% lines / 55.88% branches; the whole admin subtree 78.55% lines.
        // The `components/admin` aggregate stays low because `components/admin/shell` is exercised
        // structurally (one geometry container, a skip link, the panel) rather than coveraged to
        // death, which is what the Playwright walk is for. Each floor is set just below the achieved
        // value so the gate never blocks on noise.
        'src/routes/admin/**': { lines: 77, functions: 75, branches: 63, statements: 77 },
        'src/components/admin/**': { lines: 28, functions: 18, branches: 9, statements: 30 },
        'src/contexts/AdminSessionContext.tsx': {
          lines: 63,
          functions: 70,
          branches: 52,
          statements: 63,
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
