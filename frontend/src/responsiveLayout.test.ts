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
    expect(normalizedAppCss).toContain('.modal-backdrop {\n  position: fixed;\n  inset: 0;\n  z-index: 20;\n  display: grid;\n  place-items: center;\n  overflow-y: auto;')
    expect(normalizedAppCss).toContain('.detail-dialog {\n  width: min(560px, 100%);\n  max-height: min(860px, calc(100dvh - 48px));\n  overflow-y: auto;')
    expect(appCss).toContain('box-sizing: border-box;')
    expect(appCss).toContain('overscroll-behavior: contain;')
    expect(appCss).toContain('scrollbar-gutter: stable;')
    expect(appCss).toContain('overflow-wrap: anywhere;')
    expect(normalizedAppCss).toContain('.detail-dialog-header {\n  position: sticky;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions {\n  position: sticky;')
  })

  it('keeps shared dialogs usable on narrow screens without action overlap', () => {
    expect(appCss).toContain('@media (max-width: 640px)')
    expect(normalizedAppCss).toContain('.modal-backdrop {\n    align-items: start;\n    padding: 12px;')
    expect(normalizedAppCss).toContain('.detail-dialog {\n    width: 100%;\n    max-height: calc(100dvh - 24px);\n    padding: 14px;')
    expect(normalizedAppCss).toContain('.detail-dialog-header {\n    top: -14px;\n    margin: -14px -14px 14px;\n    padding: 14px;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions {\n    bottom: -14px;\n    display: grid;\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions button {\n    width: 100%;')
  })

  it('keeps garage multi-selection compact and responsive', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n  position: absolute;')
    expect(appCss).toContain('grid-template-columns: repeat(3, minmax(190px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-selected-list {\n  display: grid;\n  grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.payments-prototype-garage-summary-value {\n  justify-self: end;\n  text-align: right;')
    expect(normalizedAppCss).toContain('.payments-prototype-owner-row .payments-prototype-actions {\n  flex-wrap: nowrap;')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: repeat(2, minmax(190px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: minmax(0, 1fr);\n    width: calc(100vw - 32px);')
  })

  it('centers fund action columns', () => {
    expect(normalizedAppCss).toContain('.funds-table .funds-table-action-column {\n  text-align: center;')
  })
})
