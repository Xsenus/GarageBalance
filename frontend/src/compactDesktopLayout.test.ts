import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8').replace(/\r\n/g, '\n')
const appShellSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'AppShell.tsx'), 'utf8').replace(/\r\n/g, '\n')
const meterReadingsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'meterReadings', 'MeterReadingsPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const auditSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'audit', 'AuditPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const contractorsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'contractors', 'ContractorsPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')

describe('compact desktop layout contract', () => {
  it('switches to compact geometry by width or viewport height', () => {
    expect(appCss).toContain('@media (max-width: 1499px), (max-height: 849px) {')
    expect(appCss).toContain('--app-sidebar-collapsed-width: 68px;')
    expect(appCss).toContain('--app-sidebar-expanded-width: 224px;')
    expect(appCss).toContain('--app-workspace-padding: 12px;')
    expect(appCss).toContain('--app-dialog-viewport-gap: 24px;')
  })

  it('keeps standard desktop geometry outside compact mode', () => {
    expect(appCss).toContain('--app-sidebar-collapsed-width: 84px;')
    expect(appCss).toContain('--app-sidebar-expanded-width: 280px;')
    expect(appCss).toContain('--app-workspace-padding: 18px;')
    expect(appCss).toContain('--app-dialog-viewport-gap: 48px;')
  })

  it('uses bounded tracks so the workspace cannot widen the application grid', () => {
    expect(appCss).toContain('grid-template-columns: var(--app-sidebar-collapsed-width) minmax(0, 1fr);')
    expect(appCss).toContain('grid-template-columns: var(--app-sidebar-expanded-width) minmax(0, 1fr);')
    expect(appCss).toContain('max-height: min(860px, calc(100dvh - var(--app-dialog-viewport-gap, 48px)));')
    expect(appCss).toContain('top: calc(-1 * var(--app-dialog-padding, 18px));')
    expect(appCss).toContain('bottom: calc(-1 * var(--app-dialog-padding, 18px));')
  })

  it('gives the users matrix the remaining viewport height instead of growing the document', () => {
    expect(appCss).toContain('.workspace--users {\n  display: flex;\n  height: 100dvh;')
    expect(appCss).toContain('.workspace--users > .users-panel-v2 {\n    min-height: 0;\n    flex: 1 1 auto;\n    overflow: hidden;')
    expect(appCss).toContain('.users-panel-v2 .role-matrix {\n    min-height: 0;\n    flex: 1 1 auto;\n    grid-template-rows: auto minmax(0, 1fr);')
  })

  it('marks every large workspace so compact height rules apply consistently', () => {
    for (const section of ['users', 'tariffs', 'contractors', 'dictionaries', 'meter-readings', 'payments', 'funds', 'reports', 'import', 'audit', 'releases', 'settings']) {
      expect(appShellSource).toContain(`workspace--${section}`)
    }
  })

  it('keeps compact meter month labels visually short and fully accessible', () => {
    expect(meterReadingsSource).toContain('aria-label={`${month.label}${selectedMeterType.unit}`}')
    expect(meterReadingsSource).toContain('className="meter-readings-month-full" aria-hidden="true"')
    expect(meterReadingsSource).toContain('className="meter-readings-month-short" aria-hidden="true"')
    expect(appCss).toContain('.meter-readings-month-full {\n    display: none;')
    expect(appCss).toContain('.meter-readings-month-short {\n    display: inline;')
  })

  it('renders compact audit rows and contractor headers with explicit labels', () => {
    expect(auditSource).toContain('data-label="Время"')
    expect(auditSource).toContain('data-label="Кто"')
    expect(auditSource).toContain('data-label="Было"')
    expect(auditSource).toContain('data-label="Стало"')
    expect(contractorsSource).toContain('compactLabel?: string')
    expect(contractorsSource).toContain('contractors-sort-label--compact')
  })

  it('keeps report filter popovers inside the compact viewport', () => {
    expect(appCss).toContain('.report-garage-filter-panel {\n    top: auto;\n    bottom: calc(100% + 8px);')
    expect(appCss).toContain('max-height: calc(100dvh - 48px);')
    expect(appCss).toContain('.report-quick-list-garages .payments-prototype-search-results {\n  position: static;\n  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));\n  width: 100%;')
    expect(appCss).toContain('overflow-x: hidden;\n  overflow-y: auto;')
  })

  it('compresses the ordinary expense form without hiding its action row', () => {
    expect(appCss).toContain('.expense-form {\n    gap: 7px 12px;')
    expect(appCss).toContain('.expense-form textarea {\n    min-height: 48px;\n    height: 48px;')
  })
})
