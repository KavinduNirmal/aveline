/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

/**
 * The tenant-dashboard subtree coverage run (strategy TD17).
 *
 * The tenant surface used to be measured by **nothing**: `vite.config.ts` excludes
 * `src/components/**`, `src/contexts/**` and `src/routes/**` from the global run, and the admin
 * config includes only admin paths. A file under `src/components/shared/**` was therefore
 * measured by no run at all, which is exactly why this config exists.
 *
 * It mirrors `vitest.admin-coverage.config.ts`: `include`-only (a gate added after the work is a
 * gate that measures nothing), its own reports directory, and a **ratchet** that starts at 0 and
 * is raised in the final slice to the value actually achieved. It is deliberately its own run so a
 * tenant regression can never be blamed on the console, and an admin slice can never move the
 * tenant number.
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
      reportsDirectory: './coverage/dashboard',
      include: [
        'src/components/dashboard/**/*.{ts,tsx}',
        'src/components/shared/**/*.{ts,tsx}',
        'src/lib/dashboard-api.ts',
        'src/lib/income-api.ts',
        'src/lib/customers-api.ts',
        'src/lib/team-api.ts',
        'src/lib/billing-api.ts',
        'src/lib/statistics-api.ts',
        'src/lib/approvals-api.ts',
        'src/lib/settings-api.ts',
        'src/hooks/useDashboardWindow.ts',
      ],
      exclude: ['**/*.test.*', '**/*.d.ts'],
      thresholds: {
        // The ratchet. It started at 0 in T0b because the slice that adds the gate must not be
        // blocked by files it has not written yet; T7 raised every floor to the value the shipped
        // tenant-dashboard subtree actually achieves. The floor never exceeds the achieved value —
        // a ratchet that blocks is a ratchet that gets removed.
        //
        // Achieved with T0a–T6 (measured, then raised here in T7):
        //   components/dashboard (recursive)  45.05% lines / 42.75% branches / 31.87% functions / 43.42% statements
        //   hooks/useDashboardWindow          100% lines / 100% branches / 100% functions / 100% statements
        //   lib aggregate                     90.47% lines / 85.39% branches / 83.54% functions / 90.90% statements
        // Raised again with the in-card sparkline slice (monospaced numerals, the tenant
        // `CONNECT_NULLS` gate, `KpiSparkline`, and the one `revenue-series` read behind the two
        // revenue tiles). Measured **twice** over the final tree, identical both times:
        //   components/dashboard (recursive)  53.01% lines / 46.15% branches / 42.70% functions / 51.26% statements
        // The new files land at 100% (`KpiSparkline.tsx`, `KpiCard.tsx`, `TakingsCard.tsx` functions
        // aside), and `Overview.tsx` rose on the three tests the series read added, so the aggregate
        // moved rather than being propped up by a smaller denominator.
        //
        // The dashboard glob's percentage is **not** the text reporter's `...ents/dashboard` row: that
        // row rolls up direct children only, while this glob matches every subdirectory. Six
        // direct-child components are still at 0 — the shell (`DashboardShell`, `BrandIcons`,
        // `NotificationBell`, `SectionPlaceholder`) and the two panels T1/T6 rewired but never
        // unit-tested (`IntegrationsPanel`, `TeamManagement`) — and they are named here rather than
        // hidden, because a floor that quietly excluded them would be a gate that measures less than
        // it claims. `lib/dashboard-api.ts` was at 9% until T7 gave it its own wire-shape test, and
        // `useDashboardWindow.ts` was at 25% functions until it gained a DOM test; both are now at
        // 100. Each floor is set just below the achieved value — or at it, where it is 100%, which is
        // the only floor that still gates — so the gate never blocks on rounding noise.
        'src/components/dashboard/**': { lines: 52, functions: 41, branches: 45, statements: 50 },
        // No file has ever landed here: T4 kept the shared primitives in `components/dashboard/**`
        // instead of promoting them, so this glob currently matches nothing. The floor stays at 0
        // rather than being deleted, so the directory is measured from the moment it first exists.
        'src/components/shared/**': { lines: 0, functions: 0, branches: 0, statements: 0 },
        'src/hooks/useDashboardWindow.ts': { lines: 100, functions: 100, branches: 100, statements: 100 },
        'src/lib/approvals-api.ts': { lines: 100, functions: 100, branches: 100, statements: 100 },
        'src/lib/billing-api.ts': { lines: 93, functions: 92, branches: 76, statements: 93 },
        'src/lib/customers-api.ts': { lines: 100, functions: 100, branches: 85, statements: 100 },
        'src/lib/dashboard-api.ts': { lines: 100, functions: 100, branches: 100, statements: 100 },
        'src/lib/income-api.ts': { lines: 100, functions: 100, branches: 95, statements: 100 },
        'src/lib/settings-api.ts': { lines: 100, functions: 100, branches: 50, statements: 100 },
        'src/lib/statistics-api.ts': { lines: 72, functions: 52, branches: 77, statements: 73 },
        'src/lib/team-api.ts': { lines: 100, functions: 100, branches: 100, statements: 100 },
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
          testTimeout: 20_000,
        },
      },
    ],
  },
})
