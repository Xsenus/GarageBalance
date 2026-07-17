if (typeof document !== 'undefined') {
  await import('@testing-library/jest-dom/vitest')
  const { configure } = await import('@testing-library/dom')

  // Parallel CI workers can briefly contend for CPU while React commits async UI
  // updates. Keep user-facing waits bounded, but allow enough headroom to avoid
  // treating scheduler contention as a product failure.
  configure({ asyncUtilTimeout: 5000 })
}
