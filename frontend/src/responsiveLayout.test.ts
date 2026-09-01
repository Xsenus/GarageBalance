// @vitest-environment node
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('responsive layout styles', () => {
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
  const contractorsPanel = readFileSync(resolve(process.cwd(), 'src', 'features', 'contractors', 'ContractorsPanel.tsx'), 'utf8')
  const settingsPanel = readFileSync(resolve(process.cwd(), 'src', 'features', 'settings', 'PasswordPanel.tsx'), 'utf8')
  const normalizedAppCss = appCss.replace(/\r\n/g, '\n')

  function collectFeatureTsxFiles(directory: string): string[] {
    return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
      const path = resolve(directory, entry.name)
      if (entry.isDirectory()) return collectFeatureTsxFiles(path)
      return entry.isFile() && entry.name.endsWith('.tsx') ? [path] : []
    })
  }

  it('stacks the configurable tariff panels and hides their splitter on narrow screens', () => {
    expect(normalizedAppCss).toContain('.tariffs-panels-splitter {\n  align-self: stretch;\n  min-height: 48px;')
    expect(normalizedAppCss).toContain('.tariffs-page .contractors-bottom-grid {\n    grid-template-columns: 1fr;\n    gap: 12px;')
    expect(normalizedAppCss).toContain('.tariffs-panels-splitter {\n    display: none;')
  })

  it('uses shared controls instead of browser-native selects and date pickers', () => {
    const nativeControls = collectFeatureTsxFiles(resolve(process.cwd(), 'src', 'features')).flatMap((path) => {
      const source = readFileSync(path, 'utf8')
      return /<select\b|type=["'](?:date|month)["']/.test(source) ? [path] : []
    })

    expect(nativeControls).toEqual([])
  })

  it('keeps all twelve working sections inside the shared desktop and mobile shell', () => {
    const appShellSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'AppShell.tsx'), 'utf8')
    const sectionCoverage = [
      ['Пользователи', 'users/UserManagementPanel.tsx', 'aria-label="Пользователи"'],
      ['Тарифы и сборы', 'tariffs/TariffsAndFeesPanel.tsx', 'aria-label="Тарифы и сборы"'],
      ['Контрагенты', 'contractors/ContractorsPanel.tsx', 'aria-label="Контрагенты"'],
      ['Справочники', 'dictionaries/DictionaryPanel.tsx', 'aria-label="Справочники"'],
      ['Показания', 'meterReadings/MeterReadingsPanel.tsx', 'aria-label="Показания"'],
      ['Платежи', 'finance/FinancePanel.tsx', 'aria-label="Форма платежей"'],
      ['Фонды', 'funds/FundsPanel.tsx', 'aria-label="Управление фондами"'],
      ['Отчеты', 'reports/ReportPanel.tsx', 'aria-label="Отчеты"'],
      ['Импорт', 'import/ImportPanel.tsx', 'aria-label="Импорт Access"'],
      ['История изменений', 'audit/AuditPanel.tsx', 'aria-label="История изменений"'],
      ['Что нового', 'releases/ReleasePanel.tsx', 'aria-label="Что нового"'],
      ['Настройки', 'settings/PasswordPanel.tsx', 'aria-label="Настройки"'],
    ] as const

    expect(sectionCoverage).toHaveLength(12)
    for (const [navigationLabel, relativePath, accessibleRoot] of sectionCoverage) {
      const source = readFileSync(resolve(process.cwd(), 'src', 'features', ...relativePath.split('/')), 'utf8')
      expect(appShellSource, navigationLabel).toContain(`label: '${navigationLabel}'`)
      expect(source, navigationLabel).toContain(accessibleRoot)
    }

    expect(normalizedAppCss).toContain('@media (max-width: 1100px) {')
    expect(normalizedAppCss).toContain('@media (max-width: 640px) {')
    expect(normalizedAppCss).toContain('.app-shell,\n  .sidebar,\n  .workspace,\n  .topbar,\n  .user-panel {\n    max-width: 100vw;\n    min-width: 0;')
    expect(normalizedAppCss).toContain('.workspace {\n    overflow-x: hidden;\n    padding: 12px;')
  })

  it('collapses the main shell and data rows on tablet width', () => {
    expect(appCss).toContain('@media (max-width: 1100px)')
    expect(normalizedAppCss).toContain('.app-shell {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.operation-row {\n    grid-template-columns: 1fr;')
  })

  it('keeps tablet and mobile navigation compact without stretching the viewport', () => {
    expect(appCss).toContain('@media (max-width: 1100px)')
    expect(normalizedAppCss).toContain('.app-shell {\n    overflow-x: hidden;')
    expect(normalizedAppCss).toContain('.sidebar {\n    width: 100%;\n    max-width: 100vw;')
    expect(normalizedAppCss).toContain('flex-direction: row;\n    align-items: center;')
    expect(normalizedAppCss).toContain('.nav-list {\n    display: flex;\n    width: auto;\n    min-width: 0;\n    flex: 1 1 auto;')
    expect(normalizedAppCss).toContain('.nav-item,\n  .sidebar--collapsed .nav-item {\n    width: 44px;\n    height: 44px;\n    min-width: 44px;')
    expect(normalizedAppCss).toContain('.operation-list {\n    overflow-x: hidden;')
  })

  it('contains responsive workspaces, tariff tables and dictionary actions', () => {
    expect(normalizedAppCss).toContain('.workspace {\n  box-sizing: border-box;\n  max-width: 100%;\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.tariffs-page {\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.tariffs-page > * {\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.dictionary-toolbar {\n    grid-template-columns: minmax(0, 1fr);\n    align-items: stretch;')
    expect(normalizedAppCss).toContain('.dictionary-toolbar .dictionary-archive-toggle,\n  .dictionary-toolbar .create-action-button {\n    width: 100%;')
    expect(normalizedAppCss).toContain('.dictionary-subnav {\n    min-height: 0;\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 1100px) {\n  .dictionary-panel-v2 {\n    min-height: 0;\n    max-height: none;\n    overflow: visible;')
    expect(normalizedAppCss).toContain('.dictionary-panel-v2 {\n    min-height: 0;\n    max-height: none;\n    overflow: visible;')
    expect(normalizedAppCss).toContain('.dictionary-subnav {\n    grid-template-columns: minmax(0, 1fr);')
  })

  it('keeps the service settings beside tariff periods and stacks them on narrower screens', () => {
    expect(normalizedAppCss).toContain('.contractors-modal-form.contractors-modal-form--service-edit {\n  grid-template-columns: minmax(400px, 0.9fr) minmax(540px, 1.35fr);')
    expect(normalizedAppCss).toContain('gap: 0 14px;')
    expect(normalizedAppCss).toContain('"settings-title schedule"\n    "heading schedule"\n    "catalogs schedule"')
    expect(normalizedAppCss).toContain('.contractors-modal-form--service-edit-tiered > .contractors-tier-editor {\n  grid-area: schedule;\n  margin-top: 0;')
    expect(normalizedAppCss).toContain('.tariff-schedule-table {\n  overflow: visible;')
    expect(normalizedAppCss).toContain('.tariff-schedule-row:focus-within {\n  z-index: 40;')
    expect(normalizedAppCss).toContain(".tariff-schedule-row > [role='cell']:nth-child(3) input {\n  text-align: right;")
    expect(normalizedAppCss).toContain('.contractors-threshold-row {\n  display: grid;\n  grid-template-columns: minmax(120px, 0.72fr) minmax(230px, 1.25fr) minmax(230px, 1.25fr) 40px;')
    expect(normalizedAppCss).toContain('.contractors-threshold-row label:last-of-type input {\n  text-align: right;')
    expect(normalizedAppCss).toContain('@media (max-width: 1100px) {\n  .contractors-modal-form.contractors-modal-form--service-edit {\n    grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('"flags"\n      "schedule"\n      "tiers"\n      "actions";')
  })

  it('keeps row actions visible at the right edge of wide mobile tables', () => {
    expect(normalizedAppCss).toContain('@media (max-width: 900px) {\n  .dictionary-data-table th.table-actions-column,')
    expect(normalizedAppCss).toContain('.contractors-directory-row > .table-actions-column,\n  .tariffs-page .contractors-sheet-header > .table-actions-column,')
    expect(normalizedAppCss).toContain('position: sticky;\n    right: 0;\n    z-index: 3;')
    expect(normalizedAppCss).toContain('.table-actions-column .icon-button,\n  .table-actions-column .funds-action-button {\n    min-width: 36px;')
    expect(contractorsPanel).toContain("column.key === 'actions' ? ' table-actions-column' : ''")
  })

  it('allows long table cell text to shrink inside its grid column', () => {
    expect(normalizedAppCss).toContain('.nav-item {\n  display: flex;\n  width: 100%;\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.operation-row > * {\n  min-width: 0;')
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

  it('groups garage filters into shared compact fields and responsive ranges', () => {
    expect(contractorsPanel).toContain('<fieldset className="contractors-column-filters__range">')
    expect(contractorsPanel).toContain('<legend>Количество людей</legend>')
    expect(contractorsPanel).toContain('<legend>Количество этажей</legend>')
    expect(normalizedAppCss).toContain('.contractors-column-filters {\n  display: grid;\n  grid-template-columns: minmax(220px, 1.15fr) repeat(2, minmax(220px, 1fr)) auto;')
    expect(normalizedAppCss).toContain('.contractors-column-filters input {\n  width: 100%;\n  min-height: 40px;\n  border: 1px solid #d0d5dd;\n  border-radius: 8px;')
    expect(normalizedAppCss).toContain('.contractors-column-filters__range > div {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-column-filters__actions {\n    grid-column: auto;\n    display: grid;\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
  })

  it('reserves financial report geometry while its data is loading', () => {
    expect(normalizedAppCss).toContain('.financial-report-dialog {\n  height: min(720px, calc(100dvh - 48px));\n  min-height: 0;\n  display: flex;\n  flex-direction: column;\n  overflow: hidden;')
    expect(normalizedAppCss).toContain('.financial-report-loading-skeleton {\n  flex: 1 1 auto;\n  min-height: 314px;')
    expect(normalizedAppCss).toContain('.financial-report-dialog > .garage-balance-table-scroll {\n  flex: 1 1 auto;\n  min-height: 0;\n  max-height: none;')
    expect(normalizedAppCss).toContain('.financial-report-loading-skeleton .loading-skeleton-row:first-child {\n  min-height: 72px;')
    expect(normalizedAppCss).toContain('.financial-report-button__spinner {\n  animation: report-export-spin 0.8s linear infinite;')
    expect(normalizedAppCss).toContain('.report-export-button__spinner,\n  .financial-report-button__spinner {\n    animation: none;')
  })

  it('wraps quick report periods and keeps dialog presets on their own row', () => {
    expect(normalizedAppCss).toContain('.report-quick-periods {\n  display: flex;\n  flex-wrap: wrap;')
    expect(normalizedAppCss).toContain(".report-quick-periods__button[aria-pressed='true'] {")
    expect(normalizedAppCss).toContain('.balance-history-filters__quick-periods {\n  grid-column: 1 / -1;')
  })

  it('makes the staff dialog wider and taller while keeping it responsive', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-dialog--staff {\n  width: min(700px, calc(100vw - 48px));\n  min-height: 400px;')
    expect(normalizedAppCss).toContain('.contractors-dialog--staff .contractors-modal-form {\n  min-height: 300px;\n  align-content: space-between;')
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-dialog--supplier,\n  .detail-dialog.contractors-dialog--staff {\n    width: 100%;')
  })

  it('opens address suggestions upward and limits the visible rows', () => {
    expect(normalizedAppCss).toContain('.suggestion-options {\n  position: absolute;')
    expect(normalizedAppCss).toContain('max-height: 284px;\n  overflow-y: auto;\n  overscroll-behavior: contain;\n  scrollbar-gutter: stable;')
    expect(normalizedAppCss).toContain('.suggestion-options--above {\n  top: auto;\n  bottom: calc(100% + 6px);')
    expect(normalizedAppCss).toContain('.suggestion-option {\n  display: grid;\n  width: 100%;\n  height: 54px;\n  min-height: 54px;')
    expect(normalizedAppCss).toContain('@media (max-width: 720px) {\n  .suggestion-options {\n    max-height: 176px;')
  })

  it('keeps the tariff threshold action at the left edge of its table cell', () => {
    expect(normalizedAppCss).toContain('.tariffs-add-threshold-button {\n  justify-self: start;')
  })

  it('reserves separate tariff schedule columns and enough room for all row actions', () => {
    expect(normalizedAppCss).toContain('--tariffs-schedule-columns: ;')
    expect(normalizedAppCss).toContain(':is(.tariffs-show-periodicity, .tariffs-show-month) :is(.contractors-sheet-header, .contractors-sheet-row) {\n  --tariffs-schedule-columns: minmax(130px, 0.65fr);\n  min-width: 1240px;')
    expect(normalizedAppCss).toContain('.tariffs-show-periodicity.tariffs-show-month :is(.contractors-sheet-header, .contractors-sheet-row) {\n  --tariffs-schedule-columns: repeat(2, minmax(130px, 0.65fr));\n  min-width: 1370px;')
    expect(normalizedAppCss).toContain('.tariffs-row-actions {\n  display: flex;\n  min-height: 34px;')
    expect(normalizedAppCss).toContain('.tariffs-due-date-cell .contractors-date-value {\n  grid-template-columns: minmax(52px, 64px) minmax(112px, 1fr);\n  align-items: center;')
    expect(normalizedAppCss).toContain('height: 36px;\n  min-height: 36px;\n  box-sizing: border-box;')
  })

  it('keeps audit controls and event details readable at every supported width', () => {
    expect(normalizedAppCss).toContain('.select-control__trigger,\n.localized-date-picker input {\n  width: 100%;')
    expect(normalizedAppCss).toContain('.select-control__list {\n  position: absolute;')
    expect(normalizedAppCss).toContain('.localized-date-picker__popover {\n  position: absolute;')
    expect(normalizedAppCss).toContain('.audit-detail-dialog {\n  width: min(1120px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.audit-detail-grid {\n  grid-template-columns: repeat(3, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.audit-detail-grid {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.audit-detail-grid {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.audit-filter-grid {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
  })

  it('keeps shared dialogs usable on narrow screens without action overlap', () => {
    expect(appCss).toContain('@media (max-width: 640px)')
    expect(normalizedAppCss).toContain('.modal-backdrop {\n    align-items: start;\n    padding: 12px;')
    expect(normalizedAppCss).toContain('.detail-dialog {\n    width: 100%;\n    max-height: calc(100dvh - 24px);\n    padding: 14px;')
    expect(normalizedAppCss).toContain('.detail-dialog-header {\n    top: -14px;\n    margin: -14px -14px 14px;\n    padding: 14px;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions {\n    bottom: -14px;\n    display: grid;\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions button {\n    width: 100%;')
  })

  it('keeps backup and diagnostic status cards readable in settings', () => {
    expect(normalizedAppCss).toContain('.settings-card--backups,\n.settings-card--diagnostics {\n  width: 100%;\n  grid-template-columns: minmax(280px, 0.65fr) minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.settings-card--backups .summary-strip,\n.settings-card--diagnostics .summary-strip {\n  grid-template-columns: repeat(2, minmax(140px, 1fr));')
    expect(normalizedAppCss).toContain('.settings-card--backups .summary-strip strong,\n.settings-card--diagnostics .summary-strip strong {\n  min-width: 0;\n  overflow-wrap: anywhere;')
    expect(normalizedAppCss).toContain('.settings-card--backups .summary-strip,\n  .settings-card--diagnostics .summary-strip {\n    grid-template-columns: 1fr;')
  })

  it('keeps the single garage search and grouped active card responsive', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-heading {\n  margin-bottom: 2px;')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header {\n  display: grid;\n  gap: 10px;\n  min-width: 0;\n  border: 1px solid #dfe4ec;\n  border-radius: 10px;')
    expect(normalizedAppCss).toContain('.payments-prototype-overdue-details {\n  min-width: 0;\n  border: 1px solid #fecdca;')
    expect(normalizedAppCss).toContain('background: #fffafa;\n  padding: 0 14px;\n}\n\n.payments-prototype-overdue-details--expanded {\n  padding-bottom: 14px;')
    expect(normalizedAppCss).toContain('.payments-prototype-overdue-heading {\n  display: flex;\n  align-items: center;\n  justify-content: space-between;\n  flex-wrap: nowrap;')
    expect(normalizedAppCss).toContain('.payments-prototype-overdue-controls {\n  display: inline-flex;\n  flex: 0 0 auto;\n  align-items: center;\n  justify-content: flex-end;\n  gap: 8px;\n  white-space: nowrap;')
    expect(normalizedAppCss).toContain('.payments-prototype-payable > .field-help {\n  grid-column: 1;\n  justify-self: start;')
    expect(normalizedAppCss).toContain('.payments-prototype-payable-amount {\n  grid-column: 2;\n  justify-self: end;')
    expect(normalizedAppCss).toContain('.field-help__tooltip.payments-prototype-calculation-tooltip {\n  width: max-content;\n  max-width: min(420px, calc(100vw - 48px));\n  white-space: nowrap;')
    expect(normalizedAppCss).toContain('.detail-dialog.payments-prototype-calculation-dialog {\n  width: min(920px, 100%);')
    expect(normalizedAppCss).toContain('.payments-prototype-calculation--historical > p {\n  max-width: none;\n  line-height: 1.45;')
    expect(normalizedAppCss).toContain('.payments-prototype-garage-summary {\n  display: grid;\n  grid-template-columns: minmax(180px, 0.8fr) minmax(240px, 1.2fr) minmax(280px, 1.25fr);')
    expect(normalizedAppCss).toContain('.payments-prototype-summary-group {\n  display: grid;\n  align-content: start;\n  gap: 9px;')
    expect(normalizedAppCss).toContain('.payments-prototype-summary-group dl > div {\n  display: grid;\n  grid-template-columns: minmax(0, 1fr) auto;')
    expect(normalizedAppCss).toContain('.payments-prototype-summary-group dd {\n  max-width: 260px;\n  margin: 0;')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n  position: absolute;')
    expect(appCss).toContain('grid-template-columns: repeat(3, minmax(190px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-topline {\n  display: grid;\n  gap: 10px;\n  width: 100%;')
    expect(normalizedAppCss).toContain('.payments-prototype-search {\n  display: flex;\n  align-items: center;\n  width: min(680px, 100%);')
    expect(normalizedAppCss).toContain('.payments-prototype-search-option {\n  display: flex;\n  align-items: center;\n  width: 100%;\n  gap: 10px;\n  border: 0;')
    expect(normalizedAppCss).not.toContain('.payments-prototype-selected-item {')
    expect(normalizedAppCss).not.toContain('.payments-prototype-selected-metrics {')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header > .payments-prototype-actions {\n  display: grid;\n  grid-template-columns: repeat(4, minmax(0, 1fr));\n  gap: 8px;')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header .payments-prototype-action-button {\n  display: inline-flex;\n  align-items: center;\n  justify-content: center;\n  gap: 8px;\n  min-width: 0;\n  min-height: 58px;')
    expect(normalizedAppCss).toContain('line-height: 1.2;\n  text-align: center;\n  white-space: normal;')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header .payments-prototype-action-button span {\n  min-width: 0;\n  text-wrap: balance;')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header .payments-prototype-action-button svg {\n  flex: 0 0 auto;')
    expect(normalizedAppCss).toContain('@media (max-width: 1100px) {')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header > .payments-prototype-actions {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 640px) {')
    expect(normalizedAppCss).toContain('.payments-prototype-workspace-header > .payments-prototype-actions {\n    grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.payments-prototype-garage-summary {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('@media (max-width: 1180px) {\n  .payments-prototype-garage-summary {\n    grid-template-columns: repeat(2, minmax(130px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-summary-group--finances {\n    grid-column: 1 / -1;')
    expect(normalizedAppCss).toContain('.payments-prototype-summary-group--finances {\n    grid-column: auto;')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: repeat(2, minmax(190px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: minmax(0, 1fr);\n    width: calc(100vw - 32px);')
  })

  it('keeps report checkbox filters aligned with the shared responsive search layout', () => {
    expect(normalizedAppCss).toContain('.report-checkbox-picker {\n  display: grid;\n  gap: 6px;')
    expect(normalizedAppCss).toContain('.report-checkbox-picker .payments-prototype-search {\n  display: flex;\n  align-items: center;\n  width: 100%;\n  min-height: 38px;')
    expect(normalizedAppCss).toContain('.report-checkbox-picker-selected-item {\n  display: grid;\n  min-width: 210px;\n  max-width: 320px;')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: minmax(0, 1fr);\n    width: calc(100vw - 32px);')
  })

  it('keeps the signed garage balance column and four period totals readable', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-table--garage {\n  min-width: 1260px;')
    expect(normalizedAppCss).toContain('.payments-prototype-table--garage th:nth-child(2) {\n  width: 32%;')
    expect(normalizedAppCss).toContain('.payments-prototype-table--garage :is(th, td):is(:nth-child(4), :nth-child(5), :nth-child(7), :nth-child(8)) {')
    expect(normalizedAppCss).toContain('white-space: nowrap;')
    expect(normalizedAppCss).toContain('.payments-prototype-period-summary {\n  display: grid;\n  grid-template-columns: repeat(4, minmax(112px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-period-summary {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-period-summary {\n    grid-template-columns: 1fr;')
  })

  it('keeps the expense month calendar within the worksheet and narrow viewport', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-sheet {\n  overflow: visible;')
    expect(normalizedAppCss).toContain('.payments-prototype-period-row .localized-date-picker__popover {\n  right: auto;\n  left: 0;\n  width: min(292px, calc(100vw - 44px));\n  box-sizing: border-box;')
  })

  it('keeps payment history actions compact and horizontal', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-card--history {\n  padding: 0;\n  overflow-x: auto;')
    expect(normalizedAppCss).toContain('.payments-prototype-mini-table {\n  width: 100%;\n  min-width: 820px;')
    expect(normalizedAppCss).toContain('.payments-prototype-history-actions {\n  display: flex;\n  align-items: center;\n  justify-content: flex-end;\n  gap: 4px;\n  flex-wrap: nowrap;')
    expect(normalizedAppCss).toContain('.payments-prototype-history-actions .icon-button {\n  width: 32px;\n  height: 32px;\n  flex: 0 0 32px;')
  })

  it('keeps accrual and bonus form fields, hints and validation aligned', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-modal-form > .form-field:not(.full-payment-field) {\n  grid-column: 1 / -1;\n  display: grid;\n  grid-template-columns: minmax(130px, 180px) minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.payments-prototype-modal-form > .form-field:not(.full-payment-field) > .form-field-hint {\n  grid-column: 2;')
    expect(normalizedAppCss).toContain('.payments-prototype-modal-form > .form-hint,\n.payments-prototype-modal-form > .form-error {\n  grid-column: 2;\n  min-width: 0;\n  margin: 0;\n  box-sizing: border-box;')
  })

  it('recomposes the add-expense dialog into compact paired fields', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.payments-prototype-dialog--wide {\n  width: min(720px, 100%);')
    expect(normalizedAppCss).toContain('.expense-form {\n  grid-template-columns: 1fr 1fr;')
    expect(normalizedAppCss).toContain('.expense-form > :is(.full-payment-field, .form-hint, .form-error) {')
    expect(normalizedAppCss).toContain('@media (max-width: 640px) {')
    expect(normalizedAppCss).toContain('.payments-prototype-modal-form {\n    grid-template-columns: 1fr;')
  })

  it('stacks accrual and bonus form feedback without overlap on narrow screens', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-modal-form > .form-field:not(.full-payment-field) {\n    grid-template-columns: minmax(0, 1fr);\n    display: grid;\n    gap: 6px;')
    expect(normalizedAppCss).toContain('.payments-prototype-modal-form > .form-field:not(.full-payment-field) > .form-field-hint,\n  .payments-prototype-modal-form > .form-hint,\n  .payments-prototype-modal-form > .form-error {\n    grid-column: 1;')
  })

  it('keeps the full payment form and its error at a comfortable width', () => {
    expect(normalizedAppCss).toContain('.full-payment-dialog {\n  width: min(660px, 100%);')
    expect(normalizedAppCss).toContain('.full-payment-form {\n  grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.full-payment-fields {\n  display: grid;\n  grid-template-columns: minmax(0, 1fr) minmax(200px, 240px);')
    expect(normalizedAppCss).toContain('.full-payment-amount input {\n  text-align: right;\n  font-variant-numeric: tabular-nums;')
    expect(normalizedAppCss).toContain('.full-payment-form > .form-error {\n  grid-column: 1;\n  width: 100%;')
  })

  it('widens the owner editor and keeps its identity fields responsive', () => {
    expect(normalizedAppCss).toContain('.dictionary-editor-dialog {\n  box-sizing: border-box;\n  max-width: min(620px, calc(100vw - 32px));\n  overflow-x: hidden;')
    expect(normalizedAppCss).toContain('.dictionary-editor-dialog--owners {\n  width: min(860px, calc(100vw - 32px));\n  max-width: 860px;')
    expect(normalizedAppCss).toContain('.owner-name-grid,\n.owner-contact-grid {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.owner-name-grid__middle-name {\n  grid-column: 1 / -1;')
    expect(normalizedAppCss).toContain('.field-label-with-help {\n  position: relative;')
    expect(normalizedAppCss).toContain('width: min(280px, 100%);')
    expect(normalizedAppCss).toContain('.field-help:hover .field-help__tooltip,\n.field-help:focus .field-help__tooltip {')
    expect(normalizedAppCss).toContain('.owner-name-grid,\n  .owner-contact-grid {\n    grid-template-columns: minmax(0, 1fr);')
  })

  it('keeps the yearly meter table inside the desktop workspace', () => {
    expect(normalizedAppCss).toContain('.workspace--meter-readings {\n  display: flex;\n  height: 100vh;')
    expect(normalizedAppCss).toContain('.meter-readings-page {\n  display: flex;\n  min-height: 0;\n  flex: 1 1 auto;')
    expect(normalizedAppCss).toContain('.meter-readings-table-shell {\n  min-height: 0;\n  flex: 1 1 0;\n  overflow: auto;\n  max-height: none;')
    expect(normalizedAppCss).toContain('.meter-readings-table {\n  width: 100%;\n  min-width: 1368px;')
    expect(normalizedAppCss).toContain('.meter-readings-title-row,\n.meter-readings-month-row,\n.meter-readings-data-row {\n  display: grid;\n  grid-template-columns: 96px repeat(12, minmax(106px, 1fr));')
    expect(normalizedAppCss).toContain('.meter-readings-month-row span {\n  display: grid;\n  grid-template-columns: minmax(0, 1fr) auto;')
    expect(normalizedAppCss).toContain('.meter-readings-data-row input {\n  box-sizing: border-box;\n  width: 100%;')
    expect(normalizedAppCss).toContain('.meter-readings-replacement-marker {\n  position: absolute;\n  z-index: 1;\n  top: 50%;\n  left: 4px;\n  display: grid;\n  width: 17px;\n  height: 17px;\n  place-items: center;\n  line-height: 0;\n  transform: translateY(-50%);')
    expect(normalizedAppCss).toContain('.meter-readings-controls .form-field {\n  display: flex;\n  align-items: center;')
    expect(normalizedAppCss).toContain('.meter-readings-data-row > span:not(:first-child) {\n  display: block;\n  min-height: 32px;')
  })

  it('styles report exports as compact format-specific icon buttons', () => {
    expect(normalizedAppCss).toContain('.report-export-button {\n  position: relative;\n  display: inline-grid;\n  width: 40px;\n  min-width: 40px;\n  height: 40px;\n  border: 1px solid transparent;\n  border-radius: 10px;')
    expect(normalizedAppCss).toContain('.report-export-button--xlsx {\n  border-color: #abefc6;\n  background: #ecfdf3;\n  color: #067647;')
    expect(normalizedAppCss).toContain('.report-export-button--pdf {\n  border-color: #fecdca;\n  background: #fff1f3;\n  color: #b42318;')
  })

  it('keeps contractor pagination visible while the directory table scrolls', () => {
    expect(normalizedAppCss).toContain('.workspace--contractors {\n  display: flex;\n  height: 100dvh;\n  min-height: 0;\n  flex-direction: column;\n  overflow: hidden;\n  box-sizing: border-box;')
    expect(normalizedAppCss).toContain('.workspace--contractors > .contractors-page--directory {\n  display: flex;\n  min-height: 0;\n  flex: 1 1 auto;\n  flex-direction: column;')
    expect(normalizedAppCss).toContain('.contractors-page--directory > .contractors-directory-card > .contractors-directory-table {\n  min-height: 0;\n  flex: 1 1 auto;\n  overflow: auto;')
    expect(normalizedAppCss).toContain('.contractors-page--directory > .contractors-directory-card > .dictionary-pagination {\n  flex: 0 0 auto;')
  })

  it('lets report tables extend down the page while keeping report choices in one row', () => {
    expect(normalizedAppCss).toContain('.workspace--reports {\n  display: flex;\n  min-height: 100dvh;\n  flex-direction: column;\n  overflow: visible;\n  box-sizing: border-box;')
    expect(normalizedAppCss).toContain('.workspace--reports > .reports-workbook-panel {\n  display: flex;\n  flex: 0 0 auto;\n  flex-direction: column;')
    expect(normalizedAppCss).toContain('.report-tabs--workbook {\n  display: flex;\n  overflow-x: auto;')
    expect(normalizedAppCss).toContain('.report-tabs--workbook button {\n  min-width: 150px;\n  flex: 1 0 150px;')
    expect(normalizedAppCss).toContain('.report-workbook-sheet > .report-workbook-table {\n  flex: 0 0 auto;\n  overflow-x: auto;\n  overflow-y: visible;')
    expect(normalizedAppCss).not.toContain('.report-workbook-sheet > .dictionary-pagination {')
  })

  it('keeps fund operations in the right column with visible pagination', () => {
    expect(normalizedAppCss).toContain('.workspace--funds {\n  display: flex;\n  height: 100dvh;\n  min-height: 0;\n  flex-direction: column;\n  overflow: hidden;\n  box-sizing: border-box;')
    expect(normalizedAppCss).toContain('.funds-heading {\n  display: flex;\n  align-items: center;\n  justify-content: space-between;\n  gap: 16px;')
    expect(normalizedAppCss).toContain('.funds-content {\n  display: grid;\n  min-height: 0;\n  flex: 1 1 auto;\n  grid-template-columns: minmax(520px, 0.95fr) minmax(0, 1.05fr);')
    expect(normalizedAppCss).toContain('.funds-left-column {\n  display: flex;\n  width: 100%;\n  min-height: 0;\n  flex-direction: column;')
    expect(normalizedAppCss).toContain('.funds-operations-sheet {\n  display: flex;\n  width: 100%;\n  min-height: 0;\n  flex-direction: column;\n  overflow: hidden;')
    expect(normalizedAppCss).toContain('.funds-operations-table-scroll {\n  min-height: 0;\n  flex: 1 1 auto;\n  overflow: auto;')
    expect(normalizedAppCss).toContain('.funds-operations-sheet > .dictionary-pagination {\n  flex: 0 0 auto;')
    expect(normalizedAppCss).toContain('@media (max-width: 1180px) {\n  .workspace--funds {\n    height: auto;\n    min-height: 100dvh;\n    overflow: visible;')
  })

  it('keeps settings navigation full-height and settings forms compact', () => {
    expect(normalizedAppCss).toContain('.settings-layout {\n  display: grid;\n  grid-template-columns: 240px minmax(0, 1fr);\n  gap: 18px;\n  min-height: calc(100dvh - 210px);')
    expect(normalizedAppCss).toContain('.settings-section-nav {\n  position: sticky;\n  top: 18px;\n  display: grid;\n  min-height: 100%;')
    expect(normalizedAppCss).toContain('.settings-section-content > .password-panel {\n  width: 100%;\n}')
    expect(normalizedAppCss).toContain('.settings-section-content > .password-panel > * {\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.settings-card {\n  width: 100%;')
    expect(normalizedAppCss).toContain('.settings-card--security {\n  grid-template-columns: minmax(220px, 0.55fr) minmax(440px, 1fr);')
    expect(normalizedAppCss).toContain('.settings-card--backups,\n.settings-card--diagnostics {\n  width: 100%;')
    expect(normalizedAppCss).toContain('.settings-card--business-date {\n  width: 100%;\n  grid-template-columns: minmax(280px, 0.65fr) minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('@media (max-width: 1500px) {\n  .settings-card--security,\n  .settings-card--display,\n  .settings-card--backups,\n  .settings-card--diagnostics,\n  .settings-card--business-date,\n  .settings-card--cash-bank {\n    grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.settings-form-actions {\n  display: flex;\n  flex-wrap: wrap;\n  align-items: center;\n  justify-content: flex-start;\n  gap: 8px;')
    expect(normalizedAppCss).toContain('.business-date-salary-form {\n  margin-top: 4px;\n  background: #ffffff;')
    expect(normalizedAppCss).toContain('.settings-card-body {\n  display: grid;\n  min-width: 0;\n  align-content: start;\n  gap: 12px;')
    expect(normalizedAppCss).toContain('.settings-card-intro {\n  align-self: start;')
    expect(normalizedAppCss).toContain('.settings-card-body > .summary-strip {\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-switch-row {\n  display: grid;\n  grid-template-columns: minmax(170px, 1fr) max-content;\n  min-height: 42px;')
    expect(normalizedAppCss).toContain('.settings-layout {\n    grid-template-columns: 1fr;\n    min-height: 0;')
    expect(normalizedAppCss).toContain('.password-panel,\n  .settings-card--security,\n  .settings-card--display,')
    expect(normalizedAppCss).toContain('.settings-card--cash-bank {\n    grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.settings-card-body > .summary-strip {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.settings-form-actions {\n    display: grid;\n    grid-template-columns: 1fr;')
    expect(settingsPanel).not.toContain('className="dialog-heading"')
    expect(settingsPanel).not.toContain('className="dialog-actions dialog-actions--start"')
    expect(settingsPanel.match(/className="detail-dialog-header"/g)).toHaveLength(5)
    expect(settingsPanel.match(/className="detail-dialog-actions"/g)).toHaveLength(5)
  })

  it('stretches and centers cash and bank balance groups', () => {
    expect(normalizedAppCss).toContain('.summary-strip.cash-bank-summary {\n  width: 100%;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.summary-strip.cash-bank-summary strong {\n  white-space: nowrap;\n  font-variant-numeric: tabular-nums;')
    expect(normalizedAppCss).toContain('.cash-bank-action-card {\n  display: grid;\n  gap: 12px;\n  align-content: start;\n  justify-items: stretch;')
    expect(normalizedAppCss).toContain('.cash-bank-action-card > div:first-child {\n  display: flex;\n  align-items: center;\n  justify-content: center;')
    expect(normalizedAppCss).toContain('.cash-bank-action-card > .dialog-actions {\n  display: grid;\n  width: 100%;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.cash-bank-action-card > .dialog-actions > button {\n  width: 100%;\n  min-width: 0;\n  justify-content: center;')
    expect(normalizedAppCss).toContain('.summary-strip.cash-bank-summary,\n  .cash-bank-adjustment-grid,\n  .cash-bank-action-groups {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.cash-bank-history-table th,\n.cash-bank-history-table td {\n  padding: 12px 16px;')
    expect(normalizedAppCss).toContain('.toast-viewport {\n  position: fixed;\n  right: 22px;\n  bottom: 22px;')
  })

  it('keeps the garage editor wide, compact and responsive', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-dialog--garage {\n  width: min(1120px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.contractors-dialog--garage .contractors-modal-form {\n  gap: 10px;')
    expect(normalizedAppCss).toContain('.contractors-garage-form-details,\n.contractors-garage-form-notes {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-garage-form-notes textarea {\n  min-height: 64px;')
    expect(normalizedAppCss).toContain('.contractors-garage-form-columns,\n  .contractors-garage-form-details,\n  .contractors-garage-form-notes {\n    grid-template-columns: 1fr;')
  })

  it('adapts the tariff service dialog to irregular and regular creation modes', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-service-dialog {\n  overflow-x: hidden;\n  transition: width 240ms ease, min-height 240ms ease;')
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-service-dialog--compact {\n  width: min(680px, calc(100vw - 48px));\n  min-height: 0;')
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-service-dialog--regular {\n  width: min(1280px, calc(100vw - 48px));\n  min-height: min(640px, calc(100dvh - 48px));')
    expect(normalizedAppCss).toContain('.contractors-tariff-dialog .detail-dialog-header h3 {\n  flex: 1;\n  text-align: center;')
    expect(normalizedAppCss).toContain('.contractors-service-header-actions {\n  display: inline-flex;\n  align-items: center;\n  gap: 12px;')
    expect(normalizedAppCss).toContain('@media (prefers-reduced-motion: reduce) {\n  .detail-dialog.contractors-service-dialog {\n    transition: none;')
    expect(normalizedAppCss).toContain('.contractors-service-regular-toggle--in-actions {\n    justify-content: space-between;\n    white-space: normal;')
    expect(normalizedAppCss).toContain('.contractors-tariff-dialog .contractors-service-period-grid--catalogs {\n  grid-template-columns: repeat(3, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-tariff-dialog .contractors-inline-field--date .select-control {\n  flex: 1 1 auto;')
    expect(normalizedAppCss).toContain('.contractors-service-cost-grid {\n  display: grid;\n  grid-template-columns: repeat(3, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-service-secondary-grid {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));\n  gap: 12px;\n  align-items: end;')
    expect(normalizedAppCss).toContain('.contractors-service-heading-grid {\n  display: grid;\n  grid-template-columns: minmax(0, 3fr) minmax(220px, 1fr);')
    expect(normalizedAppCss).toContain('.editable-combobox .editable-combobox__input {\n  padding-right: 40px;')
    expect(normalizedAppCss).toContain('.editable-combobox__list {\n  width: 100%;\n  max-height: 294px;\n  scrollbar-gutter: stable;\n  overscroll-behavior: contain;')
    expect(normalizedAppCss).toContain('.contractors-service-cost-field {\n  grid-column: 3;')
    expect(normalizedAppCss).toContain('.contractors-tariff-dialog .contractors-service-period-grid--catalogs,\n  .contractors-tariff-dialog .contractors-service-period-grid--single-row,\n  .contractors-service-heading-grid,\n  .contractors-service-secondary-grid,\n  .contractors-fee-layout,\n  .contractors-fee-two-column-grid,\n  .contractors-fee-date-grid,\n  .contractors-service-period-grid,\n  .contractors-service-flags,\n  .contractors-service-cost-grid {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.contractors-service-cost-field {\n    grid-column: 1;')
  })

  it('keeps the fee dialog wide with separate responsive date columns', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-fee-dialog {\n  width: min(1120px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.contractors-fee-layout {\n  display: grid;\n  grid-template-columns: minmax(360px, 0.85fr) minmax(480px, 1.15fr);')
    expect(normalizedAppCss).toContain('.contractors-fee-card {\n  display: grid;\n  min-width: 0;\n  gap: 12px;')
    expect(normalizedAppCss).toContain('.contractors-fee-calculation-status {\n  display: block;\n  min-height: 32px;')
    expect(normalizedAppCss).toContain('.contractors-fee-participant-list {\n  grid-column: 1 / -1;\n  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));\n  width: 100%;')
    expect(normalizedAppCss).toContain('.contractors-fee-participant-list legend,\n.contractors-fee-participant-list > .form-hint {\n  grid-column: 1 / -1;')
    expect(normalizedAppCss).toContain('@media (max-width: 980px) {\n  .contractors-fee-layout {\n    grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.contractors-fee-two-column-grid,\n.contractors-fee-date-grid {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));\n  gap: 12px;')
    expect(normalizedAppCss).toContain('.contractors-fee-layout,\n  .contractors-fee-two-column-grid,\n  .contractors-fee-date-grid,\n  .contractors-service-period-grid,\n  .contractors-service-flags,\n  .contractors-service-cost-grid {\n    grid-template-columns: 1fr;')
  })

  it('centers the announced-fee period heading and every date on one axis', () => {
    expect(normalizedAppCss).toContain('.fee-period {\n  display: grid;\n  place-content: center;')
  })

  it('keeps the supplier editor wide, compact and responsive', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-dialog--supplier {\n  width: min(1280px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.contractors-dialog--supplier .contractors-modal-form {\n  gap: 9px;')
    expect(normalizedAppCss).toContain('.contractors-supplier-lookup-grid {\n  grid-template-columns: minmax(150px, 0.6fr) minmax(170px, 0.65fr) minmax(170px, 0.65fr) minmax(300px, 1.6fr);')
    expect(normalizedAppCss).toContain('.contractors-supplier-lookup-grid > .form-field {\n  align-content: start;')
    expect(normalizedAppCss).toContain('.contractors-supplier-footer-grid {\n  grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.contractors-supplier-footer-grid .form-field > input,\n.contractors-supplier-footer-grid .form-field > textarea {\n  box-sizing: border-box;\n  height: 76px;\n  min-height: 76px;')
    expect(normalizedAppCss).toContain('.contractors-contacts-preview--editable {\n  min-height: 196px;\n  max-height: 280px;')
    expect(normalizedAppCss).toContain('.contractors-contacts-row > span {\n  min-width: 0;')
    expect(normalizedAppCss).not.toContain('.contractors-contacts-row span {')
    expect(normalizedAppCss).toContain('.contractors-contacts-row--editable > span {\n  padding: 4px 2px;')
    expect(normalizedAppCss).toContain('.contractors-contacts-row--editable > span:first-child {\n  padding-right: 1px;\n  text-align: center;')
    expect(normalizedAppCss).toContain('.contractors-contacts-row--editable > span:nth-child(2) {\n  padding-left: 1px;')
    expect(normalizedAppCss).toContain('.contractors-contacts-row--header > span {\n  text-align: center;')
    expect(normalizedAppCss).toContain('.contractors-supplier-primary-grid,\n  .contractors-supplier-contact-summary-grid,\n  .contractors-supplier-lookup-grid,\n  .contractors-supplier-footer-grid,\n  .contractors-staff-fields {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('@media (min-width: 721px) and (max-width: 980px) {\n  .contractors-supplier-contact-summary-grid,\n  .contractors-supplier-lookup-grid,\n  .contractors-supplier-footer-grid {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
  })

  it('keeps long supplier values inside readable table columns', () => {
    expect(contractorsPanel).toContain("{ key: 'name', label: 'Поставщик', defaultWidth: 220, minWidth: 170 }")
    expect(contractorsPanel).toContain("{ key: 'phone', label: 'Телефон', defaultWidth: 180, minWidth: 168 }")
    expect(contractorsPanel).toContain("{ key: 'debt', label: 'Задолженность', defaultWidth: 160, minWidth: 150 }")
    expect(normalizedAppCss).toContain('.contractors-directory-table--suppliers .contractors-directory-row {\n  grid-template-columns: var(--supplier-col-name, 220px) var(--supplier-col-service, 180px) var(--supplier-col-contactPerson, 210px) var(--supplier-col-phone, 180px) var(--supplier-col-email, 210px) var(--supplier-col-debt, 160px) var(--supplier-col-actions, 132px);')
    expect(normalizedAppCss).toContain('.contractors-supplier-cell {\n  align-self: stretch;\n  line-height: 1.35;\n  overflow-wrap: anywhere;\n  word-break: normal;')
    expect(normalizedAppCss).toContain('.contractors-supplier-cell--phone {\n  overflow-wrap: normal;\n  white-space: nowrap;')
    expect(normalizedAppCss).toContain('.contractors-directory-table--suppliers .contractors-directory-header-cell--debt .contractors-sort-button > span:first-child {\n  overflow-wrap: normal;\n  white-space: nowrap;')
  })

  it('keeps staff rate in the right column and submit actions at the right edge', () => {
    expect(normalizedAppCss).toContain('.contractors-staff-fields {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-staff-rate-field input {\n  text-align: right;\n  font-variant-numeric: tabular-nums;')
    expect(normalizedAppCss).toContain('.contractors-dialog-submit-actions {\n  display: inline-flex;\n  align-items: center;\n  gap: 8px;\n  margin-left: auto;')
    expect(normalizedAppCss).toContain('.contractors-supplier-footer-grid,\n  .contractors-staff-fields {\n    grid-template-columns: 1fr;')
  })

  it('centers fund action columns', () => {
    expect(normalizedAppCss).toContain('.funds-table .funds-table-action-column {\n  width: 132px;\n  text-align: center;')
    expect(normalizedAppCss).toContain('.funds-table-row-actions {\n  display: inline-flex;\n  align-items: center;')
  })

  it('keeps the access matrix in one horizontally scrollable table', () => {
    expect(normalizedAppCss).toContain('.role-matrix-table-scroll {\n  overflow-x: auto;')
    expect(normalizedAppCss).toContain('.role-matrix-table {\n  width: max-content;\n  min-width: 100%;')
    expect(normalizedAppCss).toContain('.role-matrix-table thead th {')
    expect(normalizedAppCss).toContain('white-space: nowrap;')
    expect(normalizedAppCss).toContain('.role-matrix-table th:first-child,\n.role-matrix-table td:first-child {\n  position: sticky;\n  left: 0;')
    expect(normalizedAppCss).not.toContain('.role-matrix-row {')
  })

  it('lays out release notes as an adaptive card grid', () => {
    expect(normalizedAppCss).toContain('.release-list {\n  display: grid;\n  grid-template-columns: repeat(3, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 1280px) {\n  .release-list {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 720px) {\n  .release-list {\n    grid-template-columns: minmax(0, 1fr);')
  })
})
