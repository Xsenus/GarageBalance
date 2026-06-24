import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('accessible dynamic messages', () => {
  const appSource = readFileSync(resolve(process.cwd(), 'src', 'App.tsx'), 'utf8')

  it('keeps polite live regions exposed as statuses in the main workspace', () => {
    const liveRegionLines = appSource
      .split(/\r?\n/)
      .map((line, index) => ({ index: index + 1, line: line.trim() }))
      .filter(({ line }) => line.includes('aria-live="polite"'))

    expect(liveRegionLines.length).toBeGreaterThan(0)
    expect(liveRegionLines.filter(({ line }) => !line.includes('role="status"'))).toEqual([])
  })

  it('keeps shared form errors and validation summaries exposed as alerts', () => {
    expect(appSource).toContain('<div className="form-error" role="alert">')
    expect(appSource).toContain('<div className="form-error validation-summary" role="alert" aria-label={title}>')
  })
})
