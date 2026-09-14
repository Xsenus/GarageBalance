import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8').replace(/\r\n/g, '\n')
const appShellSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'AppShell.tsx'), 'utf8').replace(/\r\n/g, '\n')
const meterReadingsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'meterReadings', 'MeterReadingsPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const auditSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'audit', 'AuditPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const contractorsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'contractors', 'ContractorsPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const tariffsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'tariffs', 'TariffsAndFeesPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const financeSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'finance', 'FinancePanel.tsx'), 'utf8').replace(/\r\n/g, '\n')
const settingsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'settings', 'PasswordPanel.tsx'), 'utf8').replace(/\r\n/g, '\n')

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

  it('keeps both tariff summary panels beside each other and protects their pagination', () => {
    expect(appCss).toContain('.topbar-back-button {\n    width: 43px;\n    height: 43px;')
    expect(appCss).toContain('.workspace--settings\n  ) > .topbar {\n    position: absolute;')
    expect(appCss).toContain('.workspace--settings\n  ) .user-panel {\n    display: none;')
    expect(appCss).toContain('.tariffs-page .contractors-heading {\n    padding-left: 50px;')
    expect(appCss).toContain('.tariffs-page > .dictionary-pagination {\n  min-height: 54px;')
    expect(appCss).toContain('.tariffs-page .contractors-bottom-grid {\n    grid-template-columns: minmax(320px, var(--tariffs-irregular-width, 40%)) 8px minmax(0, 1fr);')
    expect(appCss).toContain('.tariffs-summary-card {\n    min-height: 280px;')
    expect(appCss).toContain('.tariffs-row-action-button.icon-button {\n    width: 30px;\n    min-width: 30px;')
    expect(appCss).toContain('.tariffs-threshold-range__input {\n  width: 62px;\n  min-width: 62px;')
    expect(appCss).toContain('justify-content: flex-start;')
    expect(tariffsSource).not.toContain('tariffs-threshold-range__unbounded')
    expect(appCss).toContain('.dictionary-pagination--compact {\n  display: flex;\n  flex-wrap: nowrap;')
    expect(appCss).toContain('.irregular-payments-table-scroll,\n.fee-campaign-table-scroll {\n  min-height: 0;\n  flex: 1 1 auto;\n  overflow-x: auto;')
    expect(appCss).toContain('var(--fee-campaign-col-actions, 120px);')
    expect(tariffsSource.match(/compactPageSizeSelect/g)).toHaveLength(2)
    expect(tariffsSource).toContain('useColumnResize(irregularPaymentColumnDefinitions')
    expect(tariffsSource).toContain('useColumnResize(feeCampaignColumnDefinitions')
  })

  it('uses the approved compact toolbar pattern across the pictured sections', () => {
    for (const section of ['contractors', 'meter-readings', 'payments', 'funds', 'reports', 'settings']) {
      expect(appCss).toContain(`.workspace--${section}`)
    }
    expect(contractorsSource).toContain('aria-label="Разделы контрагентов"')
    expect(appCss).toContain('.workspace--contractors .contractors-heading > div:first-child {\n    display: flex;')
    expect(appCss).toContain('.workspace--meter-readings .meter-readings-heading,\n  .workspace--funds .funds-heading {\n    min-height: 43px;\n    padding-left: 50px;')
    expect(appCss).toContain('.workspace--payments .payments-prototype-commandbar {\n    grid-template-columns: max-content minmax(0, 1fr);\n    grid-template-areas: "tabs search";')
    expect(appCss).toContain('.workspace--payments .payments-prototype-search {\n    width: 100%;')
    expect(appCss).toContain('.workspace--payments .payments-prototype > .empty-state {\n    padding-left: 326px;')
    expect(appCss).toContain('.workspace--settings .settings-tab-list {\n    margin-top: 50px;')
    expect(appCss).toContain(') > .topbar {\n    position: absolute;\n    z-index: 2;')
    expect(settingsSource).toContain('aria-orientation="vertical"')
  })

  it('keeps all payout actions in one compact row with shorter visual labels', () => {
    expect(appCss).toContain('.workspace--payments .payments-prototype-actions--sheet {\n    display: grid;\n    grid-template-columns: repeat(6, minmax(0, 1fr));')
    for (const label of ['Начисление', 'Выплата', 'Оклад', 'Премия', 'Штраф']) {
      expect(financeSource).toContain(`data-compact-label="${label}"`)
    }
    for (const accessibleLabel of ['Добавить начисление', 'Добавить выплату', 'Оплатить все', 'Выплатить оклад', 'Начислить премию', 'Начислить штраф']) {
      expect(financeSource).toContain(`aria-label="${accessibleLabel}"`)
    }
  })

  it('keeps report tabs in a single horizontally scrollable strip', () => {
    expect(appCss).toContain('.report-tabs--workbook {\n  display: flex;\n  overflow-x: auto;')
    expect(appCss).toContain('.report-tabs--workbook button {\n  min-width: 150px;\n  flex: 1 0 auto;')
    expect(appCss).not.toContain('grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));')
  })
})
