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

  it('keeps detail dialogs named, described and modal', () => {
    const dialogLines = appSource
      .split(/\r?\n/)
      .map((line, index) => ({ index: index + 1, line: line.trim() }))
      .filter(({ line }) => line.includes('className="detail-dialog"'))

    expect(dialogLines.length).toBeGreaterThan(0)
    expect(dialogLines.filter(({ line }) => !line.includes('role="dialog"'))).toEqual([])
    expect(dialogLines.filter(({ line }) => !line.includes('aria-modal="true"'))).toEqual([])
    expect(dialogLines.filter(({ line }) => !line.includes('aria-labelledby='))).toEqual([])
    expect(dialogLines.filter(({ line }) => !line.includes('aria-describedby='))).toEqual([])
  })

  it('keeps dictionary disclosure controls linked to their list', () => {
    expect(appSource).toContain('<ul className="dictionary-list" id={listId}>')

    const disclosureControlLines = appSource
      .split(/\r?\n/)
      .map((line, index) => ({ index: index + 1, line: line.trim() }))
      .filter(({ line }) => line.includes('setShowAllItems((value) => !value)'))

    expect(disclosureControlLines.length).toBeGreaterThan(0)
    expect(disclosureControlLines.filter(({ line }) => !line.includes('aria-controls={listId}'))).toEqual([])
    expect(disclosureControlLines.filter(({ line }) => !line.includes('aria-expanded={showAllItems}'))).toEqual([])
  })
})
