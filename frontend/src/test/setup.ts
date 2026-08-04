if (typeof document !== 'undefined') {
  await import('@testing-library/jest-dom/vitest')
  const { configure } = await import('@testing-library/dom')
  const { afterEach } = await import('vitest')

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

  const failOnUnexpectedConsoleOutput = (method: 'error' | 'warn') => {
    const unexpectedMessages: string[] = []
    console[method] = (...values: unknown[]) => {
      const message = values
        .map((value) => value instanceof Error ? value.stack ?? value.message : String(value))
        .join(' ')
      // React reports this development-only diagnostic when a deliberately
      // lazy workspace chunk first suspends inside Testing Library's event act.
      // Dedicated workspace tests assert the Suspense skeleton and chunk-error
      // recovery; every other warning/error remains a hard failure.
      if (method === 'error' && message.startsWith('A component suspended inside an `act` scope')) return
      unexpectedMessages.push(message)
    }
    afterEach(() => {
      if (unexpectedMessages.length === 0) return
      const messages = unexpectedMessages.splice(0)
      throw new Error(`Unexpected console.${method}: ${messages.join('\n\n')}`)
    })
  }

  // Browser warnings and errors are part of the observable quality gate. Tests
  // that intentionally exercise an error boundary must spy on the corresponding
  // method explicitly; every other console diagnostic is a regression.
  failOnUnexpectedConsoleOutput('error')
  failOnUnexpectedConsoleOutput('warn')
}
