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
    environment: 'node',
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary', 'html'],
      exclude: [
        'node_modules/**',
        'dist/**',
        'coverage/**',
        'src/components/**',
        'src/contexts/**',
        'src/routes/**',
        '**/*.d.ts',
        '**/*.test.*',
      ],
      thresholds: {
        lines: 80,
        functions: 70,
        branches: 70,
        statements: 80,
      },
    },
  },
})
