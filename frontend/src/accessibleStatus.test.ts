import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('accessible dynamic messages', () => {
  const appSource = readFileSync(resolve(process.cwd(), 'src', 'App.tsx'), 'utf8')
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
        id: 'auth-password-policy-hint',
        fields: ['aria-label="Пароль"'],
      },
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
