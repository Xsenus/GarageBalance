import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('accessible dynamic messages', () => {
  const appSource = readFileSync(resolve(process.cwd(), 'src', 'App.tsx'), 'utf8')
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
  const formFeedbackSource = readFileSync(resolve(process.cwd(), 'src', 'shared', 'formFeedback.tsx'), 'utf8')

  it('keeps polite live regions exposed as statuses in the main workspace', () => {
    const liveRegionLines = appSource
      .split(/\r?\n/)
      .map((line, index) => ({ index: index + 1, line: line.trim() }))
      .filter(({ line }) => line.includes('aria-live="polite"'))

    expect(liveRegionLines.length).toBeGreaterThan(0)
    expect(liveRegionLines.filter(({ line }) => !line.includes('role="status"'))).toEqual([])
  })

  it('keeps shared form errors and validation summaries exposed as alerts', () => {
    expect(appSource).toContain("import { FormError, FormValidationSummary } from './shared/formFeedback'")
    expect(formFeedbackSource).toContain('<div className="form-error" role="alert">')
    expect(formFeedbackSource).toContain('<div className="form-error validation-summary" role="alert" aria-label={title}>')
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

  it('keeps report date format hints linked to date fields', () => {
    const reportDateHints = [
      { id: 'consolidated-report-date-format', text: 'Формат периода сводного отчета: ММ.ГГГГ.' },
      { id: 'income-report-date-format', text: 'Формат дат поступлений: ДД.ММ.ГГГГ.' },
      { id: 'expense-report-date-format', text: 'Формат дат выплат: ДД.ММ.ГГГГ.' },
    ]

    for (const hint of reportDateHints) {
      expect(appSource).toContain(`aria-describedby="${hint.id}"`)
      expect(appSource).toContain(`id="${hint.id}">${hint.text}</p>`)
    }
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

    expect(appSource).toContain('function isValidMeterReadingYear(value: string)')
    expect(appSource).toContain('return year >= 1900 && year <= 9999')
    expect(appSource).toContain('aria-label="Год показаний"')
    expect(appSource).toContain('aria-invalid={!yearIsValid}')
    expect(appSource).toContain('inputMode="numeric"')
    expect(appSource).toContain('maxLength={4}')
    expect(appSource).toContain('Введите год четырьмя цифрами от 1900 до 9999.')
  })

  it('keeps select controls consistently styled and labeled', () => {
    const singleSelectContainers = [
      '.dictionary-form select',
      '.dictionary-modal-form select',
      '.dictionary-pagination select',
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
    expect(appCss).toContain('background-image:\n    linear-gradient(45deg, transparent 50%, #475467 50%),')
    expect(appCss).toContain('padding-right: 34px;')
    expect(appCss).toContain('appearance: auto;')
    expect(appCss).toContain('background-image: none;')
    expect(appCss).toContain('cursor: not-allowed;')
    expect(appCss).toContain('background-color: #f8fafc;')

    const selectOpeningTags = [...appSource.matchAll(/<select\b[\s\S]*?>/g)].map((match) => match[0])

    expect(selectOpeningTags.length).toBeGreaterThan(0)
    expect(selectOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(appSource).toContain('Выберите гараж')
    expect(appSource).toContain('Выберите вид')
    expect(appSource).toContain('Выберите поставщика')
    expect(appSource).toContain('Выберите тариф')
    expect(appSource).toContain('Все')
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

    const inputOpeningTags = [...appSource.matchAll(/<input\b[\s\S]*?>/g)].map((match) => match[0])
    const textareaOpeningTags = [...appSource.matchAll(/<textarea\b[\s\S]*?>/g)].map((match) => match[0])

    expect(inputOpeningTags.length).toBeGreaterThan(0)
    expect(textareaOpeningTags.length).toBeGreaterThan(0)
    expect(inputOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(textareaOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])

    const fieldsWithPlaceholder = [...appSource.matchAll(/<(?:input|textarea)\b[\s\S]*?placeholder=/g)].map((match) => match[0])

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
    expect(appCss).toContain('.contractors-check-row:hover,\n.dictionary-archive-toggle:hover')
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

    const checkboxOpeningTags = [...appSource.matchAll(/<input\b(?=[\s\S]*?\btype="checkbox")[\s\S]*?>/g)].map(
      (match) => match[0],
    )

    expect(checkboxOpeningTags.length).toBeGreaterThan(0)
    expect(checkboxOpeningTags.filter((tag) => !/\saria-label=|\saria-labelledby=/.test(tag))).toEqual([])
    expect(appSource).toContain('Показывать архивные')
    expect(appSource).toContain('Регулярные платежи')
    expect(appSource).toContain('Пороговая тарификация')
    expect(appSource).toContain('Все гаражи')
  })

  it('keeps report export buttons out of filter form submission', () => {
    const reportExportButtons = [
      'Скачать сводный XLSX',
      'Скачать сводный PDF',
      'Скачать поступления XLSX',
      'Скачать поступления PDF',
      'Скачать выплаты XLSX',
      'Скачать выплаты PDF',
    ]

    for (const label of reportExportButtons) {
      const labelIndex = appSource.indexOf(`'${label}'`)
      expect(labelIndex).toBeGreaterThan(-1)

      const buttonStart = appSource.lastIndexOf('<button', labelIndex)
      const buttonEnd = appSource.indexOf('</button>', labelIndex)
      expect(buttonStart).toBeGreaterThan(-1)
      expect(buttonEnd).toBeGreaterThan(labelIndex)

      const buttonSource = appSource.slice(buttonStart, buttonEnd)
      expect(buttonSource).toContain('className="secondary-button"')
      expect(buttonSource).toContain('type="button"')
    }
  })

  it('keeps icon-only buttons named and explicitly typed', () => {
    const iconButtonIndexes = [...appSource.matchAll(/className="icon-button"/g)].map((match) => match.index ?? -1)

    expect(iconButtonIndexes.length).toBeGreaterThan(0)

    for (const classNameIndex of iconButtonIndexes) {
      const buttonStart = appSource.lastIndexOf('<button', classNameIndex)
      const buttonEnd = appSource.indexOf('</button>', classNameIndex)

      expect(buttonStart).toBeGreaterThan(-1)
      expect(buttonEnd).toBeGreaterThan(classNameIndex)

      const buttonSource = appSource.slice(buttonStart, buttonEnd)
      expect(buttonSource).toMatch(/\stype="(?:button|submit)"/)
      expect(buttonSource).toContain('aria-label=')
    }
  })

  it('keeps all buttons explicitly typed', () => {
    const buttonIndexes = [...appSource.matchAll(/<button\b/g)].map((match) => match.index ?? -1)

    expect(buttonIndexes.length).toBeGreaterThan(0)

    for (const buttonStart of buttonIndexes) {
      const openingTagEnd = appSource.indexOf('>', buttonStart)
      expect(openingTagEnd).toBeGreaterThan(buttonStart)

      const openingTagSource = appSource.slice(buttonStart, openingTagEnd + 1)
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
    ]

    const containerStyledButtonPatterns = [
      /\srole="(?:tab|menuitem|row)"/,
      /className=\{activeTab ===/,
      /className=\{activeSection === section/,
      /className=\{section\.key === activeSection/,
    ]

    for (const className of sharedButtonClasses) {
      expect(appSource).toContain(className)
    }

    const buttonIndexes = [...appSource.matchAll(/<button\b/g)].map((match) => match.index ?? -1)

    expect(buttonIndexes.length).toBeGreaterThan(0)

    const unstyledButtons = buttonIndexes
      .map((buttonStart) => {
        const openingTagEnd = appSource.indexOf('>', buttonStart)
        expect(openingTagEnd).toBeGreaterThan(buttonStart)

        return appSource.slice(buttonStart, openingTagEnd + 1)
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
      '>Удалить нерегулярный платеж</span>',
      '>Отменить запись</span>',
      '>Архивировать запись</span>',
      '>Перейти без сохранения</span>',
      '>Отменить без сохранения</span>',
      'aria-label={`Удалить нерегулярный платеж',
    ]
    const destructiveStyleClasses = [
      'danger-button',
      'context-menu-danger',
      'contractors-delete-button',
      'dictionary-row-action-danger',
    ]

    for (const className of destructiveStyleClasses) {
      expect(appSource).toContain(className)
    }

    const buttonSources = [...appSource.matchAll(/<button\b[\s\S]*?<\/button>/g)]
      .map((match) => match[0])
      .filter((buttonSource) => destructiveLabels.some((label) => buttonSource.includes(label)))

    expect(buttonSources.length).toBeGreaterThan(0)

    const unmarkedDestructiveButtons = buttonSources
      .filter((buttonSource) => !destructiveStyleClasses.some((className) => buttonSource.includes(className)))

    expect(unmarkedDestructiveButtons).toEqual([])
  })

  it('keeps form controls explicitly named', () => {
    const formControlIndexes = [...appSource.matchAll(/<(?:input|select|textarea)\b/g)].map((match) => match.index ?? -1)

    expect(formControlIndexes.length).toBeGreaterThan(0)

    for (const controlStart of formControlIndexes) {
      const openingTagEnd = appSource.indexOf('>', controlStart)
      expect(openingTagEnd).toBeGreaterThan(controlStart)

      const openingTagSource = appSource.slice(controlStart, openingTagEnd + 1)
      expect(openingTagSource).toMatch(/\saria-label=|\saria-labelledby=/)
    }
  })

  it('keeps password policy hints linked to password fields', () => {
    const passwordPolicyHints = [
      {
        id: 'own-password-policy-hint',
        fields: ['aria-label="Новый пароль"', 'aria-label="Повтор нового пароля"'],
      },
      {
        id: 'new-user-password-policy-hint',
        fields: ['aria-label="Пароль пользователя"'],
      },
    ]

    for (const hint of passwordPolicyHints) {
      expect(appSource).toContain(`id="${hint.id}">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>`)

      for (const fieldLabel of hint.fields) {
        const fieldIndex = appSource.indexOf(fieldLabel)
        expect(fieldIndex).toBeGreaterThan(-1)

        const fieldEnd = appSource.indexOf('>', fieldIndex)
        expect(fieldEnd).toBeGreaterThan(fieldIndex)

        const fieldSource = appSource.slice(fieldIndex, fieldEnd + 1)
        expect(fieldSource).toContain(`aria-describedby="${hint.id}"`)
      }
    }
  })
})
