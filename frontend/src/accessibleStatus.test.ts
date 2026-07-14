import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('accessible dynamic messages', () => {
  const appSource = readFileSync(resolve(process.cwd(), 'src', 'App.tsx'), 'utf8')
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
  const normalizedAppCss = appCss.replace(/\r\n/g, '\n')
  const formFeedbackSource = readFileSync(resolve(process.cwd(), 'src', 'shared', 'formFeedback.tsx'), 'utf8')
  const authGateSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'auth', 'AuthGate.tsx'), 'utf8')
  const releasePanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'releases', 'ReleasePanel.tsx'), 'utf8')
  const settingsPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'settings', 'PasswordPanel.tsx'), 'utf8')
  const fundsPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'funds', 'FundsPanel.tsx'), 'utf8')
  const importPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'import', 'ImportPanel.tsx'), 'utf8')
  const meterReadingsPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'meterReadings', 'MeterReadingsPanel.tsx'), 'utf8')
  const auditPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'audit', 'AuditPanel.tsx'), 'utf8')
  const reportPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'reports', 'ReportPanel.tsx'), 'utf8')
  const userManagementPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'users', 'UserManagementPanel.tsx'), 'utf8')
  const dictionaryListSource = readFileSync(resolve(process.cwd(), 'src', 'shared', 'DictionaryList.tsx'), 'utf8')
  const dictionaryPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'dictionaries', 'DictionaryPanel.tsx'), 'utf8')
  const tariffsPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'tariffs', 'TariffsAndFeesPanel.tsx'), 'utf8')
  const contractorsPanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'contractors', 'ContractorsPanel.tsx'), 'utf8')
  const financePanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'finance', 'FinancePanel.tsx'), 'utf8')
  const workspacePanelSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'Workspace.tsx'), 'utf8')
  const appShellSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'AppShell.tsx'), 'utf8')
  const workspaceSource = [appSource, authGateSource, releasePanelSource, settingsPanelSource, fundsPanelSource, importPanelSource, meterReadingsPanelSource, auditPanelSource, reportPanelSource, userManagementPanelSource, dictionaryListSource, dictionaryPanelSource, tariffsPanelSource, contractorsPanelSource, financePanelSource, workspacePanelSource, appShellSource].join('\n')

  it('keeps polite live regions exposed as statuses in the main workspace', () => {
    const liveRegionLines = workspaceSource
      .split(/\r?\n/)
      .map((line, index) => ({ index: index + 1, line: line.trim() }))
      .filter(({ line }) => line.includes('aria-live="polite"'))

    expect(liveRegionLines.length).toBeGreaterThan(0)
    expect(liveRegionLines.filter(({ line }) => !line.includes('role="status"'))).toEqual([])
  })

  it('keeps shared form errors and validation summaries exposed as alerts', () => {
    expect(financePanelSource).toContain("import { FormError, FormValidationSummary } from '../../shared/formFeedback'")
    expect(formFeedbackSource).toContain('<div className="form-error" role="alert">')
    expect(formFeedbackSource).toContain('<div className="form-error validation-summary" role="alert" aria-label={title}>')
  })

  it('keeps detail dialogs named, described and modal', () => {
    const dialogLines = workspaceSource
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
    expect(dictionaryListSource).toContain('<ul className="dictionary-list" id={listId}>')

    const disclosureControlLines = dictionaryListSource
      .split(/\r?\n/)
      .map((line, index) => ({ index: index + 1, line: line.trim() }))
      .filter(({ line }) => line.includes('setShowAllItems((value) => !value)'))

    expect(disclosureControlLines.length).toBeGreaterThan(0)
    expect(disclosureControlLines.filter(({ line }) => !line.includes('aria-controls={listId}'))).toEqual([])
    expect(disclosureControlLines.filter(({ line }) => !line.includes('aria-expanded={showAllItems}'))).toEqual([])
  })

  it('keeps report workbook period filters typed and quickly adjustable', () => {
    expect(reportPanelSource).toContain('type="month"')
    expect(reportPanelSource).toContain('type="date"')
    expect(reportPanelSource).toContain('applyPreviousMonth')
    expect(reportPanelSource).toContain('applyToday')
    expect(reportPanelSource).toContain('report-period-button')
    expect(appCss).toContain('.report-workbook-filter')
    expect(appCss).toContain('.report-period-button')
  })
  it('keeps date month and year controls styled and validated', () => {
    const calendarStyleContainers = [
      ".dictionary-form input[type='date']",
      ".dictionary-form input[type='month']",
      ".dictionary-modal-form input[type='date']",
      ".dictionary-modal-form input[type='month']",
      ".report-filter input[type='date']",
      ".report-filter input[type='month']",
      ".balance-history-filters input[type='month']",
      ".finance-period-filter input[type='month']",
      ".audit-filter-grid input[type='date']",
      ".audit-filter-grid input[type='month']",
    ]

    for (const selector of calendarStyleContainers) {
      expect(appCss).toContain(selector)
      expect(appCss).toContain(`${selector}::-webkit-calendar-picker-indicator`)
    }

    expect(appCss).toContain(".audit-filter-grid input:not([type='checkbox']):not([type='radio']):not([type='file'])")
    expect(appCss).toContain('.audit-filter-grid select')
    expect(appCss).toContain(".audit-filter-grid input:not([type='checkbox']):not([type='radio']):not([type='file']):focus")
    expect(appCss).toContain('.audit-filter-grid select:focus')
    expect(appCss).toContain('.meter-readings-control[aria-invalid=\'true\']')

    expect(meterReadingsPanelSource).toContain('function isValidMeterReadingYear(value: string)')
    expect(meterReadingsPanelSource).toContain('return year >= 1900 && year <= 9999')
    expect(meterReadingsPanelSource).toContain('aria-label="Год показаний"')
    expect(meterReadingsPanelSource).toContain('aria-invalid={!yearIsValid}')
    expect(meterReadingsPanelSource).toContain('inputMode="numeric"')
    expect(meterReadingsPanelSource).toContain('maxLength={4}')
    expect(meterReadingsPanelSource).toContain('Введите год четырьмя цифрами от 1900 до 9999.')
  })

  it('keeps select controls consistently styled and labeled', () => {
    const singleSelectContainers = [
      '.dictionary-form select',
      '.dictionary-modal-form select',
      '.report-filter select',
      '.balance-history-filters select',
      '.audit-filter-grid select',
    ]
    const multipleSelectContainers = [
      '.dictionary-form select[multiple]',
      '.dictionary-modal-form select[multiple]',
      '.report-filter select[multiple]',
    ]

    for (const selector of singleSelectContainers) {
      expect(appCss).toContain(selector)
      expect(appCss).toContain(`${selector}:disabled`)
    }

    for (const selector of multipleSelectContainers) {
      expect(appCss).toContain(selector)
    }

    expect(appCss).toContain('appearance: none;')
    expect(normalizedAppCss).toContain('background-image:\n    linear-gradient(45deg, transparent 50%, #475467 50%),')
    expect(appCss).toContain('padding-right: 34px;')
    expect(appCss).toContain('appearance: auto;')
    expect(appCss).toContain('background-image: none;')
    expect(appCss).toContain('cursor: not-allowed;')
    expect(appCss).toContain('background-color: #f8fafc;')

    const selectOpeningTags = [...workspaceSource.matchAll(/<select\b[\s\S]*?>/g)].map((match) => match[0])

    expect(selectOpeningTags.length).toBeGreaterThan(0)
    expect(selectOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(financePanelSource).toContain('aria-label="Гараж для поступления"')
    expect(financePanelSource).toContain('aria-label="Вид выплаты"')
    expect(financePanelSource).toContain('aria-label="Тариф для регулярного начисления"')
    expect(financePanelSource).toContain("pageSizeLabel={getFinanceToolbarLabel('pageSize')}")
    expect(financePanelSource).toContain('<SelectControl aria-label="Месяц поступлений с"')
    expect(financePanelSource).toContain('<SelectControl aria-label="Месяц поступлений по"')
    expect(financePanelSource).toContain('aria-label="Месяц выплат"')
    expect(financePanelSource).not.toContain('<select aria-label="Месяц поступлений с"')
    expect(financePanelSource).not.toContain('<select aria-label="Месяц поступлений по"')
    expect(financePanelSource).not.toContain('<select aria-label="Месяц выплат"')
    expect(financePanelSource).toContain('<LocalizedDatePicker ariaLabel="Дата изменяемого платежа"')
    expect(financePanelSource).toContain('<LocalizedDatePicker ariaLabel="Месяц изменяемого платежа"')
    expect(financePanelSource).toContain("<LocalizedDatePicker ariaLabel={getFinanceToolbarLabel('periodFrom')} mode=\"month\"")
    expect(financePanelSource).toContain("<LocalizedDatePicker ariaLabel={getFinanceToolbarLabel('periodTo')} mode=\"month\"")
    expect(financePanelSource).not.toContain('aria-label="Дата изменяемого платежа" type="date"')
    expect(financePanelSource).not.toContain('aria-label="Месяц изменяемого платежа" type="month"')
    expect(financePanelSource).not.toContain("aria-label={getFinanceToolbarLabel('periodFrom')} type=\"month\"")
    expect(financePanelSource).not.toContain("aria-label={getFinanceToolbarLabel('periodTo')} type=\"month\"")
  })

  it('keeps text inputs and textareas consistently styled and labeled', () => {
    const textInputStyleContainers = [
      ".dictionary-form input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".dictionary-toolbar input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".dictionary-modal-form input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".payments-prototype-modal-form input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".report-filter input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".balance-history-filters input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".finance-period-filter input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".audit-filter-grid input:not([type='checkbox']):not([type='radio']):not([type='file'])",
      ".detail-dialog input:not([type='checkbox']):not([type='radio']):not([type='file'])",
    ]
    const textareaStyleContainers = [
      '.dictionary-form textarea',
      '.dictionary-modal-form textarea',
      '.payments-prototype-modal-form textarea',
      '.detail-dialog textarea',
    ]

    for (const selector of [...textInputStyleContainers, ...textareaStyleContainers]) {
      expect(appCss).toContain(selector)
      expect(appCss).toContain(`${selector}:disabled`)
      expect(appCss).toContain(`${selector}:focus`)
    }

    expect(appCss).toContain('min-height: 40px;')
    expect(appCss).toContain('border: 1px solid #d0d5dd;')
    expect(appCss).toContain('border-radius: 8px;')
    expect(appCss).toContain('outline: 3px solid rgba(46, 144, 250, 0.16);')
    expect(appCss).toContain('min-height: 74px;')
    expect(appCss).toContain('resize: vertical;')
    expect(appCss).toContain('cursor: not-allowed;')
    expect(appCss).toContain('background-color: #f8fafc;')

    const inputOpeningTags = [...workspaceSource.matchAll(/<input\b[\s\S]*?>/g)].map((match) => match[0])
    const textareaOpeningTags = [...workspaceSource.matchAll(/<textarea\b[\s\S]*?>/g)].map((match) => match[0])

    expect(inputOpeningTags.length).toBeGreaterThan(0)
    expect(textareaOpeningTags.length).toBeGreaterThan(0)
    expect(inputOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(textareaOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])

    const fieldsWithPlaceholder = [...workspaceSource.matchAll(/<(?:input|textarea)\b[\s\S]*?placeholder=/g)].map((match) => match[0])

    expect(fieldsWithPlaceholder.length).toBeGreaterThan(0)
    expect(fieldsWithPlaceholder.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
  })

  it('keeps checkbox and toggle controls visible labeled and keyboard reachable', () => {
    const checkboxStyleContainers = [
      ".contractors-check-row input[type='checkbox']",
      ".dictionary-archive-toggle input[type='checkbox']",
    ]

    expect(appCss).toContain('.contractors-check-row')
    expect(appCss).toContain('.dictionary-archive-toggle')
    expect(normalizedAppCss).toContain('.contractors-check-row:hover,\n.dictionary-archive-toggle:hover')
    expect(appCss).toContain(".contractors-check-row:has(input[type='checkbox']:disabled)")
    expect(appCss).toContain(".dictionary-archive-toggle:has(input[type='checkbox']:disabled)")

    for (const selector of checkboxStyleContainers) {
      expect(appCss).toContain(selector)
      expect(appCss).toContain(`${selector}:checked`)
      expect(appCss).toContain(`${selector}:checked::after`)
      expect(appCss).toContain(`${selector}:focus-visible`)
      expect(appCss).toContain(`${selector}:disabled`)
    }

    expect(appCss).toContain('appearance: none;')
    expect(appCss).toContain('width: 18px;')
    expect(appCss).toContain('height: 18px;')
    expect(appCss).toContain('border-radius: 5px;')
    expect(appCss).toContain('outline: 3px solid rgba(46, 144, 250, 0.18);')

    const checkboxOpeningTags = [...workspaceSource.matchAll(/<input\b(?=[\s\S]*?\btype="checkbox")[\s\S]*?>/g)].map(
      (match) => match[0],
    )

    expect(checkboxOpeningTags.length).toBeGreaterThan(0)
    expect(checkboxOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(dictionaryPanelSource).toContain('Показывать архивные')
    expect(workspaceSource).toContain('Регулярные платежи')
    expect(workspaceSource).toContain('Пороговая тарификация')
  })

  it('keeps tabs active focused and responsive', () => {
    const tabContainers = [
      '.contractors-prototype-tabs',
      '.meter-readings-tabs',
      '.payments-prototype-tabs',
      '.finance-tabs',
      '.report-tabs',
      '.import-tabs',
    ]

    for (const selector of tabContainers) {
      expect(appCss).toContain(selector)
      expect(appCss).toContain(`${selector} button`)
      expect(appCss).toContain(`${selector} button:hover`)
      expect(appCss).toContain(`${selector} button.is-active`)
      expect(appCss).toContain(`${selector} button:focus-visible`)
    }

    expect(appCss).toContain('.dictionary-subnav button:focus-visible')
    expect(appCss).toContain('outline: 3px solid rgba(46, 144, 250, 0.18);')
    expect(normalizedAppCss).toContain('.contractors-prototype-tabs,\n  .meter-readings-tabs,')
    expect(normalizedAppCss).toContain('.payments-prototype-tabs button,\n  .contractors-prototype-tabs button,\n  .meter-readings-tabs button')
    expect(appCss).toContain('flex-wrap: wrap;')
    expect(appCss).toContain('grid-template-columns: 1fr;')

    const tabButtonIndexes = [...workspaceSource.matchAll(/role="tab"/g)].map((match) => match.index ?? -1)

    expect(tabButtonIndexes.length).toBeGreaterThan(0)

    for (const roleIndex of tabButtonIndexes) {
      const buttonStart = workspaceSource.lastIndexOf('<button', roleIndex)
      const buttonEnd = workspaceSource.indexOf('</button>', roleIndex)

      expect(buttonStart).toBeGreaterThan(-1)
      expect(buttonEnd).toBeGreaterThan(roleIndex)

      const buttonSource = workspaceSource.slice(buttonStart, buttonEnd)
      expect(buttonSource).toContain('type="button"')
      expect(buttonSource).toContain('aria-selected=')
      expect(buttonSource).toContain('onClick=')
    }
  })

  it('keeps sidebar topbar and dashboard icon navigation labeled titled and focusable', () => {
    expect(appShellSource).toContain('const sidebarToggleLabel = isSidebarExpanded ? \'Свернуть панель\' : \'Развернуть панель\'')
    expect(appShellSource).toContain('aria-label={sidebarToggleLabel} title={sidebarToggleLabel}')
    expect(appShellSource).toContain('aria-label={item.label}')
    expect(appShellSource).toContain('title={item.label}')
    expect(appShellSource).toContain('aria-current={isActive ? \'page\' : undefined}')
    expect(workspacePanelSource).toContain('aria-label={tile.title.replace(\'\\n\', \' \')}')
    expect(workspacePanelSource).toContain('title={tile.title.replace(\'\\n\', \' \')}')
    expect(workspacePanelSource).toContain('aria-label="Назад к выбору раздела" title="Назад к выбору раздела"')
    expect(workspacePanelSource).toContain('aria-label="Уведомления" title="Уведомления"')
    expect(workspacePanelSource).toContain('aria-label="Выйти" title="Выйти"')

    expect(normalizedAppCss).toContain('.nav-item:hover,\n.nav-item:focus-visible,\n.nav-item.active')
    expect(normalizedAppCss).toContain('.nav-item:focus-visible {\n  outline: 3px solid rgba(46, 144, 250, 0.18);')
    expect(normalizedAppCss).toContain('.icon-button:hover:not(:disabled),\n.icon-button:focus-visible')
    expect(normalizedAppCss).toContain('.icon-button:disabled {\n  cursor: not-allowed;')
    expect(appCss).toContain('.topbar-back-button:hover')
    expect(appCss).toContain('.dashboard-tile:hover:not(:disabled)')
    expect(appCss).toContain('.dashboard-tile:focus-visible')
  })

  it('keeps tables scrollable focused and announced', () => {
    const scrollableTableContainers = [
      '.dictionary-table-scroll',
      '.operation-list',
      '.audit-event-table',
      '.contractors-sheet',
      '.contractors-directory-table',
      '.contractors-contacts-preview',
      '.meter-readings-table-shell',
      '.report-workbook-table',
    ]
    const stickyHeaderSnippets = [
      '.dictionary-data-table th {\n  position: sticky;',
      '.meter-readings-title-row {\n  position: sticky;',
      '.meter-readings-month-row {\n  position: sticky;',
      '.contractors-sheet-header {\n  position: sticky;',
      '.contractors-directory-row--header {\n  position: sticky;',
      '.contractors-contacts-row--header {\n  position: sticky;',
      '.operation-row.header {\n  position: sticky;',
      '.report-workbook-row--header {\n  position: sticky;',
    ]

    for (const selector of scrollableTableContainers) {
      expect(appCss).toContain(selector)
    }

    for (const snippet of stickyHeaderSnippets) {
      expect(normalizedAppCss).toContain(snippet)
    }

    expect(appCss).toContain('overflow-x: auto;')
    expect(appCss).toContain('.dictionary-data-table tbody tr:hover')
    expect(appCss).toContain('.operation-row--interactive:focus-visible')
    expect(appCss).toContain('button.operation-row:focus-visible')
    expect(appCss).toContain('.contractors-sheet-row:hover')
    expect(appCss).toContain('.contractors-directory-row:not(.contractors-directory-row--header):hover')
    expect(appCss).toContain('.contractors-contacts-row:not(.contractors-contacts-row--header):hover')
    expect(appCss).toContain('.empty-state')
    expect(appCss).toContain('overflow-wrap: anywhere;')

    const tableOpeningTags = [...workspaceSource.matchAll(/<[^>]+\brole="table"[\s\S]*?>/g)].map((match) => match[0])

    expect(tableOpeningTags.length).toBeGreaterThan(0)
    expect(tableOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(workspaceSource).toContain('role="status" aria-live="polite"')
  })

  it('keeps old report export buttons hidden while workbook layouts are active', () => {
    const removedReportExportButtons = [
      'Скачать сводный XLSX',
      'Скачать сводный PDF',
      'Скачать поступления XLSX',
      'Скачать поступления PDF',
      'Скачать выплаты XLSX',
      'Скачать выплаты PDF',
    ]

    for (const label of removedReportExportButtons) {
      expect(reportPanelSource).not.toContain(label)
    }

    expect(reportPanelSource).toContain('reportWorkbookTabs')
    expect(reportPanelSource).toContain('reports-workbook-panel')
  })
  it('keeps icon-only buttons named and explicitly typed', () => {
    const iconButtonIndexes = [
      ...workspaceSource.matchAll(/className="icon-button"/g),
      ...workspaceSource.matchAll(/className="funds-action-button/g),
    ].map((match) => match.index ?? -1)

    expect(iconButtonIndexes.length).toBeGreaterThan(0)

    for (const classNameIndex of iconButtonIndexes) {
      const buttonStart = workspaceSource.lastIndexOf('<button', classNameIndex)
      const buttonEnd = workspaceSource.indexOf('</button>', classNameIndex)

      expect(buttonStart).toBeGreaterThan(-1)
      expect(buttonEnd).toBeGreaterThan(classNameIndex)

      const buttonSource = workspaceSource.slice(buttonStart, buttonEnd)
      expect(buttonSource).toMatch(/\stype="(?:button|submit)"/)
      expect(buttonSource).toContain('aria-label=')
    }
  })

  it('keeps all buttons explicitly typed', () => {
    const buttonIndexes = [...workspaceSource.matchAll(/<button\b/g)].map((match) => match.index ?? -1)

    expect(buttonIndexes.length).toBeGreaterThan(0)

    for (const buttonStart of buttonIndexes) {
      const openingTagEnd = workspaceSource.indexOf('>', buttonStart)
      expect(openingTagEnd).toBeGreaterThan(buttonStart)

      const openingTagSource = workspaceSource.slice(buttonStart, openingTagEnd + 1)
      expect(openingTagSource).toMatch(/\stype="(?:button|submit)"/)
    }
  })

  it('keeps action buttons on the shared button style families', () => {
    const sharedButtonClasses = [
      'primary-button',
      'secondary-button',
      'ghost-button',
      'icon-button',
      'danger-button',
      'link-button',
      'dashboard-tile',
      'topbar-back-button',
      'nav-item',
      'file-picker-button',
      'operation-row',
      'funds-action-button',
      'payments-prototype-search-option',
    ]

    const containerStyledButtonPatterns = [
      /\srole="(?:tab|menuitem|row)"/,
      /className=\{activeTab ===/,
      /className=\{activeSection === section/,
      /className=\{section\.key === activeSection/,
    ]

    for (const className of sharedButtonClasses) {
      expect(workspaceSource).toContain(className)
    }

    const buttonIndexes = [...workspaceSource.matchAll(/<button\b/g)].map((match) => match.index ?? -1)

    expect(buttonIndexes.length).toBeGreaterThan(0)

    const unstyledButtons = buttonIndexes
      .map((buttonStart) => {
        const openingTagEnd = workspaceSource.indexOf('>', buttonStart)
        expect(openingTagEnd).toBeGreaterThan(buttonStart)

        return workspaceSource.slice(buttonStart, openingTagEnd + 1)
      })
      .filter((openingTagSource) => !sharedButtonClasses.some((className) => openingTagSource.includes(className)))
      .filter((openingTagSource) => !containerStyledButtonPatterns.some((pattern) => pattern.test(openingTagSource)))

    expect(unstyledButtons).toEqual([])
  })

  it('keeps destructive actions visually distinct without leaving the shared style system', () => {
    const destructiveLabels = [
      '>Удалить</span>',
      '>Удалить запись</span>',
      '>Удалить гараж</span>',
      '>Удалить поставщика</span>',
      '>Удалить сотрудника</span>',
      '>Удалить</span>',
      '>Отменить запись</span>',
      '>Архивировать запись</span>',
      '>Перейти без сохранения</span>',
      '>Отменить без сохранения</span>',
    ]
    const destructiveStyleClasses = [
      'danger-button',
      'context-menu-danger',
      'dictionary-row-action-danger',
    ]

    for (const className of destructiveStyleClasses) {
      expect(workspaceSource).toContain(className)
    }

    const buttonSources = [...workspaceSource.matchAll(/<button\b[\s\S]*?<\/button>/g)]
      .map((match) => match[0])
      .filter((buttonSource) => destructiveLabels.some((label) => buttonSource.includes(label)))

    expect(buttonSources.length).toBeGreaterThan(0)

    const unmarkedDestructiveButtons = buttonSources
      .filter((buttonSource) => !destructiveStyleClasses.some((className) => buttonSource.includes(className)))

    expect(unmarkedDestructiveButtons).toEqual([])
  })

  it('keeps form controls explicitly named', () => {
    const formControlIndexes = [...workspaceSource.matchAll(/<(?:input|select|textarea)\b/g)].map((match) => match.index ?? -1)

    expect(formControlIndexes.length).toBeGreaterThan(0)

    for (const controlStart of formControlIndexes) {
      const openingTagEnd = workspaceSource.indexOf('>', controlStart)
      expect(openingTagEnd).toBeGreaterThan(controlStart)

      const openingTagSource = workspaceSource.slice(controlStart, openingTagEnd + 1)
      expect(openingTagSource).toMatch(/\saria-label=|\saria-labelledby=/)
    }
  })

  it('keeps password policy hints linked to password fields', () => {
    const passwordPolicyHints = [
      {
        id: 'own-password-policy-hint',
        fields: ['aria-label="Новый пароль"', 'aria-label="Повтор нового пароля"'],
        source: settingsPanelSource,
      },
      {
        id: 'new-user-password-policy-hint',
        fields: ['aria-label="Пароль пользователя"'],
        source: userManagementPanelSource,
      },
    ]

    for (const hint of passwordPolicyHints) {
      expect(hint.source).toContain(`id="${hint.id}">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>`)

      for (const fieldLabel of hint.fields) {
        const fieldIndex = hint.source.indexOf(fieldLabel)
        expect(fieldIndex).toBeGreaterThan(-1)

        const fieldEnd = hint.source.indexOf('>', fieldIndex)
        expect(fieldEnd).toBeGreaterThan(fieldIndex)

        const fieldSource = hint.source.slice(fieldIndex, fieldEnd + 1)
        expect(fieldSource).toContain(`aria-describedby="${hint.id}"`)
      }
    }
  })
})
