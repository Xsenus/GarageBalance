import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('responsive layout styles', () => {
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')

  it('collapses the main shell and data rows on tablet width', () => {
    expect(appCss).toContain('@media (max-width: 980px)')
    expect(appCss).toContain('.app-shell {\n    grid-template-columns: 1fr;')
    expect(appCss).toContain('.user-table-row,\n  .operation-row {\n    grid-template-columns: 1fr;')
  })

  it('keeps mobile navigation usable without stretching the viewport', () => {
    expect(appCss).toContain('@media (max-width: 640px)')
    expect(appCss).toContain('.nav-list {\n    display: flex;\n    overflow-x: auto;')
    expect(appCss).toContain('.nav-item {\n    min-width: 160px;')
    expect(appCss).toContain('.operation-list {\n    overflow-x: hidden;')
  })

  it('allows long table cell text to shrink inside its grid column', () => {
    expect(appCss).toContain('.nav-item {\n  display: flex;\n  width: 100%;\n  min-width: 0;')
    expect(appCss).toContain('.user-table-row > *,\n.operation-row > * {\n  min-width: 0;')
  })
})
