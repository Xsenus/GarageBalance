import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/dom'

// Parallel CI workers can briefly contend for CPU while React commits async UI
// updates. Keep user-facing waits bounded, but allow enough headroom to avoid
// treating scheduler contention as a product failure.
configure({ asyncUtilTimeout: 5000 })
