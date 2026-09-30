import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, it } from 'vitest'

const css = readFileSync(resolve(process.cwd(), 'src/features/reports/serviceReports.css'), 'utf8')
const sharedCss = readFileSync(resolve(process.cwd(), 'src/App.css'), 'utf8')

it('matches the existing workbook grid, header and footer palette while preserving virtual row geometry', () => {
  for (const declaration of ['border-right: 1px solid #111827;', 'border-bottom: 1px solid #111827;', 'padding: 5px 7px;', 'background: #f8fafc;', 'background: #fff1e8;', 'font-weight: 850;', 'font-size: 13px;']) {
    expect(css).toContain(declaration)
    expect(sharedCss).toContain(declaration)
  }
  expect(css).toContain('.service-report-table tbody tr:not([aria-hidden]) { height: 38px; }')
  expect(css).toContain('position: sticky; top: 0;')
  expect(css).toContain('position: sticky; bottom: 0;')
})

it('fills the available debt report height through every flex parent without a fixed table cap', () => {
  expect(css).toContain('.report-tab-panel:has(.service-report-panel--debt) { display: flex; flex: 1 1 auto; min-height: 0; }')
  expect(css).toContain('.report-workbook-sheet:has(.service-report-panel--debt) { flex: 1 1 auto; min-height: 0; }')
  expect(css).toContain('.service-report-panel--debt { display: flex; flex: 1 1 auto; flex-direction: column; gap: 12px; }')
  expect(css).toContain('.service-report-panel--debt .service-report-scroll { flex: 1 1 auto; min-height: 180px; max-height: none; }')
})

it('keeps the footer at the bottom for short or empty debt reports and lets mode controls wrap', () => {
  expect(css).toContain('.service-report-panel--debt .service-report-table { height: 100%; }')
  expect(css).toContain('.service-report-fill-row td { height: 100%; padding: 0; border: 0; }')
  expect(css).toContain('.service-report-modes { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }')
})
