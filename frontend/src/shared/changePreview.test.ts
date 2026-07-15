import { describe, expect, it } from 'vitest'
import type { ChangePreview } from './changePreview'
import {
  appendChangePreview,
  appendFormattedChangePreview,
  createChangePreview,
  createFormattedChangePreview,
  formatChangeDate,
  formatChangeMoney,
  formatChangeNumber,
  formatChangeText,
  formatSensitiveChange,
} from './changePreview'

describe('change preview helpers', () => {
  it('formats empty and filled values for confirmation dialogs', () => {
    expect(formatChangeText('  Иванов  ')).toBe('Иванов')
    expect(formatChangeText('   ')).toBe('пусто')
    expect(formatChangeText(null)).toBe('пусто')

    expect(formatChangeNumber(12)).toBe('12')
    expect(formatChangeNumber(Number.NaN)).toBe('пусто')
    expect(formatChangeNumber(undefined)).toBe('пусто')

    expect(formatChangeMoney(1200.5)).toBe('1 200.50')
    expect(formatChangeMoney(null)).toBe('пусто')

    expect(formatChangeDate('2026-07-03')).toBe('03.07.2026')
    expect(formatChangeDate('')).toBe('пусто')
  })

  it('does not expose sensitive values in change previews', () => {
    expect(formatSensitiveChange('secret-before')).toBe('изменено')
    expect(formatSensitiveChange('')).toBe('пусто')
    expect(formatSensitiveChange(null)).toBe('пусто')
  })

  it('creates only real before-after changes', () => {
    expect(createChangePreview('Телефон', 'пусто', 'пусто')).toBeNull()
    expect(createChangePreview('Телефон', 'пусто', '+7')).toEqual({
      field: 'Телефон',
      before: 'пусто',
      after: '+7',
    })
  })

  it('appends plain and formatted changes to an existing list', () => {
    const changes: ChangePreview[] = []

    appendChangePreview(changes, 'Адрес', 'пусто', 'пусто')
    appendChangePreview(changes, 'Адрес', 'пусто', 'ул. Ленина')
    appendFormattedChangePreview(changes, 'Сумма', 0, 1500, formatChangeMoney)

    expect(changes).toEqual([
      { field: 'Адрес', before: 'пусто', after: 'ул. Ленина' },
      { field: 'Сумма', before: '0.00', after: '1 500.00' },
    ])
  })

  it('supports custom formatting for select-like values', () => {
    const labels: Record<string, string> = {
      owner1: 'Иванов Иван',
      owner2: 'Петров Петр',
    }

    expect(createFormattedChangePreview('Владелец', 'owner1', 'owner2', (value) => labels[value] ?? value)).toEqual({
      field: 'Владелец',
      before: 'Иванов Иван',
      after: 'Петров Петр',
    })
  })

  it('builds a representative edit confirmation preview without no-op rows or sensitive values', () => {
    const expenseTypeLabels: Record<string, string> = {
      water: 'Водоснабжение',
      electricity: 'Электроэнергия',
    }
    const changes: ChangePreview[] = []

    appendChangePreview(changes, 'Комментарий', formatChangeText('  без изменений  '), formatChangeText('без изменений'))
    appendFormattedChangePreview(changes, 'Сумма', 1500, 1750, formatChangeMoney)
    appendFormattedChangePreview(changes, 'Дата', '2026-07-01', '2026-07-03', formatChangeDate)
    appendFormattedChangePreview(changes, 'Вид начисления', 'water', 'electricity', (value) => expenseTypeLabels[value] ?? value)
    appendChangePreview(changes, 'Пароль', formatSensitiveChange('old-secret'), formatSensitiveChange('new-secret'))
    appendChangePreview(changes, 'Токен интеграции', formatSensitiveChange(''), formatSensitiveChange('fresh-secret'))

    expect(changes).toEqual([
      { field: 'Сумма', before: '1 500.00', after: '1 750.00' },
      { field: 'Дата', before: '01.07.2026', after: '03.07.2026' },
      { field: 'Вид начисления', before: 'Водоснабжение', after: 'Электроэнергия' },
      { field: 'Токен интеграции', before: 'пусто', after: 'изменено' },
    ])
    expect(JSON.stringify(changes)).not.toContain('fresh-secret')
    expect(JSON.stringify(changes)).not.toContain('old-secret')
    expect(JSON.stringify(changes)).not.toContain('new-secret')
  })
})
