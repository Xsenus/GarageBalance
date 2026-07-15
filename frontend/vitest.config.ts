import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    globals: true,
    testTimeout: 15000,
    fileParallelism: true,
    maxWorkers: process.env.VITEST_MAX_WORKERS ?? '50%',
    slowTestThreshold: 1000,
    coverage: {
      provider: 'v8',
      reporter: ['text-summary', 'json-summary'],
      thresholds: {
        statements: 78,
        branches: 69,
        functions: 74,
        lines: 79,
      },
    },
  },
})
