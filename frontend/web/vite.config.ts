/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5091',
        changeOrigin: true,
      },
    },
  },
  test: {
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary', 'html'],
      exclude: [
        'node_modules/**',
        'dist/**',
        'coverage/**',
        // The tenant surface stays out of the denominator, exactly as it was.
        'src/components/**',
        'src/contexts/**',
        'src/routes/**',
        // Test infrastructure: the C# catalog parser and the DOM setup are not shipped code.
        'src/test/**',
        '**/*.d.ts',
        '**/*.test.*',
      ],
      // Thresholds are **glob-scoped** rather than global (strategy C3). The admin subtree is
      // deliberately not in this run: it is excluded above and measured by its own run
      // (`bun run test:coverage:admin`), so an admin slice can never move the tenant number
      // and the tenant number can never block an admin slice. The globs below reproduce the
      // repository's existing floor exactly (lines 80 / functions 70 / branches 70 /
      // statements 80) over the same surface the global threshold used to cover.
      thresholds: {
        'src/lib/**': { lines: 80, functions: 70, branches: 70, statements: 80 },
        'src/hooks/**': { lines: 80, functions: 70, branches: 70, statements: 80 },
        'src/types/**': { lines: 80, functions: 70, branches: 70, statements: 80 },
        'src/*.{ts,tsx}': { lines: 80, functions: 70, branches: 70, statements: 80 },
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
          // Vitest's 5 s default is a liveness bound, not an assertion, and it is too tight for
          // the admin DOM tests: several drive `userEvent.type` across a whole filter form, and
          // v8 coverage instrumentation plus a 2-core CI runner pushed
          // `AdminAudit.dom.test.tsx` past 5 s — it failed in CI while passing locally, twice.
          // This bound still catches a genuine hang; it just stops reporting one for a slow test.
          testTimeout: 20_000,
        },
      },
    ],
  },
})
