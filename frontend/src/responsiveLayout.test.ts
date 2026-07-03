import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('responsive layout styles', () => {
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
  const normalizedAppCss = appCss.replace(/\r\n/g, '\n')

  it('collapses the main shell and data rows on tablet width', () => {
    expect(appCss).toContain('@media (max-width: 980px)')
    expect(normalizedAppCss).toContain('.app-shell {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.user-table-row,\n  .operation-row {\n    grid-template-columns: 1fr;')
  })

  it('keeps mobile navigation usable without stretching the viewport', () => {
    expect(appCss).toContain('@media (max-width: 640px)')
    expect(normalizedAppCss).toContain('.app-shell {\n    overflow-x: hidden;')
    expect(normalizedAppCss).toContain('.nav-list {\n    display: flex;\n    width: 100%;\n    max-width: 100%;\n    overflow-x: auto;')
    expect(normalizedAppCss).toContain('.nav-item {\n    flex: 0 0 160px;\n    min-width: 160px;')
    expect(normalizedAppCss).toContain('.operation-list {\n    overflow-x: hidden;')
  })

  it('allows long table cell text to shrink inside its grid column', () => {
    expect(normalizedAppCss).toContain('.nav-item {\n  display: flex;\n  width: 100%;\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.user-table-row > *,\n.operation-row > * {\n  min-width: 0;')
  })

  it('keeps tall dialogs scrollable inside the viewport', () => {
    expect(normalizedAppCss).toContain('.detail-dialog {\n  width: min(560px, 100%);\n  max-height: min(860px, calc(100dvh - 48px));\n  overflow-y: auto;')
    expect(normalizedAppCss).toContain('.detail-dialog-header {\n  position: sticky;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions {\n  position: sticky;')
  })
})
