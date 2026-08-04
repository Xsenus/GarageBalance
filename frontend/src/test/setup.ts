if (typeof document !== 'undefined') {
  await import('@testing-library/jest-dom/vitest')
  const { configure } = await import('@testing-library/dom')

  // Parallel CI workers can briefly contend for CPU while React commits async UI
  // updates. Keep user-facing waits bounded, but allow enough headroom to avoid
  // treating scheduler contention as a product failure.
  configure({ asyncUtilTimeout: 5000 })

  // Export helpers intentionally click a temporary download link. JSDOM does
  // not implement document navigation and otherwise reports a misleading
  // asynchronous "Not implemented: navigation" error after a successful test.
  // Individual export tests still spy on this method and verify the click.
  Object.defineProperty(HTMLAnchorElement.prototype, 'click', {
    configurable: true,
    writable: true,
    value: () => undefined,
  })
}
